using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Core.Store;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Argus.Core.Game;

/// <summary>
/// Captures each voyage's haul when the result screen (<c>AirShipExplorationResult</c>, shared by both vessel
/// types) opens. Submarines carry the sector id per gathered slot; airships do not, so their sectors are read back
/// from the "… reached. Commencing survey." lines of the voyage log in the order they were surveyed.
/// </summary>
internal sealed unsafe class LootHook : IDisposable
{
    private const string ResultAddon = "AirShipExplorationResult";

    private readonly Plugin plugin;
    private readonly LootStore store;

    /// <summary>Raised with the new entries after a result was recorded.</summary>
    public event Action<IReadOnlyList<LootEntry>>? Recorded;

    public LootHook(Plugin plugin, LootStore store)
    {
        this.plugin = plugin;
        this.store = store;
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ResultAddon, OnResult);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, ResultAddon, OnResult);
    }

    public void Dispose()
    {
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ResultAddon, OnResult);
        Service.AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, ResultAddon, OnResult);
    }

    private void OnResult(AddonEvent type, AddonArgs args)
    {
        try
        {
            Capture((AddonAirShipExplorationResult*)args.Addon.Address);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: failed to read the voyage result");
        }
    }

    private void Capture(AddonAirShipExplorationResult* addon)
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->WorkshopTerritory == null)
            return;

        var fcId = WorkshopReader.CurrentFreeCompanyId();
        if (fcId == 0)
            return;

        var ws = hm->WorkshopTerritory;
        var now = DateTime.UtcNow;
        var data = plugin.Data;
        var entries = new List<LootEntry>();

        var subAgent = AgentSubmersibleExplorationResult.Instance();
        if (subAgent != null && subAgent->IsAgentActive())
        {
            var cur = ws->Submersible.DataPointers[4].Value;
            if (cur == null)
                return;

            var slot = -1;
            var subs = ws->Submersible.Data;
            for (var i = 0; i < subs.Length; i++)
            {
                if (subs[i].RegisterTime == cur->RegisterTime && subs[i].RankId == cur->RankId)
                    slot = i;
            }

            if (slot < 0)
                return;

            var gathered = cur->GatheredData;
            var expTotal = 0u;
            for (var i = 0; i < gathered.Length; i++)
                if (gathered[i].Point > 0)
                    expTotal += gathered[i].ExpGained;
            if (gathered.Length == 0 || gathered[0].ItemIdPrimary == 0)
                return;

            var rank = RankSim.OriginalRank(data, VesselType.Submarine, cur->RankId, cur->CurrentExp, expTotal);
            var build = SafeBuild(VesselType.Submarine, rank, cur->HullId, cur->SternId, cur->BowId, cur->BridgeId);
            var name = new Lumina.Text.ReadOnly.ReadOnlySeStringSpan(cur->Name).ExtractText();

            for (var i = 0; i < gathered.Length; i++)
            {
                ref var g = ref gathered[i];
                if (g.Point == 0)
                    continue;

                entries.Add(new LootEntry
                {
                    FreeCompanyId = fcId,
                    Type = VesselType.Submarine,
                    Slot = slot,
                    VesselName = name,
                    Voyage = cur->RegisterTime,
                    CollectedUtc = now,
                    Rank = rank,
                    Hull = cur->HullId, Stern = cur->SternId, Bow = cur->BowId, Bridge = cur->BridgeId,
                    Surveillance = build?.Surveillance ?? cur->SurveillanceBase + cur->SurveillanceBonus,
                    Retrieval = build?.Retrieval ?? cur->RetrievalBase + cur->RetrievalBonus,
                    Speed = build?.Speed ?? cur->SpeedBase + cur->SpeedBonus,
                    Range = build?.Range ?? cur->RangeBase + cur->RangeBonus,
                    Favor = build?.Favor ?? cur->FavorBase + cur->FavorBonus,
                    Sector = g.Point,
                    ExpGained = g.ExpGained,
                    PrimaryItem = g.ItemIdPrimary, PrimaryCount = g.ItemCountPrimary, PrimaryHq = g.ItemHQPrimary,
                    AdditionalItem = g.ItemIdAdditional, AdditionalCount = g.ItemCountAdditional, AdditionalHq = g.ItemHQAdditional,
                    Rating = g.PointRating.ToString(),
                    DoubleDip = g.DoubleDip,
                    FirstExploration = g.FirstExploration,
                    UnlockedSector = g.UnlockedPoint,
                    SlotUnlocked = g.AdditionalSubmarineUnlocked,
                });
            }
        }
        else
        {
            var airAgent = AgentAirshipExplorationResult.Instance();
            if (airAgent == null || !airAgent->IsAgentActive())
                return;

            var id = ws->Airship.ActiveAirshipId;
            if (id >= 4)
                return;

            ref var a = ref ws->Airship.Data[id];
            var gathered = a.GatheredData;
            var sectors = ReachedSectors(addon, data);
            var expTotal = 0u;
            var valid = 0;
            for (var i = 0; i < gathered.Length; i++)
            {
                if (gathered[i].ItemIdPrimary == 0 && gathered[i].ExpGained == 0)
                    continue;
                expTotal += gathered[i].ExpGained;
                valid++;
            }

            if (valid == 0)
                return;

            var rank = RankSim.OriginalRank(data, VesselType.Airship, a.RankId, a.CurrentExp, expTotal);
            var build = SafeBuild(VesselType.Airship, rank, a.HullId, a.SternId, a.BowId, a.BridgeId);
            var name = new Lumina.Text.ReadOnly.ReadOnlySeStringSpan(a.Name).ExtractText();

            var k = 0;
            for (var i = 0; i < gathered.Length; i++)
            {
                ref var g = ref gathered[i];
                if (g.ItemIdPrimary == 0 && g.ExpGained == 0)
                    continue;

                var sector = k < sectors.Count ? sectors[k] : 0u;
                k++;
                entries.Add(new LootEntry
                {
                    FreeCompanyId = fcId,
                    Type = VesselType.Airship,
                    Slot = id,
                    VesselName = name,
                    Voyage = a.RegisterTime,
                    CollectedUtc = now,
                    Rank = rank,
                    Hull = a.HullId, Stern = a.SternId, Bow = a.BowId, Bridge = a.BridgeId,
                    Surveillance = build?.Surveillance ?? a.Surveillance,
                    Retrieval = build?.Retrieval ?? a.Retrieval,
                    Speed = build?.Speed ?? a.Speed,
                    Range = build?.Range ?? a.Range,
                    Favor = build?.Favor ?? a.Favor,
                    Sector = sector,
                    ExpGained = g.ExpGained,
                    PrimaryItem = g.ItemIdPrimary, PrimaryCount = g.ItemCountPrimary, PrimaryHq = g.AirshipItemValidPrimary,
                    AdditionalItem = g.ItemIdAdditional, AdditionalCount = g.ItemCountAdditional, AdditionalHq = g.AirshipItemValidAdditional,
                });
            }

#if DEBUG
            AirshipLearner.Record(plugin, entries);
#endif
        }

        if (entries.Count == 0 || store.Contains(entries[0].Key))
            return;

        var added = store.Add(entries);
        if (added > 0)
        {
            Service.Log.Information("Argus: recorded {Count} sector hauls for {Vessel}", added, entries[0].VesselName);
            Recorded?.Invoke(entries);
        }
    }

    private Build? SafeBuild(VesselType type, int rank, ushort hull, ushort stern, ushort bow, ushort bridge)
    {
        try { return Build.From(plugin.Data, type, rank, hull, stern, bow, bridge); }
        catch (KeyNotFoundException) { return null; }
    }

    /// <summary>Airship sectors in survey order, parsed from the voyage log's "reached" lines.</summary>
    private static List<uint> ReachedSectors(AddonAirShipExplorationResult* addon, GameData data)
    {
        var result = new List<uint>();
        if (addon == null)
            return result;

        var count = (int)Math.Min(addon->VoyageLogEntryCount, 200u);
        var entries = addon->VoyageLogEntries;
        for (var i = 0; i < count; i++)
        {
            var line = entries[i].String.ToString();
            if (string.IsNullOrEmpty(line) || !line.Contains("reached", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var s in data.AirshipSectors.Values)
            {
                if (!s.IsDestination)
                    continue;
                if (line.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(s.Id);
                    break;
                }
            }
        }

        return result;
    }
}

#if DEBUG
/// <summary>
/// DEBUG-only: appends one JSON line per airship sector haul to <c>airship-learn.jsonl</c> in the config directory
/// so the rating model in <see cref="Argus.Core.Data.AirshipData"/> can be refitted from real results. Release
/// builds compile none of this.
/// </summary>
internal static class AirshipLearner
{
    public static void Record(Plugin plugin, IReadOnlyList<LootEntry> entries)
    {
        try
        {
            var path = Path.Combine(Service.PluginInterface.GetPluginConfigDirectory(), "airship-learn.jsonl");
            using var w = File.AppendText(path);
            foreach (var e in entries)
            {
                if (e.Type != VesselType.Airship || e.Sector == 0 && e.ExpGained == 0)
                    continue;
                var baseExp = plugin.Data.AirshipSectors.TryGetValue(e.Sector, out var s) ? s.Exp : 0u;
                var rating = baseExp == 0 ? -1.0 : Math.Round((e.ExpGained / (double)baseExp - 1.0) / Argus.Core.Data.AirshipData.BonusPerRatingStep, 2);
                w.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    e.CollectedUtc, e.Sector, e.Rank, e.Surveillance, e.Retrieval, e.Speed, e.Range, e.Favor,
                    e.ExpGained, BaseExp = baseExp, Rating = rating,
                    e.PrimaryItem, e.PrimaryCount, e.AdditionalItem, e.AdditionalCount,
                }));
            }
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Argus: airship learner write failed");
        }
    }
}
#endif
