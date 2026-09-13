using System;
using System.Collections.Generic;
using Argus.Core.Calc;
using Argus.Core.Model;
using Dalamud.Configuration;

namespace Argus;

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

    /// <summary>
    /// Plan for discovering the next sector instead of EXP: shortest voyage through the progression sector (discovery
    /// is a roll on every survey there) and builds ranked by surveillance tier, favor line and speed. Off by default.
    /// </summary>
    public bool UnlockFocus = false;

    /// <summary>Item the planner is farming for; 0 plans for EXP. Mutually exclusive with <see cref="UnlockFocus"/>.</summary>
    public uint FarmItem;

    public PlannerPrefs Clone() => (PlannerPrefs)MemberwiseClone();
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

    /// <summary>
    /// Tick on the overlay: after Apply has selected every sector, press Deploy and confirm. Off by default, and it
    /// only ever fires from the player pressing Apply.
    /// </summary>
    public bool DeployAfterApply;

    // Main window
    public bool OpenOnWorkshopEnter = false;

    /// <summary>FCs the user hid from the overview/DTR, by FC id.</summary>
    public HashSet<ulong> HiddenFreeCompanies = new();

    /// <summary>
    /// The settings every vessel of a type shared before they were stored per vessel. Now only the starting point for a
    /// vessel the planner has not been used with yet, so upgrading changes nothing until a vessel is edited.
    /// </summary>
    public PlannerPrefs SubmarinePlanner = new();
    public PlannerPrefs AirshipPlanner = new();

    /// <summary>
    /// Planner settings per vessel, so a progression, a farming and a levelling submarine each keep their own route
    /// type. Keyed by FC, type and slot: the slot is stable for a vessel's life and, unlike the name, survives a rename.
    /// </summary>
    public Dictionary<string, PlannerPrefs> VesselPlanners = new();

    public static string VesselKey(Vessel vessel) => $"{vessel.FreeCompanyId}:{vessel.Type}:{vessel.Slot}";

    public PlannerPrefs PlannerFor(Vessel vessel)
    {
        var key = VesselKey(vessel);
        if (!VesselPlanners.TryGetValue(key, out var prefs))
        {
            prefs = (vessel.Type == VesselType.Airship ? AirshipPlanner : SubmarinePlanner).Clone();
            VesselPlanners[key] = prefs;
        }

        return prefs;
    }

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
