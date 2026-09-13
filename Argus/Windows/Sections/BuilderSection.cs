using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core;
using Argus.Core.Calc;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

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
    private static List<PartOptimizer.ItemCandidate> itemResults = new();
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
        var prefs = plugin.Config.PlannerFor(vessel);
        var useAverage = prefs.AverageBonus || vessel.Type == VesselType.Airship;
        var unlock = prefs.UnlockFocus && planner.AutoStep is { } step ? step : null;
        var farmItem = unlock == null ? prefs.FarmItem : 0;
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
        else if (farmItem != 0)
        {
            Styling.Text($"{vessel.Name} · route {routeText} · ", Styling.TextSecondary);
            ImGui.SameLine(0, 0);
            Pill.Draw("FARM", Styling.AccentMint, 0.72f);
            Styling.TextWrapped($"Ranked for {Sheets.ItemName(farmItem)} per day. Surveillance decides which loot pool each sector rolls on, so the best build is the one inside the band that lists it, not the highest surveillance.", Styling.TextDim);
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
            DrawBuildRow(plugin, vessel, current, route, useAverage, null, unlock?.VisitSector, false, rowId: "current");
        }

        var sig = $"{vessel.Type}|{targetRank}|{planner.Map}|{string.Join(",", route)}|{prefs.Goal}|{useAverage}|{unlock?.VisitSector}|{farmItem}";
        if (sig != signature)
        {
            signature = sig;
            results = new List<PartOptimizer.Candidate>();
            unlockResults = new List<PartOptimizer.UnlockCandidate>();
            itemResults = new List<PartOptimizer.ItemCandidate>();

            if (unlock != null)
                unlockResults = PartOptimizer.BestForUnlock(data, vessel.Type, targetRank, planner.Map, route, unlock.VisitSector, 10);
            else if (farmItem != 0)
                itemResults = PartOptimizer.BestForItem(data, vessel.Type, targetRank, planner.Map, route, farmItem, 10);
            else
                results = PartOptimizer.Best(data, vessel.Type, targetRank, planner.Map, route, prefs.Goal, useAverage, 10);
        }

        Styling.VSpace(6f);
        Styling.SectionLabel(unlock != null ? "Unlock builds" : farmItem != 0 ? $"Builds for {Sheets.ItemName(farmItem)}" : "Suggested builds");
        if (results.Count == 0 && unlockResults.Count == 0 && itemResults.Count == 0)
        {
            Styling.TextWrapped(farmItem != 0
                ? "No build at this rank can get that item on this route: no sector on it lists the item at any surveillance tier. Add a sector that carries it in the Planner."
                : "No part set at this rank reaches the route within capacity. Try a shorter route or a higher rank.", Styling.TextMuted);
            return;
        }

        if (unlock != null)
        {
            for (var i = 0; i < unlockResults.Count; i++)
                DrawBuildRow(plugin, vessel, unlockResults[i].Build, route, useAverage, null, unlock.VisitSector, true, unlockResults[i], $"u{i}");
        }
        else if (farmItem != 0)
        {
            var itemName = Sheets.ItemName(farmItem);
            for (var i = 0; i < itemResults.Count; i++)
            {
                var c = itemResults[i];
                DrawBuildRow(plugin, vessel, c.Build, route, useAverage, null, null, true, null, $"f{i}",
                    $"{itemName}: ≈{c.Units:0.##} per voyage · {c.UnitsPerDay:0.##} per day",
                    i == 0 ? "BEST FOR THIS ITEM" : null);
            }
        }
        else
        {
            for (var i = 0; i < results.Count; i++)
                DrawBuildRow(plugin, vessel, results[i].Build, route, useAverage, results[i], null, true, null, $"b{i}");
        }
    }

    private static void DrawBuildRow(Plugin plugin, Vessel vessel, Build build, uint[] route, bool useAverage,
        PartOptimizer.Candidate? c, uint? unlockSector, bool showDiff, PartOptimizer.UnlockCandidate? u = null, string rowId = "",
        string? yieldLine = null, string? badge = null)
    {
        // Every card draws the same labels, and ImGui keys widgets by label, so without a scope per card their
        // buttons share one id and a click lands on whichever was drawn first.
        using var scope = ImRaii.PushId(rowId);
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
        if (badge != null)
        {
            ImGui.SameLine();
            Pill.Draw(badge, Styling.AccentMint, 0.7f);
        }

        ImGui.SameLine();
        var partNames = string.Join(" · ", build.Parts.Select((p, i) => $"{Build.SlotName(vessel.Type, i)}: {(vessel.Type == VesselType.Submarine ? Build.SubmarineClassName(p.Id) : Build.AirshipClassName(p.Class))}"));
        Styling.Text(partNames, Styling.TextDim);

        Styling.Text($"Sur {build.Surveillance} · Ret {build.Retrieval} · Spd {build.Speed} · Rng {build.Range} · Fav {build.Favor} · cost {build.Cost}/{build.Capacity}",
            build.FitsCapacity ? Styling.TextSecondary : Styling.AccentRose);
        Styling.Text($"{Formatting.VoyageLength(duration)} · {distance}/{build.Range} range · {Formatting.Number(used)} EXP ({Formatting.Number(exp.Guaranteed)}–{Formatting.Number(exp.Maximum)}) · {Formatting.Number((long)(duration.TotalHours > 0 ? used / duration.TotalHours : 0))}/h",
            fits ? Styling.TextSecondary : Styling.AccentRose);

        if (yieldLine != null)
            Styling.Text(yieldLine, Styling.AccentMint);

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
            DrawInstall(plugin, vessel, build, width, rowId);

        Card.EndFlat(origin, width, Styling.CardBgSoft, fits && build.FitsCapacity ? null : Styling.AccentRose);
        Styling.VSpace(3f);
    }

    /// <summary>
    /// What this build would cost in parts and a button to install it. Parts are consumed from the inventory, so the
    /// carried count is shown per slot and the button stays disabled unless every part is actually in the bags.
    /// </summary>
    private static void DrawInstall(Plugin plugin, Vessel vessel, Build build, float width, string rowId)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var parts = plugin.PartsInterop;
        var changes = PartsInterop.Differences(vessel, build);

        if (changes.Count == 0)
        {
            Styling.Text("This is the current build.", Styling.AccentMint);
            return;
        }

        var haveAll = true;
        foreach (var (slot, row, item) in changes)
        {
            var name = item == 0 ? "unknown part" : Sheets.ItemName(item);
            var carried = item == 0 ? 0 : PartsInterop.Carried(item);
            haveAll &= carried > 0;
            Styling.Text($"{Build.SlotName(vessel.Type, slot)} → {name}", Styling.AccentTealSoft);
            ImGui.SameLine();
            Styling.Text(carried > 0 ? $"(carrying {carried})" : "(none carried)", carried > 0 ? Styling.TextDim : Styling.AccentRose);
        }

        var ready = haveAll && parts.CanStart;
        if (Buttons.Action($"Install {changes.Count} part{(changes.Count == 1 ? string.Empty : "s")}", ready, 160f * scale, Styling.AccentAmber))
        {
            Service.Log.Information("Argus: install pressed for {Build}, {Count} parts", build.Identifier, changes.Count);
            if (!parts.ApplyBuild(vessel, build, rowId))
                Service.Log.Information("Argus: install refused: {Reason}", parts.LastError ?? parts.LastResult ?? "unknown");
        }

        if (!haveAll)
        {
            ImGui.SameLine();
            Styling.Text("craft or withdraw the missing parts first", Styling.TextMuted);
        }
        else if (parts.Blocker is { } blocker)
        {
            ImGui.SameLine();
            Styling.Text(blocker, Styling.TextMuted);
        }

        // Only the card that asked: the interop's state is global, and every card would otherwise claim the result.
        if (parts.LastRunId != rowId)
            return;

        if (parts.Running)
            Styling.Text($"Installing… {parts.Installed} done, {parts.Remaining} to go ({parts.StageName})", Styling.PulseColor(Styling.AccentAmber, Styling.AccentAmberSoft));
        else if (parts.LastError != null)
            Styling.TextWrapped(parts.LastError, Styling.AccentRose);
        else if (parts.LastResult != null)
            Styling.TextWrapped(parts.LastResult, Styling.AccentMint);
    }
}
