using System;
using System.Linq;
using System.Numerics;
using Argus.Core;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Windows.Components;
using Argus.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Argus.Windows;

/// <summary>
/// Docked beside the submarine map list (Nautical Maps) while a submarine is selected: which map the planner plans this
/// sub on, the route it would pick there, and a button that opens the planner on that map. From there the planner
/// overlay takes over. Only ever on a click, and choosing a map sends nothing.
/// </summary>
public sealed class MapSelectOverlay : Window, IDisposable
{
    private const float Width = 300f;

    private readonly Plugin plugin;
    private Vessel? vessel;
    private string? error;

    public MapSelectOverlay(Plugin plugin) : base("Argus Map Overlay###ArgusMapOverlay")
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
        if (plugin.Runner.Running)
            return;
        var rect = MapSelectInterop.Rect();
        var fcId = plugin.Fleet.CurrentFreeCompanyId;
        if (rect == null || fcId == 0 || !plugin.Fleet.Store.TryGet(fcId, out var fc))
        {
            vessel = null;
            error = null;
            return;
        }

        // The list belongs to the submarine whose menu it came from; the workshop says which.
        var selected = fc.OfType(VesselType.Submarine).FirstOrDefault(v => WorkshopReader.IsSelected(VesselType.Submarine, v.Slot) == true);
        if (selected == null)
            return;

        if (vessel == null || !vessel.SameIdentity(selected))
            error = null;
        vessel = selected;

        var (x, y, w, _) = rect.Value;
        Position = new Vector2(x + w + 4f, y + 4f);
        PositionCondition = ImGuiCond.Always;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(Width * ImGuiHelpers.GlobalScale, 0));
    }

    public override void Draw()
    {
        using var style = Styling.PushWindowStyle();
        using var bg = ImRaii.PushColor(ImGuiCol.WindowBg, Styling.CardBg);
        var v = vessel;
        if (v == null)
            return;

        var planner = plugin.Planner;
        var data = plugin.Data;
        planner.Select(v);
        planner.Refresh();
        var prefs = plugin.Config.PlannerFor(v);

        Styling.TextScaled("ARGUS", Styling.AccentTealSoft, 0.85f);
        ImGui.SameLine();
        Styling.Text($"{v.Name} · R{v.Rank} · {PlannerSection.ModeLabel(prefs)}", Styling.TextStrong);

        var map = planner.Map;
        var listed = MapSelectInterop.Maps();
        var name = listed.FirstOrDefault(m => m.Id == map)?.Name
                   ?? data.Maps.FirstOrDefault(m => m.Id == map)?.Name
                   ?? $"map {map}";
        Styling.Text($"Planning on {name}", Styling.TextSecondary);

        if (planner.Computing)
        {
            Styling.Text("Searching…", Styling.PulseColor(Styling.AccentTeal, Styling.AccentTealSoft));
        }
        else if (planner.ChosenRoute is { } route)
        {
            var letters = string.Concat(route.Sectors.Select(id => data.Sector(VesselType.Submarine, id).Letter));
            var yield = PlannerService.FarmsGil(prefs, v.Type)
                ? $"≈{Formatting.Number((long)route.Gil)} gil"
                : $"{Formatting.Number(route.ExpUsed(prefs.AverageBonus))} EXP";
            Styling.Text($"Route {letters} · {Formatting.VoyageLength(route.Duration)} · {yield}", Styling.TextDim);
        }

        var offered = listed.Any(m => m.Id == map);
        Styling.VSpace(4f);
        if (Buttons.Action($"Open {name}", offered, ImGui.GetContentRegionAvail().X, Styling.AccentTeal))
        {
            if (!MapSelectInterop.Select(map, out error))
                Service.Log.Information("Argus: map choice refused: {Reason}", error ?? "unknown");
        }

        if (!offered && listed.Count > 0)
            Styling.TextWrapped("That map is not in the list: not unlocked for this FC. Pick another here, or change the map on the Planner page.", Styling.TextMuted);
        else if (error != null)
            Styling.TextWrapped(error, Styling.AccentRose);
        else
            Styling.TextWrapped("Opens the planner on it; the route overlay takes over there.", Styling.TextMuted);
    }
}
