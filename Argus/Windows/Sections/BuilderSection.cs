using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Argus.Windows.Sections;

/// <summary>
/// Part optimizer: for the planner's vessel and chosen route, every part set allowed at the vessel's rank that fits
/// the airframe and reaches the route, ranked by the planner's goal — or, with unlock focus on, by how well it rolls
/// for discovering the next sector. Shows what to swap from the current build.
/// </summary>
internal static class BuilderSection
{
    private static string signature = string.Empty;
    private static List<PartOptimizer.Candidate> results = new();
    private static List<PartOptimizer.UnlockCandidate> unlockResults = new();
    private static int targetRank;

    public static void Draw(Plugin plugin)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        MainWindow.PageHeader("Builder", "Best part sets for the planner's vessel on its chosen route, within airframe capacity.");

        var vessel = planner.Vessel;
        if (vessel == null)
        {
            Styling.Text("Pick a vessel in the Planner first.", Styling.TextMuted);
            return;
        }

        var route = planner.ChosenRoute?.Sectors
                    ?? (vessel.Type == VesselType.Submarine && vessel.Points.Count > 0 ? vessel.Points.ToArray() : null);
        if (route == null || route.Length == 0)
        {
            Styling.Text("Choose a route in the Planner (or dispatch the vessel once) so the builder has something to optimise for.", Styling.TextMuted);
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var prefs = plugin.Config.PlannerFor(vessel.Type);
        var useAverage = prefs.AverageBonus || vessel.Type == VesselType.Airship;
        var unlock = prefs.UnlockFocus && planner.AutoStep is { } step ? step : null;
        if (targetRank == 0 || signature.Length == 0)
            targetRank = vessel.Rank;

        var routeText = string.Join(" → ", route.Select(id => data.Sector(vessel.Type, id).Letter));
        if (unlock != null)
        {
            var target = data.Sector(vessel.Type, unlock.VisitSector);
            Styling.Text($"{vessel.Name} · route {routeText} · ", Styling.TextSecondary);
            ImGui.SameLine(0, 0);
            Pill.Draw("UNLOCK FOCUS", Styling.AccentTeal, 0.72f);
            Styling.Text($"Ranked for discovering the next sector at {target.Letter}. {target.Name}: surveillance tier reached there, favor above its line (double-dip = a second roll), then surveys per day.", Styling.TextDim);
        }
        else
        {
            Styling.Text($"{vessel.Name} · route {routeText} · goal {(prefs.Goal == RouteGoal.ExpPerHour ? "EXP/hour" : "EXP/voyage")}", Styling.TextSecondary);
        }

        ImGui.SetNextItemWidth(120f * scale);
        ImGui.InputInt("Build for rank", ref targetRank);
        targetRank = Math.Clamp(targetRank, 1, data.LastRank(vessel.Type));
        Styling.Tooltip("Parts unlock by rank and capacity grows with it; plan ahead for the rank you are levelling towards.");

        Build? current = null;
        try { current = Build.From(data, vessel); } catch (KeyNotFoundException) { }
        if (current != null)
        {
            Styling.VSpace(4f);
            Styling.SectionLabel("Current build");
            DrawBuildRow(plugin, vessel, current, route, useAverage, null, unlock?.VisitSector, false);
        }

        var sig = $"{vessel.Type}|{targetRank}|{planner.Map}|{string.Join(",", route)}|{prefs.Goal}|{useAverage}|{unlock?.VisitSector}";
        if (sig != signature)
        {
            signature = sig;
            if (unlock != null)
            {
                unlockResults = PartOptimizer.BestForUnlock(data, vessel.Type, targetRank, planner.Map, route, unlock.VisitSector, 10);
                results = new List<PartOptimizer.Candidate>();
            }
            else
            {
                results = PartOptimizer.Best(data, vessel.Type, targetRank, planner.Map, route, prefs.Goal, useAverage, 10);
                unlockResults = new List<PartOptimizer.UnlockCandidate>();
            }
        }

        Styling.VSpace(6f);
        Styling.SectionLabel(unlock != null ? "Unlock builds" : "Suggested builds");
        if (results.Count == 0 && unlockResults.Count == 0)
        {
            Styling.Text("No part set at this rank reaches the route within capacity. Try a shorter route or a higher rank.", Styling.TextMuted);
            return;
        }

        if (unlock != null)
        {
            foreach (var c in unlockResults)
                DrawBuildRow(plugin, vessel, c.Build, route, useAverage, null, unlock.VisitSector, true, c);
        }
        else
        {
            foreach (var c in results)
                DrawBuildRow(plugin, vessel, c.Build, route, useAverage, c, null, true);
        }
    }

    private static void DrawBuildRow(Plugin plugin, Vessel vessel, Build build, uint[] route, bool useAverage,
        PartOptimizer.Candidate? c, uint? unlockSector, bool showDiff, PartOptimizer.UnlockCandidate? u = null)
    {
        var data = plugin.Data;
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = Card.BeginFlat();

        var sectors = route.Select(id => data.Sector(vessel.Type, id)).ToList();
        var start = data.StartFor(vessel.Type, plugin.Planner.Map);
        var distance = c?.Distance ?? u?.Distance ?? VoyageMath.RouteDistance(start, sectors);
        var duration = c?.Duration ?? u?.Duration ?? VoyageMath.RouteDuration(start, sectors, build.Speed);
        var exp = c?.Exp ?? u?.Exp ?? ExpModel.Route(build, sectors);
        var used = exp.For(useAverage);
        var fits = distance <= build.Range;

        Styling.TextScaled(build.Identifier, Styling.TextStrong, 1.15f);
        ImGui.SameLine();
        var partNames = string.Join(" · ", build.Parts.Select((p, i) => $"{Build.SlotName(vessel.Type, i)}: {(vessel.Type == VesselType.Submarine ? Build.SubmarineClassName(p.Id) : Build.AirshipClassName(p.Class))}"));
        Styling.Text(partNames, Styling.TextDim);

        Styling.Text($"Sur {build.Surveillance} · Ret {build.Retrieval} · Spd {build.Speed} · Rng {build.Range} · Fav {build.Favor} · cost {build.Cost}/{build.Capacity}",
            build.FitsCapacity ? Styling.TextSecondary : Styling.AccentRose);
        Styling.Text($"{Formatting.VoyageLength(duration)} · {distance}/{build.Range} range · {Formatting.Number(used)} EXP ({Formatting.Number(exp.Guaranteed)}–{Formatting.Number(exp.Maximum)}) · {Formatting.Number((long)(duration.TotalHours > 0 ? used / duration.TotalHours : 0))}/h",
            fits ? Styling.TextSecondary : Styling.AccentRose);

        if (unlockSector is { } target)
        {
            var t = ExpModel.ThresholdsFor(vessel.Type, target);
            var tier = PartOptimizer.SurveillanceTier(t, build.Surveillance);
            var favorMet = t.Known && t.Favor > 0 && build.Favor >= t.Favor;
            var rolls = (favorMet ? 2.0 : 1.0) * 24.0 / Math.Max(1.0, duration.TotalHours);
            var tierText = tier switch { 2 => $"surveillance tier 3 ({t.T3})", 1 => $"surveillance tier 2 ({t.T2}, tier 3 at {t.T3})", _ => $"below surveillance tier 2 ({t.T2})" };
            var favorText = t.Favor > 0 ? (favorMet ? $"favor {build.Favor} ≥ {t.Favor}: double-dip rolls" : $"favor {build.Favor} < {t.Favor}") : "no favor line";
            Styling.Text($"{tierText} · {favorText} · ≈{rolls:0.0} surveys/day", tier == 2 && favorMet ? Styling.AccentMint : Styling.AccentTealSoft);
        }

        if (showDiff)
        {
            Build? current = null;
            try { current = Build.From(data, vessel); } catch (KeyNotFoundException) { }
            if (current != null)
            {
                var swaps = new List<string>();
                var cur = current.Parts.ToList();
                var next = build.Parts.ToList();
                for (var i = 0; i < 4; i++)
                {
                    if (cur[i].Id == next[i].Id)
                        continue;
                    var name = vessel.Type == VesselType.Submarine ? Build.SubmarineClassName(next[i].Id) : Build.AirshipClassName(next[i].Class);
                    swaps.Add($"{Build.SlotName(vessel.Type, i)} → {name}");
                }

                Styling.Text(swaps.Count == 0 ? "This is the current build." : "Swap: " + string.Join(", ", swaps), swaps.Count == 0 ? Styling.AccentMint : Styling.AccentTealSoft);
            }
        }

        Card.EndFlat(origin, width, Styling.CardBgSoft, fits && build.FitsCapacity ? null : Styling.AccentRose);
        Styling.VSpace(3f);
    }
}
