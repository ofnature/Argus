using System;
using System.Linq;
using Argus.Core.Calc;
using Argus.Windows.Components;
using Argus.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Argus.Windows;

/// <summary>
/// What the Argus edge tab pops out: every vessel's state and timer, the voyage slots in use, the supplies, and the
/// way into the full window. Compact on purpose: the main window is a sidebar and pages that size themselves to the
/// window's height, which a pop-out sized to its content does not have.
/// </summary>
internal static class DockPanel
{
    /// <summary>Fixed, because the vessel rows right-align their timers off the width left.</summary>
    public const float Width = 420f;

    private static readonly (string Label, MainWindow.Page Page)[] Pages =
    {
        ("Overview", MainWindow.Page.Overview),
        ("Vessels", MainWindow.Page.Vessels),
        ("Planner", MainWindow.Page.Planner),
        ("Builder", MainWindow.Page.Builder),
    };

    public static void Draw(Plugin plugin)
    {
        using var style = Styling.PushWindowStyle();
        var now = DateTime.UtcNow;
        var fleet = plugin.Fleet;

        // What deserves attention first, as the main window's header says it.
        var (status, detail, accent) = fleet.HeaderStatus(now);
        Styling.TextScaled("ARGUS", Styling.AccentTealSoft, 0.85f);
        ImGui.SameLine();
        Pill.Draw(status, accent);
        ImGui.SameLine();
        Styling.Text(detail, Styling.TextSecondary);

        var companies = fleet.VisibleCompanies().ToList();
        if (companies.Count == 0)
            Styling.TextWrapped("No vessels seen yet. Enter your Free Company workshop once and Argus will remember every vessel there.", Styling.TextMuted);

        foreach (var fc in companies)
        {
            Styling.VSpace(4f);
            var title = string.IsNullOrEmpty(fc.Tag) ? $"FC {fc.Id:X}" : $"«{fc.Tag}»";
            var deployed = fc.Vessels.Count(v => v.IsOut(now));
            var live = fc.Id == fleet.CurrentFreeCompanyId && fleet.HasLiveVessels;
            Styling.Text(title, Styling.TextStrong);
            ImGui.SameLine();
            Styling.Text($"Out {deployed}/{VoyageMath.MaxDeployedVessels}", deployed >= VoyageMath.MaxDeployedVessels ? Styling.AccentTeal : Styling.TextDim);
            if (live)
            {
                ImGui.SameLine();
                Pill.Draw("LIVE", Styling.AccentMint);
            }

            var width = ImGui.GetContentRegionAvail().X;
            foreach (var v in fc.Vessels.OrderBy(v => v.Type).ThenBy(v => v.Slot))
                OverviewSection.DrawVesselRow(v, now, width, live);

            // The counts only, flagged when they will not cover the next dispatches; the Overview has the detail.
            var report = Supplies.Evaluate(plugin.Data, fc);
            if (report.Known)
            {
                Styling.Text($"{Formatting.Number(report.Tanks)} ceruleum tanks · {Formatting.Number(report.Kits)} repair materials",
                    report.TanksShort || report.KitsShort ? Styling.AccentRose : Styling.TextDim);
            }
        }

        Styling.VSpace(6f);
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 6f * scale;
        var buttonWidth = (ImGui.GetContentRegionAvail().X - gap * (Pages.Length - 1)) / Pages.Length;
        for (var i = 0; i < Pages.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);
            if (Buttons.Action(Pages[i].Label, true, buttonWidth, Styling.AccentTeal))
                plugin.MainWindow.ShowPage(Pages[i].Page);
        }
    }
}
