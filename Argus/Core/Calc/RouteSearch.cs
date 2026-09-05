using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Argus.Core.Model;

namespace Argus.Core.Calc;

public enum RouteGoal
{
    /// <summary>Best EXP per hour of voyage time. The leveling default.</summary>
    ExpPerHour,

    /// <summary>Most EXP from a single dispatch regardless of duration.</summary>
    ExpPerVoyage,
}

/// <summary>Everything the optimizer needs to know about one vessel and what it is allowed to do.</summary>
public sealed record RouteRequest(
    VesselType Type,
    uint Map,
    Build Build,
    IReadOnlySet<uint> Unlocked,
    IReadOnlySet<uint> MustInclude,
    RouteGoal Goal,
    TimeSpan? DurationCap,
    bool UseAverageBonus,
    int FuelAvailable = -1,
    bool IgnoreUnlocks = false,
    int MaxSectors = VoyageMath.MaxSectorsPerVoyage);

public sealed record RouteResult(
    uint[] Sectors,
    int Distance,
    TimeSpan Duration,
    int Fuel,
    RouteExp Exp,
    double Score)
{
    public uint ExpUsed(bool average) => Exp.For(average);
    public double ExpPerHour(bool average) => Duration.TotalHours <= 0 ? 0 : ExpUsed(average) / Duration.TotalHours;
}

/// <summary>
/// Exhaustive search over every set of up to five allowed sectors containing the must-includes, each in its shortest
/// order, filtered by range, fuel and duration cap, ranked by the goal. Small enough to run in well under a second on
/// a background thread even for a maxed submarine with a whole map unlocked.
/// </summary>
public static class RouteSearch
{
    public static IReadOnlyList<string> Issues(GameData data, RouteRequest req)
    {
        var issues = new List<string>();
        if (req.MustInclude.Count > req.MaxSectors)
            issues.Add($"At most {req.MaxSectors} sectors can be included.");

        foreach (var id in req.MustInclude)
        {
            if (!data.Sectors(req.Type).TryGetValue(id, out var s) || !s.IsDestination)
            {
                issues.Add($"Sector {id} is not a destination.");
                continue;
            }

            if (req.Type == VesselType.Submarine && s.Map != req.Map)
                issues.Add($"{s.Name} is on another map.");
            if (s.RankReq > req.Build.Rank)
                issues.Add($"{s.Name} needs rank {s.RankReq}.");
            if (s.SurveillanceReq > req.Build.Surveillance)
                issues.Add($"{s.Name} needs surveillance {s.SurveillanceReq}.");
            if (!req.IgnoreUnlocks && !req.Unlocked.Contains(id))
                issues.Add($"{s.Name} is not unlocked yet.");
        }

        return issues;
    }

    /// <summary>Sectors the vessel may visit right now.</summary>
    public static List<SectorInfo> Candidates(GameData data, RouteRequest req)
        => data.DestinationsOf(req.Type, req.Map)
            .Where(s => s.RankReq <= req.Build.Rank
                        && s.SurveillanceReq <= req.Build.Surveillance
                        && (req.IgnoreUnlocks || req.Unlocked.Contains(s.Id) || req.MustInclude.Contains(s.Id)))
            .ToList();

    public static RouteResult? FindBest(GameData data, RouteRequest req, CancellationToken ct = default)
        => FindTop(data, req, 1, ct).FirstOrDefault();

    public static List<RouteResult> FindTop(GameData data, RouteRequest req, int count, CancellationToken ct = default)
    {
        var candidates = Candidates(data, req);
        if (candidates.Count == 0 || req.MustInclude.Count > req.MaxSectors)
            return new List<RouteResult>();

        var index = new Dictionary<uint, int>();
        for (var i = 0; i < candidates.Count; i++)
            index[candidates[i].Id] = i;

        foreach (var m in req.MustInclude)
        {
            if (!index.ContainsKey(m))
                return new List<RouteResult>();
        }

        var start = data.StartFor(req.Type, req.Map);
        var n = candidates.Count;
        var speed = Math.Max(1, req.Build.Speed);

        // Leg tables: row 0 = start, rows 1..n = candidates.
        var points = new SectorInfo[n + 1];
        points[0] = start;
        for (var i = 0; i < n; i++)
            points[i + 1] = candidates[i];

        var dist = new int[n + 1, n + 1];
        var secs = new int[n + 1, n + 1];
        for (var a = 0; a <= n; a++)
        {
            for (var b = 1; b <= n; b++)
            {
                if (a == b) continue;
                dist[a, b] = VoyageMath.LegDistance(points[a], points[b]);
                secs[a, b] = VoyageMath.LegSeconds(points[a], points[b], speed);
            }
        }

        var surveyDist = new int[n + 1];
        var surveySecs = new int[n + 1];
        var fuel = new int[n + 1];
        var expG = new uint[n + 1];
        var expA = new uint[n + 1];
        var expM = new uint[n + 1];
        for (var i = 1; i <= n; i++)
        {
            var s = points[i];
            surveyDist[i] = s.SurveyDistance;
            surveySecs[i] = VoyageMath.SurveySeconds(s, speed);
            fuel[i] = s.Fuel;
            var e = ExpModel.Route(req.Build, new[] { s });
            expG[i] = e.Guaranteed;
            expA[i] = e.Average;
            expM[i] = e.Maximum;
        }

        var must = req.MustInclude.Select(m => index[m] + 1).OrderBy(i => i).ToArray();
        var free = Enumerable.Range(1, n).Where(i => Array.IndexOf(must, i) < 0).ToArray();
        var maxFree = req.MaxSectors - must.Length;

        var combos = new List<int[]>();
        var buffer = new int[req.MaxSectors];
        Array.Copy(must, buffer, must.Length);
        Combinations(free, 0, must.Length, buffer, maxFree, combos);
        if (must.Length > 0)
            combos.Add(must.ToArray()); // the must-includes alone are a valid route too

        var range = req.Build.Range;
        var capSeconds = req.DurationCap is { } cap ? (long)cap.TotalSeconds : long.MaxValue;
        var useAverage = req.UseAverageBonus;

        var results = combos
            .AsParallel()
            .WithCancellation(ct)
            .Select(set => Evaluate(set, dist, secs, surveyDist, surveySecs, range))
            .Where(r => r.Distance >= 0)
            .Select(r =>
            {
                var f = 0;
                ulong g = 0, a = 0, m = 0;
                foreach (var i in r.Order)
                {
                    f += fuel[i];
                    g += expG[i];
                    a += expA[i];
                    m += expM[i];
                }

                var exp = new RouteExp((uint)g, (uint)a, (uint)m);
                var duration = TimeSpan.FromSeconds(r.Seconds + VoyageMath.FixedVoyageSeconds);
                var used = useAverage ? a : g;
                var score = req.Goal == RouteGoal.ExpPerHour ? used / duration.TotalHours : used;
                return (Result: new RouteResult(r.Order.Select(i => points[i].Id).ToArray(), r.Distance, duration, f, exp, score), Seconds: r.Seconds, Fuel: f);
            })
            .Where(t => t.Seconds + VoyageMath.FixedVoyageSeconds <= capSeconds)
            .Where(t => req.FuelAvailable < 0 || t.Fuel <= req.FuelAvailable)
            .Select(t => t.Result)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Duration)
            .Take(count)
            .ToList();

        return results;
    }

    private static void Combinations(int[] free, int from, int filled, int[] buffer, int remaining, List<int[]> output)
    {
        if (remaining == 0)
            return;

        for (var i = from; i < free.Length; i++)
        {
            buffer[filled] = free[i];
            var combo = new int[filled + 1];
            Array.Copy(buffer, combo, filled + 1);
            output.Add(combo);
            Combinations(free, i + 1, filled + 1, buffer, remaining - 1, output);
        }
    }

    private readonly record struct Ordered(int[] Order, int Distance, long Seconds);

    /// <summary>Shortest ordering of a sector set by distance; Distance = -1 when nothing fits the range.</summary>
    private static Ordered Evaluate(int[] set, int[,] dist, int[,] secs, int[] surveyDist, int[] surveySecs, int range)
    {
        var best = new Ordered(Array.Empty<int>(), -1, 0);
        var bestDistance = int.MaxValue;
        var perm = (int[])set.Clone();
        Permute(perm, 0, ref best, ref bestDistance, dist, secs, surveyDist, surveySecs, range);
        return best;
    }

    private static void Permute(int[] perm, int k, ref Ordered best, ref int bestDistance, int[,] dist, int[,] secs, int[] surveyDist, int[] surveySecs, int range)
    {
        if (k == perm.Length)
        {
            var d = 0;
            long t = 0;
            var prev = 0;
            foreach (var i in perm)
            {
                d += dist[prev, i] + surveyDist[i];
                t += secs[prev, i] + surveySecs[i];
                prev = i;
            }

            if (d <= range && (d < bestDistance || (d == bestDistance && t < best.Seconds)))
            {
                bestDistance = d;
                best = new Ordered((int[])perm.Clone(), d, t);
            }

            return;
        }

        for (var i = k; i < perm.Length; i++)
        {
            (perm[k], perm[i]) = (perm[i], perm[k]);
            Permute(perm, k + 1, ref best, ref bestDistance, dist, secs, surveyDist, surveySecs, range);
            (perm[k], perm[i]) = (perm[i], perm[k]);
        }
    }

    /// <summary>Distance, duration, fuel and EXP for a route the user chose by hand, in the given order.</summary>
    public static RouteResult Describe(GameData data, Build build, uint map, IReadOnlyList<uint> sectors, RouteGoal goal, bool useAverage)
    {
        var route = sectors.Select(id => data.Sector(build.Type, id)).ToList();
        var start = data.StartFor(build.Type, map);
        var distance = VoyageMath.RouteDistance(start, route);
        var duration = VoyageMath.RouteDuration(start, route, build.Speed);
        var exp = ExpModel.Route(build, route);
        var used = exp.For(useAverage);
        var score = goal == RouteGoal.ExpPerHour && duration.TotalHours > 0 ? used / duration.TotalHours : used;
        return new RouteResult(sectors.ToArray(), distance, duration, VoyageMath.RouteFuel(route), exp, score);
    }
}
