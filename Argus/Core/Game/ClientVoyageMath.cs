using System;
using System.Collections.Generic;
using Argus.Core.Calc;
using Argus.Core.Model;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Argus.Core.Game;

/// <summary>
/// Routes <see cref="VoyageMath"/> through the client's own voyage functions, which are the numbers the planner
/// window shows. Results are cached per (type, from, to, speed) so the route search's leg tables and the part
/// optimizer never hammer native code; a failure falls back to the offline formula and is logged once.
/// </summary>
internal static unsafe class ClientVoyageMath
{
    /// <summary>The airship start is AirshipExplorationPoint row 127; submarine starts are their own rows.</summary>
    private const byte AirshipStartPoint = 127;

    private static readonly object Gate = new();
    private static readonly Dictionary<(VesselType, uint, uint, int), (int, int)> Legs = new();
    private static readonly Dictionary<(VesselType, uint, int), int> Surveys = new();
    private static bool warned;

    public static void Install()
    {
        VoyageMath.LegOverride = Leg;
        VoyageMath.SurveyOverride = Survey;
    }

    public static void Uninstall()
    {
        VoyageMath.LegOverride = null;
        VoyageMath.SurveyOverride = null;
        lock (Gate)
        {
            Legs.Clear();
            Surveys.Clear();
        }
    }

    private static (int Distance, int Seconds) Leg(SectorInfo from, SectorInfo to, int speed)
    {
        var key = (from.Type, from.Id, to.Id, speed);
        lock (Gate)
        {
            if (Legs.TryGetValue(key, out var hit))
                return hit;
        }

        (int, int) result;
        try
        {
            if (from.Type == VesselType.Airship)
            {
                uint time = 0, distance = 0;
                var a = from.IsStart ? AirshipStartPoint : (byte)from.Id;
                HousingManager.GetAirshipVoyageTimeAndDistance(a, (byte)to.Id, (short)speed, &time, &distance);
                result = ((int)distance, (int)time);
            }
            else
            {
                var distance = HousingManager.GetSubmarineVoyageDistance((byte)from.Id, (byte)to.Id);
                var time = HousingManager.GetSubmarineVoyageTime((byte)from.Id, (byte)to.Id, (short)speed);
                result = ((int)distance, (int)time);
            }
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
            result = (VoyageMath.LegDistanceFormula(from, to), VoyageMath.LegSecondsFormula(from, to, speed));
        }

        lock (Gate)
            Legs[key] = result;
        return result;
    }

    private static int Survey(SectorInfo sector, int speed)
    {
        var key = (sector.Type, sector.Id, speed);
        lock (Gate)
        {
            if (Surveys.TryGetValue(key, out var hit))
                return hit;
        }

        int result;
        try
        {
            result = sector.Type == VesselType.Airship
                ? (int)HousingManager.GetAirshipSurveyDuration((byte)sector.Id, (short)speed)
                : (int)HousingManager.GetSubmarineSurveyDuration((byte)sector.Id, (short)speed);
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
            result = VoyageMath.SurveySecondsFormula(sector, speed);
        }

        lock (Gate)
            Surveys[key] = result;
        return result;
    }

    private static void WarnOnce(Exception ex)
    {
        if (warned)
            return;
        warned = true;
        Service.Log.Warning(ex, "Argus: client voyage function failed; using the offline formula");
    }
}
