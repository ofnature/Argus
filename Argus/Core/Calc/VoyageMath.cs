using System;
using System.Collections.Generic;
using Argus.Core.Model;

namespace Argus.Core.Calc;

/// <summary>
/// Distances and durations. Submarine formulas are SubmarineTracker's (verified against the game). Airship formulas
/// reproduce the community model (distance units from the sheet coordinates, travel 1150 min per unit at speed 1,
/// survey minutes from the sheet scaled by 70/speed, 12h fixed) — in-game, <c>PlannerInterop</c> prefers the client's
/// own <c>GetAirshipVoyageTimeAndDistance</c> and these serve as the offline estimate.
/// </summary>
public static class VoyageMath
{
    /// <summary>Every voyage carries a fixed 12 hours on top of travel and survey time.</summary>
    public const int FixedVoyageSeconds = 43200;

    public const int MaxSectorsPerVoyage = 5;

    private const double SubmarineDistanceFactor = 0.035;
    private const double SubmarineTravelFactor = 3990.0;   // × raw distance / (speed × 100), minutes
    private const double SubmarineSurveyFactor = 7000.0;   // × SurveyMinutes / (speed × 100), minutes

    private const double AirshipDistanceFactor = 0.05;
    private const double AirshipTravelMinutesPerUnit = 1150.0; // ÷ speed
    private const double AirshipSurveyReferenceSpeed = 70.0;   // sheet SurveyMinutes are quoted at speed 70

    private static double Raw(SectorInfo a, SectorInfo b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        var dz = (double)a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Distance units between two points as the planner shows them (no survey distance).</summary>
    public static int LegDistance(SectorInfo a, SectorInfo b)
        => a.Type == VesselType.Airship
            ? (int)Math.Round(Raw(a, b) * AirshipDistanceFactor, MidpointRounding.AwayFromZero)
            : (int)Math.Floor(Raw(a, b) * SubmarineDistanceFactor);

    /// <summary>Seconds spent travelling a leg.</summary>
    public static int LegSeconds(SectorInfo a, SectorInfo b, int speed)
    {
        speed = Math.Max(1, speed);
        var raw = Raw(a, b);
        if (a.Type == VesselType.Airship)
        {
            var units = Raw(a, b) * AirshipDistanceFactor;
            return (int)Math.Floor(units * AirshipTravelMinutesPerUnit / speed * 60.0);
        }

        return (int)Math.Floor(raw * SubmarineTravelFactor / (speed * 100.0) * 60.0);
    }

    /// <summary>Seconds spent surveying one sector.</summary>
    public static int SurveySeconds(SectorInfo sector, int speed)
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
