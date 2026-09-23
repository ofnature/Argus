using System;
using System.Collections.Generic;
using Argus.Core.Data;
using Argus.Core.Model;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Argus.Core.Game;

/// <summary>
/// Reads the current FC's submarines and airships straight from <see cref="HousingManager"/>. Only valid while the
/// workshop territory is loaded; pointers are never kept across frames.
/// </summary>
internal static unsafe class WorkshopReader
{
    /// <summary>Island Sanctuary also populates WorkshopTerritory (since 6.4); its TerritoryIntendedUse is 49.</summary>
    private const uint IslandSanctuaryIntendedUse = 49;

    public static bool IsWorkshopLoaded()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->WorkshopTerritory == null)
            return false;

        var territory = Service.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(Service.ClientState.TerritoryType);
        return territory is not { } row || row.TerritoryIntendedUse.RowId != IslandSanctuaryIntendedUse;
    }

    public static ulong CurrentFreeCompanyId()
    {
        var proxy = InfoProxyFreeCompany.Instance();
        return proxy == null ? 0 : proxy->Id;
    }

    /// <summary>Snapshot of every registered vessel. Empty when the workshop is not loaded.</summary>
    public static List<Vessel> ReadVessels(ulong fcId, DateTime nowUtc)
    {
        var result = new List<Vessel>();
        if (!IsWorkshopLoaded())
            return result;

        var ws = HousingManager.Instance()->WorkshopTerritory;

        var subs = ws->Submersible.Data;
        for (var i = 0; i < subs.Length; i++)
        {
            ref var d = ref subs[i];
            if (d.RankId == 0)
                continue;

            var v = new Vessel
            {
                FreeCompanyId = fcId,
                Type = VesselType.Submarine,
                Slot = i,
                Name = new ReadOnlySeStringSpan(d.Name).ExtractText(),
                Rank = d.RankId,
                CurrentExp = d.CurrentExp,
                NextLevelExp = d.NextLevelExp,
                Hull = d.HullId,
                Stern = d.SternId,
                Bow = d.BowId,
                Bridge = d.BridgeId,
                RegisterTime = d.RegisterTime,
                ReturnTime = d.ReturnTime,
                Surveillance = d.SurveillanceBase + d.SurveillanceBonus,
                Retrieval = d.RetrievalBase + d.RetrievalBonus,
                Speed = d.SpeedBase + d.SpeedBonus,
                Range = d.RangeBase + d.RangeBonus,
                Favor = d.FavorBase + d.FavorBonus,
                LastSeenUtc = nowUtc,
            };

            foreach (var p in d.CurrentExplorationPoints)
            {
                if (p != 0)
                    v.Points.Add(p);
            }

            v.Condition = ReadCondition(v);
            result.Add(v);
        }

        var airships = ws->Airship.Data;
        for (var i = 0; i < airships.Length; i++)
        {
            ref var d = ref airships[i];
            if (d.RankId == 0)
                continue;

            var airship = new Vessel
            {
                FreeCompanyId = fcId,
                Type = VesselType.Airship,
                Slot = i,
                Name = new ReadOnlySeStringSpan(d.Name).ExtractText(),
                Rank = d.RankId,
                CurrentExp = d.CurrentExp,
                NextLevelExp = d.NextLevelExp,
                Hull = d.HullId,
                Stern = d.SternId,
                Bow = d.BowId,
                Bridge = d.BridgeId,
                RegisterTime = d.RegisterTime,
                ReturnTime = d.ReturnTime,
                Surveillance = d.Surveillance,
                Retrieval = d.Retrieval,
                Speed = d.Speed,
                Range = d.Range,
                Favor = d.Favor,
                LastSeenUtc = nowUtc,
            };
            airship.Condition = ReadCondition(airship);
            result.Add(airship);
        }

        return result;
    }

    /// <summary>
    /// Condition of each installed part, 0-30000, or -1 for a slot that could not be read. Parts are inventory items: a
    /// submarine's sit in HousingInteriorPlacedItems2 from slot*5, an airship's in HousingInteriorPlacedItems1 from
    /// 30 + slot*5 (AutoRetainer's layout). A slot holding anything but the part the vessel reports stays unknown rather
    /// than trusted, which also catches an offset that is wrong.
    /// </summary>
    public static int[] ReadCondition(Vessel vessel)
    {
        var result = new[] { -1, -1, -1, -1 };
        var manager = InventoryManager.Instance();
        if (manager == null)
            return result;

        var container = manager->GetInventoryContainer(vessel.Type == VesselType.Airship
            ? InventoryType.HousingInteriorPlacedItems1
            : InventoryType.HousingInteriorPlacedItems2);
        if (container == null)
            return result;

        var begin = (vessel.Type == VesselType.Airship ? 30 : 0) + vessel.Slot * 5;
        for (var i = 0; i < 4; i++)
        {
            if (begin + i >= container->Size)
                break;

            var item = container->GetInventorySlot(begin + i);
            var expected = PartItems.ItemFor(vessel.Type, vessel.PartRow(i));
            if (item == null || expected == 0 || item->ItemId != expected)
                continue;

            result[i] = item->Condition;
        }

        return result;
    }

    /// <summary>
    /// Whether the workshop's selected vessel is this one, or null when the game is not saying. The game points at the
    /// submarine whose menu is open and keeps the selected airship's index, which lets a repair make sure it lands on
    /// the vessel its button was pressed for.
    /// </summary>
    public static bool? IsSelected(VesselType type, int slot)
    {
        if (!IsWorkshopLoaded())
            return null;

        var ws = HousingManager.Instance()->WorkshopTerritory;
        if (type == VesselType.Airship)
        {
            var active = ws->Airship.ActiveAirshipId;
            if (active >= 4)
                return null;
            return active == slot;
        }

        var current = ws->Submersible.DataPointers[4].Value;
        if (current == null)
            return null;

        // Matched by fields the way the planner overlay does; two candidates means the game is not telling us which.
        var subs = ws->Submersible.Data;
        var match = -1;
        for (var i = 0; i < subs.Length; i++)
        {
            if (subs[i].RankId == 0 || subs[i].RegisterTime != current->RegisterTime || subs[i].RankId != current->RankId)
                continue;
            if (match >= 0)
                return null;
            match = i;
        }

        return match < 0 ? null : match == slot;
    }

    /// <summary>
    /// Why a read did or did not happen this tick, for the Debug page. Comparing successive probes shows whether the
    /// game refreshes the workshop data on its own or only when the voyage panel is opened.
    /// </summary>
    public readonly record struct Probe(
        bool HousingManager,
        bool WorkshopTerritory,
        bool IslandSanctuary,
        uint Territory,
        ulong FreeCompanyId,
        int Submarines,
        int Airships,
        uint FirstSubReturn,
        uint FirstAirReturn);

    public static Probe Inspect()
    {
        var territory = Service.ClientState.TerritoryType;
        var hm = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
        if (hm == null)
            return new Probe(false, false, false, territory, 0, 0, 0, 0, 0);

        var fcId = CurrentFreeCompanyId();
        if (hm->WorkshopTerritory == null)
            return new Probe(true, false, false, territory, fcId, 0, 0, 0, 0);

        var row = Service.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory);
        var island = row is { } r && r.TerritoryIntendedUse.RowId == IslandSanctuaryIntendedUse;

        var ws = hm->WorkshopTerritory;
        int subs = 0, airships = 0;
        uint subReturn = 0, airReturn = 0;

        var subData = ws->Submersible.Data;
        for (var i = 0; i < subData.Length; i++)
        {
            if (subData[i].RankId == 0)
                continue;
            subs++;
            if (subReturn == 0)
                subReturn = subData[i].ReturnTime;
        }

        var airData = ws->Airship.Data;
        for (var i = 0; i < airData.Length; i++)
        {
            if (airData[i].RankId == 0)
                continue;
            airships++;
            if (airReturn == 0)
                airReturn = airData[i].ReturnTime;
        }

        return new Probe(true, true, island, territory, fcId, subs, airships, subReturn, airReturn);
    }

    /// <summary>Items of one kind in the player's bags (what dispatch and repair actually consume); -1 when unavailable.</summary>
    public static int CountInventory(uint itemId)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? -1 : manager->GetInventoryItemCount(itemId, false, false);
    }

    /// <summary>Submarine sector unlock/explored flags for the current FC (HousingManager static getters).</summary>
    public static void ReadSubmarineUnlocks(IEnumerable<uint> sectorRowIds, HashSet<uint> unlocked, HashSet<uint> explored)
    {
        unlocked.Clear();
        explored.Clear();
        foreach (var id in sectorRowIds)
        {
            if (HousingManager.IsSubmarineExplorationUnlocked((byte)id))
                unlocked.Add(id);
            if (HousingManager.IsSubmarineExplorationExplored((byte)id))
                explored.Add(id);
        }
    }
}
