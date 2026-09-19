using System;
using System.Collections.Generic;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>
/// Distances and durations. In-game the plugin installs the client's own voyage functions as
/// <see cref="LegOverride"/> / <see cref="SurveyOverride"/>, which is authoritative; the formulas below are what
/// tests and offline planning use. Submarine formulas are SubmarineTracker's. Airship formulas were fitted to the
/// client's readout for all 24 start legs (2026-09-05): distance = floor(raw × 0.052) exactly, travel time within
/// one minute (the client quantises to whole minutes in a way no single rounding rule reproduces).
/// </summary>
public static class VoyageMath
{
    /// <summary>Every voyage carries a fixed 12 hours on top of travel and survey time.</summary>
    public const int FixedVoyageSeconds = 43200;

    public const int MaxSectorsPerVoyage = 5;

    /// <summary>
    /// How many vessels a Free Company can have on a voyage at once. The limit is shared: four in total across
    /// submarines and airships, however many of each are registered.
    /// </summary>
    public const int MaxDeployedVessels = 4;

    public delegate (int Distance, int Seconds) LegFunc(SectorInfo from, SectorInfo to, int speed);

    public delegate int SurveyFunc(SectorInfo sector, int speed);

    /// <summary>Client-backed leg distance/time; null outside the game.</summary>
    public static LegFunc? LegOverride { get; set; }

    /// <summary>Client-backed survey duration; null outside the game.</summary>
    public static SurveyFunc? SurveyOverride { get; set; }

    private const double SubmarineDistanceFactor = 0.035;
    private const double SubmarineTravelFactor = 3990.0;   // × raw distance / (speed × 100), minutes
    private const double SubmarineSurveyFactor = 7000.0;   // × SurveyMinutes / (speed × 100), minutes

    private const double AirshipDistanceFactor = 0.052;
    private const double AirshipTravelFactor = 3583.0;      // × raw distance / speed, seconds
    private const double AirshipSurveyReferenceSpeed = 70.0; // sheet SurveyMinutes are quoted at speed 70

    private static double Raw(SectorInfo a, SectorInfo b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        var dz = (double)a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Distance units between two points as the planner shows them (no survey distance).</summary>
    public static int LegDistance(SectorInfo a, SectorInfo b)
        => LegOverride?.Invoke(a, b, 100).Distance ?? LegDistanceFormula(a, b);

    /// <summary>Seconds spent travelling a leg.</summary>
    public static int LegSeconds(SectorInfo a, SectorInfo b, int speed)
        => LegOverride?.Invoke(a, b, Math.Max(1, speed)).Seconds ?? LegSecondsFormula(a, b, speed);

    /// <summary>Seconds spent surveying one sector.</summary>
    public static int SurveySeconds(SectorInfo sector, int speed)
        => SurveyOverride?.Invoke(sector, Math.Max(1, speed)) ?? SurveySecondsFormula(sector, speed);

    public static int LegDistanceFormula(SectorInfo a, SectorInfo b)
        => a.Type == VesselType.Airship
            ? (int)Math.Floor(Raw(a, b) * AirshipDistanceFactor)
            : (int)Math.Floor(Raw(a, b) * SubmarineDistanceFactor);

    public static int LegSecondsFormula(SectorInfo a, SectorInfo b, int speed)
    {
        speed = Math.Max(1, speed);
        var raw = Raw(a, b);
        return a.Type == VesselType.Airship
            ? (int)Math.Floor(raw * AirshipTravelFactor / speed)
            : (int)Math.Floor(raw * SubmarineTravelFactor / (speed * 100.0) * 60.0);
    }

    public static int SurveySecondsFormula(SectorInfo sector, int speed)
    {
        speed = Math.Max(1, speed);
        return sector.Type == VesselType.Airship
            ? (int)Math.Floor(sector.SurveyMinutes * AirshipSurveyReferenceSpeed / speed * 60.0)
            : (int)Math.Floor(sector.SurveyMinutes * SubmarineSurveyFactor / (speed * 100.0) * 60.0);
    }

    /// <summary>Total distance of an ordered route: legs from the start plus each sector's survey distance.</summary>
    public static int RouteDistance(SectorInfo start, IReadOnlyList<SectorInfo> route)
    {
        if (route.Count == 0)
            return 0;

        var distance = LegDistance(start, route[0]) + route[0].SurveyDistance;
        for (var i = 1; i < route.Count; i++)
            distance += LegDistance(route[i - 1], route[i]) + route[i].SurveyDistance;
        return distance;
    }

    /// <summary>Total voyage length of an ordered route including the fixed 12 hours.</summary>
    public static TimeSpan RouteDuration(SectorInfo start, IReadOnlyList<SectorInfo> route, int speed)
    {
        if (route.Count == 0)
            return TimeSpan.Zero;

        var seconds = LegSeconds(start, route[0], speed) + SurveySeconds(route[0], speed);
        for (var i = 1; i < route.Count; i++)
            seconds += LegSeconds(route[i - 1], route[i], speed) + SurveySeconds(route[i], speed);
        return TimeSpan.FromSeconds(seconds + FixedVoyageSeconds);
    }

    public static int RouteFuel(IReadOnlyList<SectorInfo> route)
    {
        var fuel = 0;
        foreach (var s in route)
            fuel += s.Fuel;
        return fuel;
    }
}
