using System.Collections.Generic;
using System.Linq;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>
/// Sector unlock chains for both vessel types. Exploring a sector can unlock other sectors, open the next map, or
/// register an extra vessel slot; the planner uses this for progression badges and the auto-included next step.
/// </summary>
public static class Progression
{
    /// <summary>What exploring one sector grants.</summary>
    public sealed record Grants(IReadOnlyList<uint> Sectors, bool NewMap, int SlotNumber)
    {
        public bool NewSlot => SlotNumber > 0;
        public bool Any => Sectors.Count > 0 || NewMap || NewSlot;
        public static readonly Grants None = new([], false, 0);
    }

    /// <summary>The next progression step: visit <see cref="VisitSector"/>, which grants <see cref="Rewards"/>.</summary>
    public sealed record NextStep(uint VisitSector, Grants Rewards);

    /// <summary>Everything exploring <paramref name="sector"/> grants.</summary>
    public static Grants GrantsOf(VesselType type, uint sector)
    {
        var sectors = new List<uint>();
        var newMap = false;
        var slot = 0;

        if (type == VesselType.Submarine)
        {
            foreach (var (id, from) in SubmarineData.SectorToUnlock)
            {
                if (IsSector(from.Sector) && (uint)from.Sector == sector)
                    sectors.Add(id);
            }

            if (SubmarineData.SectorToUnlock.TryGetValue(sector, out var self))
            {
                newMap = self.Map;
                if (self.Sub)
                    slot = SubmarineSlotNumber(sector);
            }
        }
        else
        {
            foreach (var (id, from) in AirshipData.UnlockedFrom)
            {
                if (from == sector)
                    sectors.Add(id);
            }

            AirshipData.SlotUnlocks.TryGetValue(sector, out slot);
        }

        sectors.Sort();
        return new Grants(sectors, newMap, slot);
    }

    private static bool IsSector(SubmarineData.SectorType t)
        => t is not (SubmarineData.SectorType.Begin or SubmarineData.SectorType.Map or SubmarineData.SectorType.UnknownUnlock);

    /// <summary>Submarine slots come from the sectors flagged <c>Sub</c>, in row order (2nd, 3rd, 4th).</summary>
    public static int SubmarineSlotNumber(uint sector)
    {
        var slots = SubmarineData.SectorToUnlock.Where(kv => kv.Value.Sub).Select(kv => kv.Key).OrderBy(k => k).ToList();
        var i = slots.IndexOf(sector);
        return i < 0 ? 0 : i + 2;
    }

    public static bool GrantsSlot(VesselType type, uint sector)
        => type == VesselType.Submarine
            ? SubmarineData.SectorToUnlock.TryGetValue(sector, out var u) && u.Sub
            : AirshipData.SlotUnlocks.ContainsKey(sector);

    /// <summary>The sector that must be explored to unlock <paramref name="sector"/>, or null when it is open from the start or unknown.</summary>
    public static uint? UnlockedBy(VesselType type, uint sector)
    {
        if (type == VesselType.Submarine)
        {
            if (!SubmarineData.SectorToUnlock.TryGetValue(sector, out var u) || !IsSector(u.Sector))
                return null;
            return (uint)u.Sector;
        }

        return AirshipData.UnlockedFrom.TryGetValue(sector, out var from) ? from : null;
    }

    /// <summary>Chain of sectors from the start to <paramref name="finalSector"/>, in visiting order (crosses maps).</summary>
    public static List<uint> PathTo(VesselType type, uint finalSector)
    {
        var path = new List<uint>();
        uint? current = finalSector;
        var guard = 0;
        while (current is { } c && guard++ < 512)
        {
            path.Add(c);
            current = UnlockedBy(type, c);
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// The next unlock a vessel can work on: the first locked sector on the main line whose unlocking sector is
    /// reachable (unlocked, on this map, within rank and surveillance), or an unlocked-but-unexplored sector that
    /// grants a slot or map. Null when nothing reachable is left.
    /// </summary>
    public static NextStep? Next(GameData data, VesselType type, IReadOnlySet<uint> unlocked, IReadOnlySet<uint> explored,
        int rank, int surveillance, uint map)
    {
        var sectors = data.Sectors(type);

        bool Reachable(uint id)
            => sectors.TryGetValue(id, out var info)
               && info.IsDestination
               && (type == VesselType.Airship || info.Map == map)
               && info.RankReq <= rank
               && info.SurveillanceReq <= surveillance
               && unlocked.Contains(id);

        foreach (var target in MainLine(type))
        {
            if (unlocked.Contains(target))
                continue;

            if (UnlockedBy(type, target) is { } visit && Reachable(visit))
                return new NextStep(visit, GrantsOf(type, visit));
        }

        // Dead-end sectors that still pay out a slot or a map when explored.
        foreach (var id in sectors.Keys.OrderBy(k => k))
        {
            if (explored.Contains(id) || !Reachable(id))
                continue;

            var grants = GrantsOf(type, id);
            if (grants.NewSlot || grants.NewMap)
                return new NextStep(id, grants);
        }

        return null;
    }

    /// <summary>Progression order: submarines follow SubmarineTracker's chain to the last known sector; airships use the curated line.</summary>
    public static IEnumerable<uint> MainLine(VesselType type)
    {
        if (type == VesselType.Airship)
            return AirshipData.MainLine;

        var last = SubmarineData.SectorToUnlock.Last(s => s.Value.Sector != SubmarineData.SectorType.UnknownUnlock).Key;
        var main = PathTo(VesselType.Submarine, last);
        var rest = SubmarineData.SectorToUnlock.Keys.Where(k => !main.Contains(k)).OrderBy(k => k);
        return main.Concat(rest);
    }
}
