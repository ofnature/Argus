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

    public static List<Candidate> Best(GameData data, VesselType type, int rank, uint map, IReadOnlyList<uint> route,
        RouteGoal goal, bool useAverage, int count = 10, IReadOnlySet<uint>? allowedParts = null)
    {
        var sectors = route.Select(id => data.Sector(type, id)).ToList();
        if (sectors.Count == 0)
            return new List<Candidate>();

        var start = data.StartFor(type, map);
        var rankRow = data.Rank(type, rank);
        var parts = data.Parts(type).Values
            .Where(p => p.Rank <= rank && (allowedParts == null || allowedParts.Contains(p.Id)))
            .ToList();

        var hulls = parts.Where(p => p.Slot == 0).ToList();
        var sterns = parts.Where(p => p.Slot == 1).ToList();
        var bows = parts.Where(p => p.Slot == 2).ToList();
        var bridges = parts.Where(p => p.Slot == 3).ToList();

        var results = new List<Candidate>();
        foreach (var h in hulls)
        foreach (var s in sterns)
        foreach (var b in bows)
        foreach (var br in bridges)
        {
            var build = new Build(type, rank, h, s, b, br, rankRow);
            if (!build.FitsCapacity)
                continue;

            var distance = VoyageMath.RouteDistance(start, sectors);
            if (distance > build.Range)
                continue;

            var duration = VoyageMath.RouteDuration(start, sectors, build.Speed);
            var exp = ExpModel.Route(build, sectors);
            var used = exp.For(useAverage);
            var score = goal == RouteGoal.ExpPerHour ? used / duration.TotalHours : used;
            results.Add(new Candidate(build, distance, duration, exp, score));
        }

        return results
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Build.Cost)
            .ThenBy(c => c.Duration)
            .Take(count)
            .ToList();
    }
}
