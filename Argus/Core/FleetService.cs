using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Argus.Core.Calc;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Core.Store;
using Argus.Windows;

namespace Argus.Core;

/// <summary>
/// Owns the fleet store and refreshes it from the workshop each frame the player is standing in one. Everything the
/// UI and the DTR entry need about "what is out and what is back" comes from here.
/// </summary>
internal sealed class FleetService
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    private readonly Configuration config;
    private DateTime lastSave = DateTime.MinValue;
    private bool wasInWorkshop;

    public FleetStore Store { get; }

    /// <summary>FC whose workshop the player is standing in right now, 0 otherwise.</summary>
    public ulong CurrentFreeCompanyId { get; private set; }

    public bool InWorkshop { get; private set; }

    /// <summary>When a workshop read last returned at least one vessel.</summary>
    public DateTime LastVesselReadUtc { get; private set; }

    /// <summary>
    /// In the workshop and the client has actually populated the vessel arrays. False while standing there before the
    /// Voyage Control Panel has been used, when everything reads as zero.
    /// </summary>
    public bool HasLiveVessels => InWorkshop && DateTime.UtcNow - LastVesselReadUtc < TimeSpan.FromSeconds(5);

    /// <summary>Last workshop probe, for the Debug page.</summary>
    public WorkshopReader.Probe LastProbe { get; private set; }

    private readonly List<string> log = new();

    /// <summary>Newest first: every change in what the workshop read can see.</summary>
    public IReadOnlyList<string> Log => log;

    private void Note(string text)
    {
        log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        if (log.Count > 24)
            log.RemoveAt(log.Count - 1);
    }

    /// <summary>Raised when the vessel list of an FC changed (new dispatch, return collected, rank up...).</summary>
    public event Action<ulong>? FleetChanged;

    /// <summary>Raised on the frame the player enters a workshop.</summary>
    public event Action? WorkshopEntered;

    public FleetService(Configuration config, string configDirectory)
    {
        this.config = config;
        Store = new FleetStore(configDirectory);
    }

    public void Update(DateTime nowUtc)
    {
        // The probe is a pure read; a change in it is exactly what the Debug page needs to see.
        var probe = WorkshopReader.Inspect();
        if (!probe.Equals(LastProbe))
        {
            Note(probe.WorkshopTerritory
                ? $"workshop visible · FC {probe.FreeCompanyId:X} · {probe.Submarines} subs, {probe.Airships} airships · returns {probe.FirstSubReturn}/{probe.FirstAirReturn}"
                : $"workshop not readable (HousingManager {(probe.HousingManager ? "ok" : "null")}, territory {probe.Territory})");
            LastProbe = probe;
        }

        InWorkshop = Service.ClientState.IsLoggedIn && WorkshopReader.IsWorkshopLoaded();
        if (InWorkshop && !wasInWorkshop)
            WorkshopEntered?.Invoke();
        wasInWorkshop = InWorkshop;

        if (InWorkshop)
            RefreshFromWorkshop(nowUtc);
        else
            CurrentFreeCompanyId = 0;

        if (nowUtc - lastSave >= SaveInterval)
        {
            lastSave = nowUtc;
            Store.SaveIfDirty();
        }
    }

    private void RefreshFromWorkshop(DateTime nowUtc)
    {
        var fcId = WorkshopReader.CurrentFreeCompanyId();
        CurrentFreeCompanyId = fcId;
        if (fcId == 0)
            return;

        var vessels = WorkshopReader.ReadVessels(fcId, nowUtc);
        if (vessels.Count == 0)
            return;

        LastVesselReadUtc = nowUtc;

        var record = Store.GetOrCreate(fcId);
        var player = Service.ObjectTable.LocalPlayer;
        if (player != null)
        {
            var tag = player.CompanyTag.TextValue;
            var world = player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
            if (record.Tag != tag || record.World != world || record.CharacterName != player.Name.TextValue)
            {
                record.Tag = tag;
                record.World = world;
                record.CharacterName = player.Name.TextValue;
                record.ContentId = Service.PlayerState.ContentId;
                Store.MarkDirty();
            }
        }

        if (Store.UpdateVessels(fcId, vessels, nowUtc))
        {
            RefreshUnlocks(record);
            Note($"fleet stored: {vessels.Count} vessels for FC {fcId:X}");
            FleetChanged?.Invoke(fcId);
        }

        RefreshSupplies(record, nowUtc);
    }

    private static readonly TimeSpan SuppliesInterval = TimeSpan.FromSeconds(3);
    private DateTime lastSupplies = DateTime.MinValue;

    private void RefreshSupplies(FreeCompanyRecord record, DateTime nowUtc)
    {
        if (nowUtc - lastSupplies < SuppliesInterval)
            return;
        lastSupplies = nowUtc;

        var tanks = WorkshopReader.CountInventory(Supplies.CeruleumTankItem);
        var kits = WorkshopReader.CountInventory(Supplies.MagitekRepairMaterialsItem);
        if (tanks < 0 || kits < 0)
            return;

        if (record.CeruleumTanks != tanks || record.MagitekRepairMaterials != kits)
        {
            record.CeruleumTanks = tanks;
            record.MagitekRepairMaterials = kits;
            record.SuppliesSeenUtc = nowUtc;
            Store.MarkDirty();
        }
    }

    private void RefreshUnlocks(FreeCompanyRecord record)
    {
        var unlocked = new HashSet<uint>();
        var explored = new HashSet<uint>();
        WorkshopReader.ReadSubmarineUnlocks(Sheets.SubmarineSectorIds, unlocked, explored);
        if (!unlocked.SetEquals(record.UnlockedSubSectors) || !explored.SetEquals(record.ExploredSubSectors))
        {
            record.UnlockedSubSectors = unlocked;
            record.ExploredSubSectors = explored;
            Store.MarkDirty();
        }
    }

    public IEnumerable<FreeCompanyRecord> VisibleCompanies()
        => Store.Companies.Where(c => !config.HiddenFreeCompanies.Contains(c.Id));

    public IEnumerable<Vessel> VisibleVessels()
        => Store.AllVessels(config.HiddenFreeCompanies);

    public readonly record struct Counts(int Total, int Out, int Returned, int Idle, Vessel? NextReturn)
    {
        /// <summary>Vessels the player can act on right now: returned or never dispatched.</summary>
        public int Ready => Returned + Idle;
    }

    public Counts CountsFor(VesselType type, DateTime nowUtc)
    {
        var total = 0;
        var outCount = 0;
        var returned = 0;
        var idle = 0;
        Vessel? next = null;
        foreach (var v in VisibleVessels())
        {
            if (v.Type != type)
                continue;

            total++;
            if (v.IsReturned(nowUtc)) returned++;
            else if (v.IsOut(nowUtc))
            {
                outCount++;
                if (next == null || v.ReturnTime < next.ReturnTime)
                    next = v;
            }
            else idle++;
        }

        return new Counts(total, outCount, returned, idle, next);
    }

    /// <summary>Status pill text for the header: what deserves attention first.</summary>
    public (string Status, string Detail, Vector4 Accent) HeaderStatus(DateTime nowUtc)
    {
        var subs = CountsFor(VesselType.Submarine, nowUtc);
        var air = CountsFor(VesselType.Airship, nowUtc);
        if (subs.Total + air.Total == 0)
            return ("NO FLEET", "visit a company workshop", Styling.AccentTealSoft);

        var ready = subs.Ready + air.Ready;
        if (ready > 0)
            return ("READY", $"{ready} vessel{(ready == 1 ? "" : "s")} waiting in the workshop", Styling.AccentAmber);

        var next = subs.NextReturn;
        if (air.NextReturn != null && (next == null || air.NextReturn.ReturnTime < next.ReturnTime))
            next = air.NextReturn;

        return next == null
            ? ("WATCHING", "all vessels out", Styling.AccentTeal)
            : ("WATCHING", $"next back: {next.Name} in {Formatting.Duration(next.Remaining(nowUtc))}", Styling.AccentTeal);
    }
}
