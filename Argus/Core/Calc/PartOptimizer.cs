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
