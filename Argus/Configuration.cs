using System;
using System.Collections.Generic;
using Argus.Core.Model;
using Dalamud.Configuration;

namespace Argus;

public enum RouteGoal
{
    /// <summary>Best EXP per hour of voyage time. The leveling default.</summary>
    ExpPerHour,

    /// <summary>Most EXP from a single dispatch regardless of duration.</summary>
    ExpPerVoyage,
}

/// <summary>Planner preferences, kept separately per vessel type because subs and airships level at different paces.</summary>
[Serializable]
public sealed class PlannerPrefs
{
    public RouteGoal Goal = RouteGoal.ExpPerHour;

    /// <summary>Maximum voyage length in hours, 0 = no cap. A cap of 24/36/48 fits a daily re-dispatch cycle.</summary>
    public int DurationCapHours = 0;

    /// <summary>Add the next progression sector (unlocks a sector, map or vessel slot) to must-include automatically.</summary>
    public bool ProgressionAutoInclude = true;

    /// <summary>Plan with sectors the FC has not unlocked yet (useful for what-if planning).</summary>
    public bool IgnoreUnlocks = false;

    /// <summary>Use the average EXP bonus (surveillance/favor rolls) instead of the guaranteed one when ranking routes.</summary>
    public bool AverageBonus = false;
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Server info bar
    public bool ShowDtrBar = true;
    public bool DtrShowSubmarines = true;
    public bool DtrShowAirships = true;

    // Planner overlay docked to the in-game voyage window
    public bool ShowPlannerOverlay = true;

    // Main window
    public bool OpenOnWorkshopEnter = false;

    /// <summary>FCs the user hid from the overview/DTR, by FC id.</summary>
    public HashSet<ulong> HiddenFreeCompanies = new();

    public PlannerPrefs SubmarinePlanner = new();
    public PlannerPrefs AirshipPlanner = new();

    public PlannerPrefs PlannerFor(VesselType type) => type == VesselType.Airship ? AirshipPlanner : SubmarinePlanner;

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
