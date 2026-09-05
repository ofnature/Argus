using System;
using System.Collections.Generic;
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

            result.Add(v);
        }

        var airships = ws->Airship.Data;
        for (var i = 0; i < airships.Length; i++)
        {
            ref var d = ref airships[i];
            if (d.RankId == 0)
                continue;

            result.Add(new Vessel
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
            });
        }

        return result;
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
