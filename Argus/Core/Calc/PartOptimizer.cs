using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>
/// Best part sets for a vessel type and rank on a given route: every combination of parts allowed at that rank that
/// fits the airframe capacity and whose range covers the route, ranked by the goal.
/// </summary>
public static class PartOptimizer
{
    public sealed record Candidate(Build Build, int Distance, TimeSpan Duration, RouteExp Exp, double Score)
    {
        public bool Fits => Distance <= Build.Range;
    }

    /// <summary>
    /// A build ranked for discovering a new sector. Discovery is a roll on every survey of the unlocking sector, so
    /// the levers are the surveillance tier reached there (community exploration builds max it), favor above the
    /// sector's line (a double-dip surveys it twice in one voyage), and speed (more voyages, more rolls).
    /// </summary>
    public sealed record UnlockCandidate(Build Build, int Distance, TimeSpan Duration, RouteExp Exp, int SurveillanceTier, bool FavorMet)
    {
        /// <summary>Expected surveys of the target per day: one per voyage, plus one more when favor can double-dip.</summary>
        public double RollsPerDay => (FavorMet ? 2.0 : 1.0) * 24.0 / Math.Max(1.0, Duration.TotalHours);
    }

    private static IEnumerable<Build> AllBuilds(GameData data, VesselType type, int rank, IReadOnlySet<uint>? allowedParts)
    {
        var rankRow = data.Rank(type, rank);
        var parts = data.Parts(type).Values
            .Where(p => p.Rank <= rank && (allowedParts == null || allowedParts.Contains(p.Id)))
            .ToList();

        var hulls = parts.Where(p => p.Slot == 0).ToList();
        var sterns = parts.Where(p => p.Slot == 1).ToList();
        var bows = parts.Where(p => p.Slot == 2).ToList();
        var bridges = parts.Where(p => p.Slot == 3).ToList();

        foreach (var h in hulls)
        foreach (var s in sterns)
        foreach (var b in bows)
        foreach (var br in bridges)
        {
            var build = new Build(type, rank, h, s, b, br, rankRow);
            if (build.FitsCapacity)
                yield return build;
        }
    }

    public static List<Candidate> Best(GameData data, VesselType type, int rank, uint map, IReadOnlyList<uint> route,
        RouteGoal goal, bool useAverage, int count = 10, IReadOnlySet<uint>? allowedParts = null)
    {
        var sectors = route.Select(id => data.Sector(type, id)).ToList();
        if (sectors.Count == 0)
            return new List<Candidate>();

        var start = data.StartFor(type, map);
        var distance = VoyageMath.RouteDistance(start, sectors);
        var results = new List<Candidate>();
        foreach (var build in AllBuilds(data, type, rank, allowedParts))
        {
            if (distance > build.Range)
                continue;

            var duration = VoyageMath.RouteDuration(start, sectors, build.Speed);
            var exp = ExpModel.Route(build, sectors);
            var used = exp.For(useAverage);
            var score = goal switch
            {
                RouteGoal.ExpPerHour => used / duration.TotalHours,
                RouteGoal.ShortestVoyage => 1e9 / Math.Max(1.0, duration.TotalSeconds),
                _ => used,
            };
            results.Add(new Candidate(build, distance, duration, exp, score));
        }

        return results
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Build.Cost)
            .ThenBy(c => c.Duration)
            .Take(count)
            .ToList();
    }

    /// <summary>A build ranked for how much of one item it brings back.</summary>
    public sealed record ItemCandidate(Build Build, int Distance, TimeSpan Duration, double Units, RouteExp Exp)
    {
        public double UnitsPerDay => Duration.TotalHours <= 0 ? 0 : Units * 24.0 / Duration.TotalHours;
    }

    /// <summary>
    /// Builds ranked by how much of <paramref name="itemId"/> the route yields. Surveillance decides which loot pool
    /// each sector rolls on, so this naturally lands on a build inside the band that lists the item rather than simply
    /// the highest surveillance; a route with no sector that can ever supply it comes back empty.
    /// </summary>
    public static List<ItemCandidate> BestForItem(GameData data, VesselType type, int rank, uint map, IReadOnlyList<uint> route,
        uint itemId, int count = 10, IReadOnlySet<uint>? allowedParts = null)
    {
        var sectors = route.Select(id => data.Sector(type, id)).ToList();
        if (sectors.Count == 0)
            return new List<ItemCandidate>();

        var start = data.StartFor(type, map);
        var distance = VoyageMath.RouteDistance(start, sectors);

        var results = new List<ItemCandidate>();
        foreach (var build in AllBuilds(data, type, rank, allowedParts))
        {
            if (distance > build.Range)
                continue;

            var units = ItemYield.PerVoyage(type, route, itemId, build.Surveillance, build.Retrieval);
            if (units <= 0)
                continue;

            var duration = VoyageMath.RouteDuration(start, sectors, build.Speed);
            results.Add(new ItemCandidate(build, distance, duration, units, ExpModel.Route(build, sectors)));
        }

        return results
            .OrderByDescending(c => c.UnitsPerDay)
            .ThenByDescending(c => c.Units)
            .ThenBy(c => c.Build.Cost)
            .Take(count)
            .ToList();
    }

    /// <summary>A build and the gil route it would fly with it.</summary>
    public sealed record GilCandidate(Build Build, uint[] Route, int Distance, TimeSpan Duration, double Gil)
    {
        public double GilPerDay => Duration.TotalHours <= 0 ? 0 : Gil * 24.0 / Duration.TotalHours;
    }

    /// <summary>
    /// Builds ranked by vendor gil, each with its own route. Unlike the other rankings this does not take the planner's
    /// route, because a gil build changes which route is possible: range decides how many salvage sectors fit in a
    /// voyage and surveillance which loot pool each rolls on. For every build it picks the best set of this map's gil
    /// sectors that it reaches at <paramref name="minTier"/> or better (as <see cref="SurveillanceTier"/> counts:
    /// 2 = the tier 3 pool) and can fly, scored per hour or per voyage by <paramref name="goal"/>. With
    /// <paramref name="currentParts"/> (hull, stern, bow, bridge), only builds that swap at most
    /// <paramref name="maxChanges"/> of them are considered: the cheapest upgrades for a vessel that already has parts.
    /// </summary>
    public static List<GilCandidate> BestForGil(GameData data, int rank, uint map, IReadOnlySet<uint> allowedSectors,
        IReadOnlyDictionary<uint, int> prices, int minTier, RouteGoal goal, int count = 10, IReadOnlySet<uint>? allowedParts = null,
        IReadOnlyList<uint>? currentParts = null, int maxChanges = 4)
    {
        const VesselType type = VesselType.Submarine;
        var start = data.StartFor(type, map);

        // Sectors worth a visit for gil at their best, richest first; capped so the sets stay few.
        var gilSectors = data.DestinationsOf(type, map)
            .Where(s => allowedSectors.Contains(s.Id) && s.RankReq <= rank)
            .Select(s => (Sector: s, Top: ItemYield.GilPerVisit(type, s.Id, int.MaxValue, int.MaxValue, prices)))
            .Where(x => x.Top >= 1000)
            .OrderByDescending(x => x.Top)
            .Take(8)
            .Select(x => x.Sector)
            .ToList();
        if (gilSectors.Count == 0)
            return new List<GilCandidate>();

        var thresholds = gilSectors.Select(s => ExpModel.ThresholdsFor(type, s.Id)).ToArray();

        // Every set of up to five, each in its shortest order. Distance does not depend on the build, so once.
        var sets = new List<(int Mask, SectorInfo[] Order, int Distance)>();
        for (var mask = 1; mask < 1 << gilSectors.Count; mask++)
        {
            var members = Enumerable.Range(0, gilSectors.Count).Where(i => (mask & (1 << i)) != 0).Select(i => gilSectors[i]).ToArray();
            if (members.Length > VoyageMath.MaxSectorsPerVoyage)
                continue;

            SectorInfo[]? best = null;
            var bestDistance = int.MaxValue;
            foreach (var order in Permutations(members))
            {
                var d = VoyageMath.RouteDistance(start, order);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = order;
                }
            }

            sets.Add((mask, best!, bestDistance));
        }

        // Voyage time depends only on speed, which many builds share.
        var durations = new Dictionary<(int Mask, int Speed), TimeSpan>();
        var results = new List<(GilCandidate Candidate, double Score)>();
        foreach (var build in AllBuilds(data, type, rank, allowedParts))
        {
            if (currentParts != null && Changes(build, currentParts) > maxChanges)
                continue;

            var gil = new double[gilSectors.Count];
            var eligible = 0;
            for (var i = 0; i < gilSectors.Count; i++)
            {
                if (SurveillanceTier(thresholds[i], build.Surveillance) < minTier)
                    continue;
                gil[i] = ItemYield.GilPerVisit(type, gilSectors[i].Id, build.Surveillance, build.Retrieval, prices);
                if (gil[i] > 0)
                    eligible |= 1 << i;
            }

            (GilCandidate Candidate, double Score)? best = null;
            foreach (var (mask, order, distance) in sets)
            {
                if ((mask & ~eligible) != 0 || distance > build.Range)
                    continue;

                if (!durations.TryGetValue((mask, build.Speed), out var duration))
                {
                    duration = VoyageMath.RouteDuration(start, order, build.Speed);
                    durations[(mask, build.Speed)] = duration;
                }

                var total = 0.0;
                for (var i = 0; i < gilSectors.Count; i++)
                {
                    if ((mask & (1 << i)) != 0)
                        total += gil[i];
                }

                var score = goal == RouteGoal.ExpPerHour ? total / Math.Max(0.01, duration.TotalHours) : total;
                if (best == null || score > best.Value.Score || (score == best.Value.Score && duration < best.Value.Candidate.Duration))
                    best = (new GilCandidate(build, order.Select(s => s.Id).ToArray(), distance, duration, total), score);
            }

            if (best != null)
                results.Add(best.Value);
        }

        return results
            // Equal hauls are common (every build that reaches the same tiers brings back the same), and between
            // those the faster one is strictly better; capacity used earns nothing once a build fits.
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Duration)
            .ThenBy(x => x.Candidate.Build.Cost)
            .Take(count)
            .Select(x => x.Candidate)
            .ToList();
    }

    /// <summary>How many of the four slots differ from <paramref name="current"/> (hull, stern, bow, bridge).</summary>
    public static int Changes(Build build, IReadOnlyList<uint> current)
    {
        var changes = 0;
        var i = 0;
        foreach (var part in build.Parts)
        {
            if (i >= current.Count || part.Id != current[i])
                changes++;
            i++;
        }

        return changes;
    }

    private static IEnumerable<SectorInfo[]> Permutations(SectorInfo[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }

    /// <summary>Surveillance tier a build reaches at a sector: 2 = tier 3 pool, 1 = tier 2 pool, 0 = base.</summary>
    public static int SurveillanceTier(ExpModel.Thresholds t, int surveillance)
    {
        if (!t.Known)
            return 0;
        if (t.T3 > 0 && surveillance >= t.T3)
            return 2;
        if (t.T2 > 0 && surveillance >= t.T2)
            return 1;
        return 0;
    }

    /// <summary>
    /// Builds ranked for unlocking: reach the route, then highest surveillance tier at <paramref name="targetSector"/>,
    /// favor at or above its line, most surveys per day, cheapest.
    /// </summary>
    public static List<UnlockCandidate> BestForUnlock(GameData data, VesselType type, int rank, uint map, IReadOnlyList<uint> route,
        uint targetSector, int count = 10, IReadOnlySet<uint>? allowedParts = null)
    {
        var sectors = route.Select(id => data.Sector(type, id)).ToList();
        if (sectors.Count == 0)
            return new List<UnlockCandidate>();

        var start = data.StartFor(type, map);
        var distance = VoyageMath.RouteDistance(start, sectors);
        var thresholds = ExpModel.ThresholdsFor(type, targetSector);

        var results = new List<UnlockCandidate>();
        foreach (var build in AllBuilds(data, type, rank, allowedParts))
        {
            if (distance > build.Range)
                continue;

            var duration = VoyageMath.RouteDuration(start, sectors, build.Speed);
            var exp = ExpModel.Route(build, sectors);
            var tier = SurveillanceTier(thresholds, build.Surveillance);
            var favor = thresholds.Known && thresholds.Favor > 0 && build.Favor >= thresholds.Favor;
            results.Add(new UnlockCandidate(build, distance, duration, exp, tier, favor));
        }

        return results
            .OrderByDescending(c => c.SurveillanceTier)
            .ThenByDescending(c => c.FavorMet)
            .ThenByDescending(c => c.RollsPerDay)
            .ThenByDescending(c => c.Build.Surveillance)
            .ThenBy(c => c.Build.Cost)
            .Take(count)
            .ToList();
    }
}
