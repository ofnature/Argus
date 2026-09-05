using System;
using System.Collections.Generic;
using Argus.Core.Data;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>Per-sector EXP bonus: how many bonus steps are certain, expected and possible.</summary>
public readonly record struct SectorBonus(int Guaranteed, int Average, int Maximum);

/// <summary>EXP for a whole route under the three views of the bonus model.</summary>
public readonly record struct RouteExp(uint Guaranteed, uint Average, uint Maximum)
{
    public uint For(bool average) => average ? Average : Guaranteed;
}

/// <summary>
/// Voyage EXP. Submarines: SubmarineTracker's breakpoint model (+25% per bonus, max ×2). Airships: base EXP times
/// the expected performance rating (+50% per step, max ×2.5) fitted from the community log.
/// </summary>
public static class ExpModel
{
    public static bool IsEstimate(VesselType type) => type == VesselType.Airship && AirshipData.RatingIsEstimate;

    /// <summary>Submarine bonus steps for one sector given the build's stats.</summary>
    public static SectorBonus SubmarineBonus(uint sectorId, int surveillance, int retrieval, int favor)
    {
        if (!SubmarineData.MapBreakpoints.TryGetValue(sectorId, out var br))
            return new SectorBonus(0, 0, 0);

        var guaranteed = br.Optimal <= retrieval ? 1 : 0;

        var maximum = guaranteed;
        maximum += br.T2 <= surveillance ? 1 : 0;
        maximum += br.T3 <= surveillance ? 1 : 0;

        if (br.Favor <= favor)
        {
            maximum += 1;
            maximum += br.T2 <= surveillance ? 1 : 0;
            maximum += br.T3 <= surveillance ? 1 : 0;
        }

        maximum = Math.Clamp(maximum, 0, 4);
        var average = maximum == 0 ? 0 : (guaranteed + maximum) / 2;
        return new SectorBonus(guaranteed, average, maximum);
    }

    /// <summary>Submarine EXP after <paramref name="bonus"/> steps of +25%.</summary>
    public static uint SubmarineExp(uint baseExp, int bonus) => bonus switch
    {
        <= 0 => baseExp,
        1 => (uint)(baseExp * 1.25),
        2 => (uint)(baseExp * 1.50),
        3 => (uint)(baseExp * 1.75),
        _ => (uint)(baseExp * 2.00),
    };

    /// <summary>Airship EXP for one sector at a given rating (0-3).</summary>
    public static uint AirshipExp(uint baseExp, double rating)
        => (uint)Math.Round(baseExp * (1.0 + AirshipData.BonusPerRatingStep * Math.Clamp(rating, 0, AirshipData.MaxRating)));

    /// <summary>EXP for a route with a build. Order does not matter for EXP.</summary>
    public static RouteExp Route(Build build, IReadOnlyList<SectorInfo> route)
    {
        ulong guaranteed = 0, average = 0, maximum = 0;
        if (build.Type == VesselType.Submarine)
        {
            foreach (var s in route)
            {
                var b = SubmarineBonus(s.Id, build.Surveillance, build.Retrieval, build.Favor);
                guaranteed += SubmarineExp(s.Exp, b.Guaranteed);
                average += SubmarineExp(s.Exp, b.Average);
                maximum += SubmarineExp(s.Exp, b.Maximum);
            }
        }
        else
        {
            var expected = AirshipData.ExpectedRating(build.Retrieval);
            foreach (var s in route)
            {
                guaranteed += s.Exp;
                average += AirshipExp(s.Exp, expected);
                maximum += AirshipExp(s.Exp, AirshipData.MaxRating);
            }
        }

        return new RouteExp((uint)Math.Min(guaranteed, uint.MaxValue), (uint)Math.Min(average, uint.MaxValue), (uint)Math.Min(maximum, uint.MaxValue));
    }

    /// <summary>Breakpoint view for the UI: thresholds the build meets or misses on a sector.</summary>
    public readonly record struct Thresholds(int T2, int T3, int Normal, int Optimal, int Favor, bool Known);

    public static Thresholds ThresholdsFor(VesselType type, uint sectorId)
    {
        if (type == VesselType.Submarine)
        {
            return SubmarineData.MapBreakpoints.TryGetValue(sectorId, out var br)
                ? new Thresholds(br.T2, br.T3, br.Normal, br.Optimal, br.Favor, true)
                : default;
        }

        return AirshipData.Tiers.TryGetValue(sectorId, out var t)
            ? new Thresholds(t.T2, t.T3, 0, 0, t.Favor, true)
            : default;
    }
}
