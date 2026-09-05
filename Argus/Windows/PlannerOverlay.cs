using System;
using System.Linq;
using System.Numerics;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Windows.Components;
using Argus.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Argus.Windows;

/// <summary>
/// Docked to the left of the in-game voyage planner while it is open: the chosen route for the vessel being
/// dispatched, its numbers, and the Apply button that selects the sectors. Deploying stays with the player.
/// </summary>
public sealed class PlannerOverlay : Window, IDisposable
{
    private const float Width = 320f;

    private readonly Plugin plugin;
    private (VesselType Type, int Slot)? lastVessel;
    private uint lastMap;

    public PlannerOverlay(Plugin plugin) : base("Argus Planner Overlay###ArgusPlannerOverlay")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ForceMainWindow = true;
    }

    public void Dispose() { }

    public override void PreOpenCheck()
    {
        IsOpen = false;
        if (!plugin.Config.ShowPlannerOverlay)
            return;

        var interop = plugin.PlannerInterop;
        var current = interop.CurrentVessel();
        var rect = interop.PlannerRect();
        if (current == null || rect == null)
        {
            lastVessel = null;
            return;
        }

        var fcId = plugin.Fleet.CurrentFreeCompanyId;
        if (fcId == 0 || !plugin.Fleet.Store.TryGet(fcId, out var fc))
            return;

        var (type, slot, map) = current.Value;
        var vessel = fc.Vessels.FirstOrDefault(v => v.Type == type && v.Slot == slot);
        if (vessel == null)
            return;

        if (lastVessel != (type, slot) || lastMap != map)
        {
            lastVessel = (type, slot);
            lastMap = map;
            plugin.Planner.Select(vessel);
            plugin.Planner.SetMap(map);
            if (type == VesselType.Airship)
                LearnAirshipUnlocks(fc);
        }

        var (x, y, _, _) = rect.Value;
        Position = new Vector2(x - Width * ImGuiHelpers.GlobalScale - 4f, y + 4f);
        PositionCondition = ImGuiCond.Always;
        IsOpen = true;
    }

    /// <summary>The planner only lists discovered sectors, so every listed row is an unlocked one.</summary>
    private void LearnAirshipUnlocks(FreeCompanyRecord fc)
    {
        var rows = plugin.PlannerInterop.ReadDestinations();
        if (rows.Count == 0)
            return;

        var changed = false;
        foreach (var s in plugin.Data.AirshipSectors.Values.Where(s => s.IsDestination))
        {
            var listed = rows.Any(r => string.Equals(r.NameFull, s.Name, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(r.NameShort, s.Name, StringComparison.OrdinalIgnoreCase));
            if (listed && fc.UnlockedAirshipSectors.Add(s.Id))
                changed = true;
        }

        if (changed)
            plugin.Fleet.Store.MarkDirty();
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(Width * ImGuiHelpers.GlobalScale, 0));
    }

    public override void Draw()
    {
        using var style = Styling.PushWindowStyle();
        using var bg = ImRaii.PushColor(ImGuiCol.WindowBg, Styling.CardBg);
        var planner = plugin.Planner;
        var data = plugin.Data;
        var vessel = planner.Vessel;
        var scale = ImGuiHelpers.GlobalScale;

        Styling.TextScaled("ARGUS", Styling.AccentTealSoft, 0.85f);
        ImGui.SameLine();
        Styling.Text(vessel == null ? "" : $"{vessel.Name} · R{vessel.Rank}", Styling.TextStrong);
        if (vessel != null && ExpModel.IsEstimate(vessel.Type))
        {
            ImGui.SameLine();
            Pill.Draw("ESTIMATE", Styling.AccentViolet, 0.7f);
        }

        if (vessel == null)
        {
            Styling.Text("Vessel not recognised.", Styling.TextMuted);
            return;
        }

        planner.Refresh();
        var prefs = plugin.Config.PlannerFor(vessel.Type);
        var useAverage = prefs.AverageBonus || vessel.Type == VesselType.Airship;

        if (planner.AutoStep is { } step && prefs.ProgressionAutoInclude)
        {
            var visit = data.Sector(vessel.Type, step.VisitSector);
            Styling.Text($"Progression: {visit.Letter} → {PlannerSection.GrantText(data, vessel.Type, step.Rewards)}", Styling.AccentTealSoft);
        }

        var route = planner.ChosenRoute;
        if (planner.Computing)
        {
            Styling.Text("Searching…", Styling.PulseColor(Styling.AccentTeal, Styling.AccentTealSoft));
        }
        else if (route == null)
        {
            Styling.Text("No route fits. Open Argus to adjust.", Styling.TextMuted);
        }
        else
        {
            foreach (var id in route.Sectors)
            {
                var s = data.Sector(vessel.Type, id);
                Styling.Text($"{s.Letter}. {s.Name}", Styling.TextStrong);
            }

            var exp = route.ExpUsed(useAverage);
            Styling.Text($"{Formatting.VoyageLength(route.Duration)} · {route.Distance} range · {route.Fuel} fuel", Styling.TextSecondary);
            Styling.Text($"{Formatting.Number(exp)} EXP · {Formatting.Number((long)route.ExpPerHour(useAverage))}/h", Styling.TextSecondary);
            if (planner.Results.Count > 1)
            {
                ImGui.SameLine();
                Styling.Text($"(#{planner.Chosen + 1} of {planner.Results.Count})", Styling.TextDim);
            }
        }

        Styling.VSpace(4f);
        var interop = plugin.PlannerInterop;
        var canApply = route != null && !planner.Computing && !interop.Applying;
        var half = (ImGui.GetContentRegionAvail().X - 6f * scale) * 0.5f;
        if (Buttons.Action(interop.Applying ? "Applying…" : "Apply route", canApply, half))
        {
            if (!interop.ApplyRoute(data, vessel.Type, route!.Sectors))
                Service.Log.Information("Argus: apply refused: {Reason}", interop.LastError ?? "unknown");
        }

        ImGui.SameLine(0, 6f * scale);
        if (Buttons.Action("Open planner", true, half, Styling.AccentBlue))
            plugin.MainWindow.ShowPage(MainWindow.Page.Planner);

        if (interop.LastError != null)
            Styling.Text(interop.LastError, Styling.AccentRose);
        else
            Styling.Text("Apply selects the sectors; you press Deploy.", Styling.TextMuted);
    }
}
