using System;
using System.Linq;
using System.Numerics;
using Argus.Core.Data;
using Argus.Core.Game;
using Argus.Windows.Components;
using Argus.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Argus.Windows;

/// <summary>
/// Docked beside the Voyage Control Panel's menu (the first menu and the vessel lists): which vessels are back, what
/// each will do, and the "Collect and resend" button that runs <see cref="VoyageRunner"/>. While a run goes it follows
/// the run, with a Stop button; afterwards it says what happened to each vessel.
/// </summary>
public sealed class VoyageRunOverlay : Window, IDisposable
{
    private const float Width = 300f;

    /// <summary>How long a finished run's outcome stays up at the panel.</summary>
    private static readonly TimeSpan OutcomeShown = TimeSpan.FromMinutes(2);

    private readonly Plugin plugin;
    private Vector2? dock;

    public VoyageRunOverlay(Plugin plugin) : base("Argus Run Overlay###ArgusRunOverlay")
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
        var runner = plugin.Runner;
        var rect = VoyageRunner.MenuRect();
        var atPanel = rect != null && runner.Screen() != PanelText.Screen.Unknown;
        if (!runner.Running && !atPanel)
        {
            if (rect == null)
                dock = null;
            return;
        }

        // Beside the menu while it is up; where it last was while the run is in the report, map list or planner.
        if (rect is { } r)
            dock = new Vector2(r.X + r.W + 4f, r.Y + 4f);
        if (dock == null)
            return;

        Position = dock;
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
        var runner = plugin.Runner;
        var now = DateTime.UtcNow;

        Styling.TextScaled("ARGUS", Styling.AccentTealSoft, 0.85f);
        ImGui.SameLine();
        Styling.Text("Collect and resend", Styling.TextStrong);

        if (runner.Running)
            DrawRun(runner);
        else
            DrawReady(runner, now);
    }

    private void DrawReady(VoyageRunner runner, DateTime now)
    {
        var fcId = plugin.Fleet.CurrentFreeCompanyId;
        if (fcId == 0 || !plugin.Fleet.Store.TryGet(fcId, out var fc))
            return;

        var returned = PanelText.Returned(fc.Vessels, now);
        if (returned.Count == 0)
        {
            Styling.Text("Nothing is back yet.", Styling.TextSecondary);
            var next = fc.Vessels.Where(v => v.IsOut(now)).OrderBy(v => v.ReturnUtc).FirstOrDefault();
            if (next != null)
                Styling.Text($"{next.Name} is back in {Formatting.Duration(next.Remaining(now))}.", Styling.TextDim);
        }

        foreach (var v in returned)
        {
            var line = $"{v.Name} · {PlannerSection.ModeLabel(plugin.Config.PlannerFor(v))}";
            var broken = v.BrokenSlots().Count;
            Styling.Text(broken > 0 ? $"{line} · repair {broken}" : line, broken > 0 ? Styling.AccentAmber : Styling.TextSecondary);
        }

        if (fc.CeruleumTanks >= 0)
            Styling.Text($"{Formatting.Number(fc.CeruleumTanks)} tanks · {Formatting.Number(fc.MagitekRepairMaterials)} repair materials", Styling.TextDim);

        Styling.VSpace(4f);
        var blocker = runner.Blocker(now);
        var label = returned.Count == 0 ? "Collect and resend" : $"Collect and resend {returned.Count}";
        if (Buttons.Action(label, blocker == null, ImGui.GetContentRegionAvail().X, Styling.AccentAmber))
            runner.Start(now);

        if (blocker != null && returned.Count > 0)
            Styling.TextWrapped($"Cannot start: {blocker}.", Styling.TextMuted);
        else
            Styling.TextWrapped("Finalizes each report (the loot), repairs parts at 0%, and sends each vessel out on the route its Argus planner picks now. Stops at anything it does not recognise.", Styling.TextMuted);

        if (runner.Targets.Count > 0 && now - runner.FinishedUtc < OutcomeShown)
        {
            Styling.VSpace(4f);
            Styling.Text("Last run", Styling.TextDim);
            DrawTargets(runner);
            if (runner.LastError != null)
                Styling.TextWrapped(runner.LastError, Styling.AccentRose);
        }
    }

    private static void DrawRun(VoyageRunner runner)
    {
        Styling.Text(runner.Status, Styling.PulseColor(Styling.AccentTeal, Styling.AccentTealSoft));
        DrawTargets(runner);
        Styling.VSpace(4f);
        if (Buttons.Action("Stop", true, ImGui.GetContentRegionAvail().X, Styling.AccentRose))
            runner.Cancel();
    }

    private static void DrawTargets(VoyageRunner runner)
    {
        foreach (var t in runner.Targets)
        {
            var (text, color) = t.State switch
            {
                VoyageRunner.TargetState.Pending => ("waiting", Styling.TextDim),
                VoyageRunner.TargetState.Picking or VoyageRunner.TargetState.Collecting => ("collecting…", Styling.AccentTeal),
                VoyageRunner.TargetState.AtMenu => ("planning…", Styling.AccentTeal),
                VoyageRunner.TargetState.Repairing => ("repairing…", Styling.AccentTeal),
                VoyageRunner.TargetState.Deploying => ("deploying…", Styling.AccentTeal),
                VoyageRunner.TargetState.Sent => ($"sent: {t.Note}", Styling.AccentMint),
                _ => ($"not sent: {t.Note}", Styling.AccentAmber),
            };

            if (!t.Finished && !string.IsNullOrEmpty(t.Note))
                text = $"{text} ({t.Note})";
            Styling.TextWrapped($"{t.Name}: {text}", color);
        }
    }
}
