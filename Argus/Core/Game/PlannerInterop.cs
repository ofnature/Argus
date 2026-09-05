using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Argus.Core.Model;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Argus.Core.Game;

/// <summary>
/// Talks to the in-game voyage planner. Both vessel types use the <c>AirShipExploration</c> addon; submarines add a
/// map select step before it. Selecting a destination dispatches the addon's own event (AtkEventType 35 with an
/// index payload), the technique AutoRetainer uses — it only toggles a row, never deploys.
/// </summary>
internal sealed unsafe class PlannerInterop
{
    public const string PlannerAddon = "AirShipExploration";
    private const string DetailAddon = "AirShipExplorationDetail";

    private static readonly TimeSpan SelectInterval = TimeSpan.FromMilliseconds(180);

    private readonly Queue<int> pending = new();
    private DateTime lastSelectUtc = DateTime.MinValue;

    public string? LastError { get; private set; }

    public bool Applying => pending.Count > 0;

    /// <summary>One row of the planner's destination list.</summary>
    public sealed record Destination(int Index, string NameFull, string NameShort, uint RequiredRank, uint StatusFlag)
    {
        /// <summary>AutoRetainer: rows with flag 0 or 1 accept a click; higher values are out of range / locked.</summary>
        public bool CanBeSelected => StatusFlag is 0 or 1;
    }

    private static AtkUnitBase* Addon(string name)
    {
        var ptr = Service.GameGui.GetAddonByName(name).Address;
        if (ptr == nint.Zero)
            return null;
        var addon = (AtkUnitBase*)ptr;
        return addon->IsVisible && addon->IsReady ? addon : null;
    }

    public bool IsPlannerOpen => Addon(PlannerAddon) != null;

    public bool IsDetailOpen => Addon(DetailAddon) != null;

    /// <summary>Screen position and size of the planner window, for docking the overlay.</summary>
    public (float X, float Y, float W, float H)? PlannerRect()
    {
        var addon = Addon(PlannerAddon);
        if (addon == null)
            return null;
        return (addon->X, addon->Y, addon->GetScaledWidth(true), addon->GetScaledHeight(true));
    }

    /// <summary>Which vessel the open planner belongs to, or null when it cannot be told.</summary>
    public (VesselType Type, int Slot, uint Map)? CurrentVessel()
    {
        if (!IsPlannerOpen)
            return null;

        var hm = HousingManager.Instance();
        if (hm == null || hm->WorkshopTerritory == null)
            return null;

        var subAgent = AgentSubmersibleExploration.Instance();
        if (subAgent != null && subAgent->IsAgentActive() && subAgent->MapId != 0)
        {
            var current = hm->WorkshopTerritory->Submersible.DataPointers[4].Value;
            if (current == null)
                return null;

            var subs = hm->WorkshopTerritory->Submersible.Data;
            for (var i = 0; i < subs.Length; i++)
            {
                if (subs[i].RegisterTime == current->RegisterTime && subs[i].RankId == current->RankId)
                    return (VesselType.Submarine, i, subAgent->MapId);
            }

            return null;
        }

        var active = hm->WorkshopTerritory->Airship.ActiveAirshipId;
        if (active is >= 0 and < 4)
            return (VesselType.Airship, active, 1);

        return null;
    }

    /// <summary>"Fuel: 3/120" style strings the planner shows (fuel, distance, returns-at, voyage time).</summary>
    public (string Fuel, string Distance, string ReturnsAt, string VoyageTime)? Summary()
    {
        var addon = Addon(PlannerAddon);
        if (addon == null)
            return null;
        var r = new PlannerReader(addon);
        return (r.Fuel, r.Distance, r.ReturnsAt, r.VoyageTime);
    }

    /// <summary>Nothing selected yet: the distance readout starts with 0.</summary>
    public bool SelectionIsEmpty()
    {
        var s = Summary();
        if (s == null)
            return false;
        var d = s.Value.Distance.Trim();
        return d.StartsWith("0/") || d.StartsWith("0 /") || d == "0";
    }

    public List<Destination> ReadDestinations()
    {
        var list = new List<Destination>();
        var addon = Addon(PlannerAddon);
        if (addon == null)
            return list;

        var reader = new PlannerReader(addon);
        var rows = reader.Destinations;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (string.IsNullOrEmpty(row.NameFull) && string.IsNullOrEmpty(row.NameShort))
                continue;
            list.Add(new Destination(i, row.NameFull, row.NameShort, row.RequiredRank, row.StatusFlag));
        }

        return list;
    }

    /// <summary>
    /// Queue the route's sectors for selection. Returns false (with <see cref="LastError"/>) when the planner is not
    /// open, already has a selection, or a sector cannot be matched to a row.
    /// </summary>
    public bool ApplyRoute(GameData data, VesselType type, IReadOnlyList<uint> sectors)
    {
        LastError = null;
        if (!IsPlannerOpen)
        {
            LastError = "Open the voyage planner first.";
            return false;
        }

        if (!SelectionIsEmpty())
        {
            LastError = "Clear the current selection in the planner first.";
            return false;
        }

        var rows = ReadDestinations();
        var indices = new List<int>();
        foreach (var id in sectors)
        {
            var sector = data.Sector(type, id);
            var row = rows.FirstOrDefault(r => Matches(r, sector));
            if (row == null)
            {
                LastError = $"{sector.Name} is not listed in the planner (wrong map?).";
                return false;
            }

            if (!row.CanBeSelected)
            {
                LastError = $"{sector.Name} cannot be selected right now (range, fuel or rank).";
                return false;
            }

            indices.Add(row.Index);
        }

        pending.Clear();
        foreach (var i in indices)
            pending.Enqueue(i);
        return true;
    }

    private static bool Matches(Destination row, SectorInfo sector)
    {
        static string Norm(string s) => s.Trim().ToLowerInvariant();
        var full = Norm(row.NameFull);
        var shortName = Norm(row.NameShort);
        var name = Norm(sector.Name);
        if (full == name || shortName == name)
            return true;

        // Submarines: the row may show the destination without the "(A)" suffix, or just the letter.
        var stripped = name.Contains('(') ? name[..name.IndexOf('(')].Trim() : name;
        if (full == stripped || shortName == stripped)
            return true;
        return sector.Letter.Length > 0 && shortName == Norm(sector.Letter);
    }

    /// <summary>Framework tick: pushes one queued selection per interval so the addon can settle between clicks.</summary>
    public void Update(DateTime nowUtc)
    {
        if (pending.Count == 0)
            return;

        var addon = Addon(PlannerAddon);
        if (addon == null)
        {
            pending.Clear();
            LastError = "The planner closed before the route was applied.";
            return;
        }

        if (nowUtc - lastSelectUtc < SelectInterval)
            return;
        lastSelectUtc = nowUtc;

        var index = pending.Dequeue();
        try
        {
            SelectDestination(addon, index);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: selecting planner destination {Index} failed", index);
            pending.Clear();
            LastError = "Selecting a destination failed; see the log.";
        }
    }

    public void Cancel() => pending.Clear();

    // Payload layout as reverse-engineered by AutoRetainer: the addon reads the row index at +16 and walks
    // ptr(+0) → ptr(+168) → int(+172) == 0x0FFFFFFF.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public InputData2* Next;
        [FieldOffset(16)] public int Index;
        [FieldOffset(24)] public byte Flag;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData2
    {
        [FieldOffset(168)] public InputData3* Next;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData3
    {
        [FieldOffset(172)] public int Marker;
    }

    private static void SelectDestination(AtkUnitBase* addon, int index)
    {
        var evt = stackalloc AtkEvent[1];
        var d3 = stackalloc InputData3[1];
        d3[0].Marker = 0x0FFFFFFF;
        var d2 = stackalloc InputData2[1];
        d2[0].Next = d3;
        var d1 = stackalloc InputData[1];
        d1[0].Next = d2;
        d1[0].Index = index;
        d1[0].Flag = 0;
        addon->ReceiveEvent((AtkEventType)35, 0, evt, (AtkEventData*)d1);
    }

    /// <summary>AtkValue layout of the planner (AutoRetainer's ReaderAirShipExploration).</summary>
    private sealed class PlannerReader(AtkUnitBase* unitBase, int beginOffset = 0) : AtkReader(unitBase, beginOffset)
    {
        public string Fuel => ReadString(6) ?? string.Empty;
        public string Distance => ReadString(7) ?? string.Empty;
        public string ReturnsAt => ReadString(8) ?? string.Empty;
        public string VoyageTime => ReadString(9) ?? string.Empty;

        public List<Row> Destinations => Loop<Row>(13, 7, 74);

        public sealed class Row(nint unitBasePtr, int beginOffset = 0) : AtkReader(unitBasePtr, beginOffset)
        {
            public string NameFull => (ReadSeString(1)?.TextValue ?? string.Empty).Trim();
            public string NameShort => (ReadSeString(2)?.TextValue ?? string.Empty).Trim();
            public uint RequiredRank => ReadUInt(4) ?? uint.MaxValue;
            public uint StatusFlag => ReadUInt(6) ?? uint.MaxValue;
        }
    }
}
