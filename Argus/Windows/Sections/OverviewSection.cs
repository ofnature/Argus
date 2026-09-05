using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Argus.Core;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Argus.Windows.Sections;

/// <summary>Stat strip per vessel type, then every FC as a card with one row per vessel: state pill, name, rank, timer.</summary>
internal static class OverviewSection
{
    public static void Draw(Plugin plugin)
    {
        var now = DateTime.UtcNow;
        var fleet = plugin.Fleet;
        MainWindow.PageHeader("Overview", fleet.InWorkshop ? "Reading the workshop live." : "Last known state. Stand in a company workshop to refresh.");

        var subs = fleet.CountsFor(VesselType.Submarine, now);
        var air = fleet.CountsFor(VesselType.Airship, now);
        DrawStatStrip(subs, air, now);
        Styling.VSpace(8f);

        var companies = fleet.VisibleCompanies().ToList();
        if (companies.Count == 0)
        {
            Styling.VSpace(20f);
            Styling.TextCentered("No vessels seen yet.", Styling.TextSecondary, 1.1f);
            Styling.TextCentered("Enter your Free Company workshop once and Argus will remember every vessel there.", Styling.TextMuted);
            return;
        }

        foreach (var fc in companies)
            DrawCompany(plugin, fc, now);
    }

    private static void DrawStatStrip(FleetService.Counts subs, FleetService.Counts air, DateTime now)
    {
        var gap = 6f * ImGuiHelpers.GlobalScale;
        var width = (ImGui.GetContentRegionAvail().X - gap * 3) / 4f;

        StatTile.Draw("Subs ready", $"{subs.Ready}/{subs.Total}", subs.Out > 0 ? $"{subs.Out} out" : null,
            subs.Ready > 0 ? Styling.AccentAmber : Styling.AccentTeal, width, "Returned or idle submarines over all known FCs.");
        ImGui.SameLine(0, gap);
        StatTile.Draw("Airships ready", $"{air.Ready}/{air.Total}", air.Out > 0 ? $"{air.Out} out" : null,
            air.Ready > 0 ? Styling.AccentAmber : Styling.AccentTeal, width, "Returned or idle airships over all known FCs.");
        ImGui.SameLine(0, gap);

        var next = subs.NextReturn;
        if (air.NextReturn != null && (next == null || air.NextReturn.ReturnTime < next.ReturnTime))
            next = air.NextReturn;
        StatTile.Draw("Next return", next == null ? "—" : Formatting.Duration(next.Remaining(now)), next?.Name,
            Styling.AccentCyan, width, next == null ? null : $"{next.Name} returns at {Formatting.LocalTime(next.ReturnUtc)}.");
        ImGui.SameLine(0, gap);

        var last = subs.Total + air.Total == 0 ? null : LastReturn(subs, air);
        StatTile.Draw("Last return", last == null ? "—" : Formatting.Duration(last.Remaining(now)), last?.Name,
            Styling.AccentBlue, width, last == null ? null : $"{last.Name} returns at {Formatting.LocalTime(last.ReturnUtc)}.");
    }

    private static Vessel? LastReturn(FleetService.Counts subs, FleetService.Counts air)
    {
        // Counts only track the earliest; the latest is cheap to scan for on the overview.
        Vessel? last = null;
        foreach (var v in Plugin.Instance.Fleet.VisibleVessels())
        {
            if (!v.IsDeployed)
                continue;
            if (last == null || v.ReturnTime > last.ReturnTime)
                last = v;
        }

        return last;
    }

    private static void DrawCompany(Plugin plugin, FreeCompanyRecord fc, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = Card.BeginFlat();

        var title = string.IsNullOrEmpty(fc.Tag) ? $"FC {fc.Id:X}" : $"«{fc.Tag}»";
        Styling.TextScaled(title, Styling.TextStrong, 1.1f);
        ImGui.SameLine();
        var who = string.IsNullOrEmpty(fc.World) ? fc.CharacterName : $"{fc.CharacterName} @ {fc.World}";
        Styling.Text(who, Styling.TextDim);
        if (fc.Id == plugin.Fleet.CurrentFreeCompanyId)
        {
            ImGui.SameLine();
            Pill.Draw("LIVE", Styling.AccentMint);
        }

        Styling.VSpace(2f);
        var inner = width - 22f * scale;
        foreach (var type in new[] { VesselType.Submarine, VesselType.Airship })
        {
            var vessels = fc.OfType(type).OrderBy(v => v.Slot).ToList();
            if (vessels.Count == 0)
                continue;

            Styling.SectionLabel(type == VesselType.Submarine ? "Submarines" : "Airships");
            foreach (var v in vessels)
                DrawVesselRow(v, now, inner);
        }

        DrawSupplies(plugin, fc);
        Card.EndFlat(origin, width, Styling.CardBgSoft);
        Styling.VSpace(4f);
    }

    private static void DrawSupplies(Plugin plugin, FreeCompanyRecord fc)
    {
        var planner = plugin.Planner;
        var chosen = new Dictionary<(VesselType, int), uint[]>();
        if (planner.Selected is { } sel && sel.FcId == fc.Id && planner.ChosenRoute is { } route)
            chosen[(sel.Type, sel.Slot)] = route.Sectors;

        var report = Supplies.Evaluate(plugin.Data, fc, chosen);
        Styling.VSpace(2f);
        Styling.SectionLabel("Supplies");
        if (!report.Known)
        {
            Styling.Text("Not counted yet: stand in the workshop with the tanks and repair materials in your bags.", Styling.TextMuted);
            return;
        }

        var tanks = report.TanksForNextDispatch > 0
            ? $"{report.Tanks} ceruleum tanks · next dispatch needs {report.TanksForNextDispatch} ({report.DispatchesCovered} dispatch{(report.DispatchesCovered == 1 ? "" : "es")} covered)"
            : $"{report.Tanks} ceruleum tanks";
        Styling.Text(tanks, report.TanksShort ? Styling.AccentRose : Styling.TextSecondary);

        var kits = report.KitsForFullRepair > 0
            ? $"{report.Kits} magitek repair materials · a full repair of every vessel needs {report.KitsForFullRepair} ({report.RepairsCovered} covered)"
            : $"{report.Kits} magitek repair materials";
        Styling.Text(kits, report.KitsShort ? Styling.AccentRose : Styling.TextSecondary);
        if (fc.SuppliesSeenUtc != default)
            Styling.Text($"counted {Formatting.Duration(DateTime.UtcNow - fc.SuppliesSeenUtc)} ago", Styling.TextMuted);
    }

    private static void DrawVesselRow(Vessel v, DateTime now, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var height = 30f * scale;
        var dl = ImGui.GetWindowDrawList();

        var returned = v.IsReturned(now);
        var accent = Styling.StateColor(returned, v.IsDeployed);
        var state = returned ? "RETURNED" : v.IsDeployed ? "OUT" : "IDLE";

        Pill.DrawAt(origin + new Vector2(0, (height - Pill.Measure(state).Y) * 0.5f), state, accent);
        var x = origin.X + 92f * scale;
        var midY = origin.Y + height * 0.5f;

        var nameSize = ImGui.CalcTextSize(v.Name);
        dl.AddText(new Vector2(x, midY - nameSize.Y * 0.5f), ImGui.GetColorU32(Styling.TextStrong), v.Name);
        x += nameSize.X + 10f * scale;

        var rank = $"Rank {v.Rank}";
        var rankSize = ImGui.CalcTextSize(rank);
        dl.AddText(new Vector2(x, midY - rankSize.Y * 0.5f), ImGui.GetColorU32(Styling.TextDim), rank);

        string right;
        Vector4 rightColor;
        if (returned)
        {
            right = $"back since {Formatting.Duration(now - v.ReturnUtc)}";
            rightColor = Styling.AccentAmberSoft;
        }
        else if (v.IsDeployed)
        {
            right = $"{Formatting.Duration(v.Remaining(now))} · {Formatting.LocalTime(v.ReturnUtc)}";
            rightColor = Styling.TextSecondary;
        }
        else
        {
            right = "in the workshop";
            rightColor = Styling.TextDim;
        }

        var rightSize = ImGui.CalcTextSize(right);
        dl.AddText(new Vector2(origin.X + width - rightSize.X, midY - rightSize.Y * 0.5f), ImGui.GetColorU32(rightColor), right);

        ImGui.Dummy(new Vector2(width, height));
        if (v.Type == VesselType.Submarine && v.Points.Count > 0 && ImGui.IsItemHovered())
        {
            var names = v.Points.Select(p => Sheets.SubmarineSectors.GetRowOrDefault(p)?.Location.ExtractText() ?? p.ToString());
            ImGui.SetTooltip($"Route: {string.Join(" → ", names)}");
        }
    }
}
