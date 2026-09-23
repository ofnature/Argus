using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Argus.Core;
using Argus.Core.Data;
using Argus.Core.Calc;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Argus.Windows.Sections;

/// <summary>
/// Route planner: pick a vessel (and map), set the goal and cap, add must-include sectors, and read the ranked
/// suggestions. Progression auto-include and every sector's unlock grants are shown inline.
/// </summary>
internal static class PlannerSection
{
    private static readonly (string Label, int Hours)[] Caps = { ("No cap", 0), ("12h", 12), ("24h", 24), ("36h", 36), ("48h", 48), ("72h", 72) };

    public static void Draw(Plugin plugin)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        MainWindow.PageHeader("Planner", "Best routes for this vessel's rank, range, fuel and unlocks. Must-include sectors are always kept.");

        DrawVesselPicker(plugin);
        var vessel = planner.Vessel;
        var fc = planner.Company;
        if (vessel == null || fc == null)
        {
            Styling.VSpace(12f);
            Styling.Text("Pick a vessel to plan for.", Styling.TextMuted);
            return;
        }

        var prefs = plugin.Config.PlannerFor(vessel);
        var changed = DrawOptions(plugin, vessel, prefs);
        if (changed)
            plugin.Config.Save();

        planner.Refresh();

        DrawProgression(plugin, vessel);
        DrawMustInclude(plugin, vessel, fc);
        DrawResults(plugin, vessel);
        DrawSectorTable(plugin, vessel, fc);
    }

    private static void DrawVesselPicker(Plugin plugin)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        var current = planner.Vessel;
        var label = current == null ? "Choose a vessel…" : VesselLabel(plugin, current);

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##planner_vessel", label))
        {
            if (combo.Success)
            {
                foreach (var fc in plugin.Fleet.VisibleCompanies())
                {
                    foreach (var v in fc.Vessels.OrderBy(v => v.Type).ThenBy(v => v.Slot))
                    {
                        var selected = current != null && current.SameIdentity(v);
                        if (ImGui.Selectable(VesselLabel(plugin, v), selected))
                            planner.Select(v);
                    }
                }
            }
        }

        if (current is { Type: VesselType.Submarine })
        {
            ImGui.SameLine();
            var mapName = data.Maps.FirstOrDefault(m => m.Id == planner.Map)?.Name ?? $"Map {planner.Map}";
            ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
            using var combo = ImRaii.Combo("##planner_map", mapName);
            if (combo.Success)
            {
                var fc = planner.Company;
                foreach (var m in data.Maps)
                {
                    var unlockedHere = fc != null && fc.UnlockedSubSectors.Any(id => data.SubmarineSectors.TryGetValue(id, out var s) && s.Map == m.Id);
                    using var color = ImRaii.PushColor(ImGuiCol.Text, unlockedHere ? Styling.TextStrong : Styling.TextMuted);
                    if (ImGui.Selectable(m.Name, m.Id == planner.Map))
                        planner.SetMap(m.Id);
                }
            }
        }

        if (current != null && ExpModel.IsEstimate(current.Type))
        {
            ImGui.SameLine();
            Pill.Draw("EXP ESTIMATE", Styling.AccentViolet);
            Styling.Tooltip("Airship performance ratings are not data-mined. EXP figures use a model fitted to community voyage logs and are expectations, not guarantees.");
        }
    }

    private static string VesselLabel(Plugin plugin, Vessel v)
    {
        var tag = plugin.Fleet.Store.TryGet(v.FreeCompanyId, out var fc) && !string.IsNullOrEmpty(fc.Tag) ? $"«{fc.Tag}» " : string.Empty;
        return $"{tag}{v.Name} · {(v.Type == VesselType.Submarine ? "sub" : "airship")} · R{v.Rank} · {ModeLabel(plugin.Config.PlannerFor(v))}";
    }

    /// <summary>What a vessel's planner is set up to do, in a few words.</summary>
    internal static string ModeLabel(PlannerPrefs prefs)
        => prefs.UnlockFocus ? "unlock"
            : prefs.FarmItem != 0 ? $"farm {Sheets.ItemName(prefs.FarmItem)}"
            : prefs.Goal == RouteGoal.ExpPerVoyage ? "EXP/voyage"
            : "EXP/hour";

    private static bool DrawOptions(Plugin plugin, Vessel vessel, PlannerPrefs prefs)
    {
        var changed = false;
        var scale = ImGuiHelpers.GlobalScale;
        Styling.VSpace(4f);

        // Goal segment
        Styling.Text("Goal", Styling.TextDim);
        ImGui.SameLine();
        changed |= Segment("EXP / hour", prefs.Goal == RouteGoal.ExpPerHour, () => prefs.Goal = RouteGoal.ExpPerHour);
        ImGui.SameLine(0, 4f * scale);
        changed |= Segment("EXP / voyage", prefs.Goal == RouteGoal.ExpPerVoyage, () => prefs.Goal = RouteGoal.ExpPerVoyage);

        ImGui.SameLine(0, 18f * scale);
        Styling.Text("Cap", Styling.TextDim);
        ImGui.SameLine();
        var capLabel = Caps.FirstOrDefault(c => c.Hours == prefs.DurationCapHours).Label ?? $"{prefs.DurationCapHours}h";
        ImGui.SetNextItemWidth(90f * scale);
        using (var combo = ImRaii.Combo("##planner_cap", capLabel))
        {
            if (combo.Success)
            {
                foreach (var (label, hours) in Caps)
                {
                    if (ImGui.Selectable(label, hours == prefs.DurationCapHours))
                    {
                        prefs.DurationCapHours = hours;
                        changed = true;
                    }
                }
            }
        }

        Styling.Tooltip("Longest voyage to consider. Pick the cap that matches how often you re-dispatch.");

        ImGui.SameLine(0, 18f * scale);
        changed |= ToggleInline("Progression", ref prefs.ProgressionAutoInclude,
            "Automatically include the next sector that unlocks a sector, map or vessel slot.");
        ImGui.SameLine(0, 14f * scale);
        if (vessel.Type == VesselType.Submarine)
        {
            changed |= ToggleInline("Average bonus", ref prefs.AverageBonus,
                "Rank routes by the average EXP bonus (surveillance and favor rolls) instead of only the guaranteed one.");
            ImGui.SameLine(0, 14f * scale);
        }

        changed |= ToggleInline("Include locked", ref prefs.IgnoreUnlocks,
            "Plan with sectors the FC has not unlocked yet. For what-if planning; the in-game planner will refuse them.");
        ImGui.SameLine(0, 14f * scale);
        var unlockWas = prefs.UnlockFocus;
        changed |= ToggleInline("Unlock focus", ref prefs.UnlockFocus,
            "Plan for discovering the next sector instead of EXP. Discovery is a roll on every survey of the progression sector, so this picks the shortest voyage through it and the Builder ranks parts by surveillance tier there, favor above its line (double-dip = a second roll) and speed. Needs Progression on.");
        if (prefs.UnlockFocus && !unlockWas)
            prefs.FarmItem = 0;

        Styling.VSpace(4f);
        changed |= DrawFarmPicker(plugin, vessel, prefs);
        return changed;
    }

    private static string itemFilter = string.Empty;

    /// <summary>Pick an item to farm. Choosing one ranks routes by expected units of it instead of EXP.</summary>
    private static bool DrawFarmPicker(Plugin plugin, Vessel vessel, PlannerPrefs prefs)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        var scale = ImGuiHelpers.GlobalScale;

        if (!LootTable.HasData(vessel.Type))
        {
            Styling.Text("No loot data for this vessel type.", Styling.TextMuted);
            return false;
        }

        var changed = false;
        Styling.Text("Farm", Styling.TextDim);
        ImGui.SameLine();

        var label = prefs.FarmItem == 0 ? "nothing (plan for EXP)" : Sheets.ItemName(prefs.FarmItem);
        ImGui.SetNextItemWidth(320f * scale);
        using (var combo = ImRaii.Combo("##planner_farm", label))
        {
            if (combo.Success)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##farm_filter", "search", ref itemFilter, 64);

                if (ImGui.Selectable("nothing (plan for EXP)", prefs.FarmItem == 0))
                {
                    prefs.FarmItem = 0;
                    changed = true;
                }

                // Only what this map can actually produce, so the list stays short and relevant.
                var reachable = data.DestinationsOf(vessel.Type, planner.Map).Select(x => x.Id);
                var items = LootTable.ItemsFrom(vessel.Type, reachable)
                    .Select(id => (Id: id, Name: Sheets.ItemName(id)))
                    .Where(x => itemFilter.Length == 0 || x.Name.Contains(itemFilter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(200);

                foreach (var (id, name) in items)
                {
                    if (!ImGui.Selectable($"{name}##farm{id}", prefs.FarmItem == id))
                        continue;
                    prefs.FarmItem = id;
                    prefs.UnlockFocus = false;
                    changed = true;
                }
            }
        }

        if (prefs.FarmItem == 0)
            return changed;

        ImGui.SameLine();
        if (Buttons.Icon(FontAwesomeIcon.Times, "##farm_clear", 24f * scale, "Stop farming"))
        {
            prefs.FarmItem = 0;
            changed = true;
        }

        Build? build = null;
        try { build = Build.From(data, vessel); } catch (KeyNotFoundException) { }
        if (build == null)
            return changed;

        // The surveillance band each tier occupies, so the number to build toward is visible either way.
        var windows = ItemYield.Windows(data, vessel.Type, planner.Map, prefs.FarmItem);
        var windowText = string.Join(" · ", windows
            .GroupBy(w => w.Sector)
            .Select(g => $"{data.Sector(vessel.Type, g.Key).Letter} {string.Join(" or ", g.Select(w => w.Describe()))}"));

        var sources = ItemYield.Sources(data, vessel.Type, planner.Map, prefs.FarmItem, build.Surveillance, build.Retrieval);
        if (sources.Count == 0)
        {
            // Surveillance promotes a sector to a richer loot pool, which can drop the basic items entirely.
            var atLowerTier = data.DestinationsOf(vessel.Type, planner.Map).Any(sec =>
            {
                var reached = PartOptimizer.SurveillanceTier(ExpModel.ThresholdsFor(vessel.Type, sec.Id), build.Surveillance);
                return Enumerable.Range(0, reached).Any(tier => LootTable.Drops(vessel.Type, sec.Id, tier).Any(d => d.ItemId == prefs.FarmItem));
            });

            Styling.TextWrapped(atLowerTier
                ? $"This build's surveillance ({build.Surveillance}) is too high for that item here: it only appears in the lower loot tiers, and the sectors that carry it now roll on a richer pool."
                : "No sector on this map produces that item at the surveillance tier this build reaches.", Styling.AccentRose);

            if (windowText.Length > 0)
                Styling.TextWrapped($"Surveillance needed: {windowText}. The Builder ranks builds for it.", Styling.AccentAmberSoft);

            return changed;
        }

        var rated = sources[0].PerVisit > 0 && vessel.Type == VesselType.Submarine;
        var text = string.Join(" · ", sources.Take(8).Select(x =>
        {
            var letter = data.Sector(vessel.Type, x.Sector).Letter;
            return rated ? $"{letter} {x.PerVisit:0.##}/visit ({x.Min}-{x.Max})" : letter;
        }));
        Styling.TextWrapped(rated ? $"Drops from: {text}" : $"Drops from: {text} (airship rates are unknown; sources only)", Styling.AccentTealSoft);
        if (windowText.Length > 0)
            Styling.TextWrapped($"Surveillance needed: {windowText} (this build {build.Surveillance})", Styling.TextDim);

        return changed;
    }

    private static bool Segment(string label, bool active, Action apply)
    {
        var scale = ImGuiHelpers.GlobalScale;
        using var c = ImRaii.PushColor(ImGuiCol.Button, active ? Styling.AccentTeal * 0.55f : Styling.CardBgSoft)
            .Push(ImGuiCol.ButtonHovered, active ? Styling.AccentTeal * 0.75f : Styling.CardBgHover)
            .Push(ImGuiCol.ButtonActive, Styling.AccentTeal)
            .Push(ImGuiCol.Text, active ? Styling.TextStrong : Styling.TextSecondary);
        if (!ImGui.Button(label, new Vector2(0, 24f * scale)) || active)
            return false;
        apply();
        return true;
    }

    private static bool ToggleInline(string label, ref bool value, string help)
    {
        var changed = ToggleSwitch.Draw($"##tgl_{label}", ref value);
        Styling.Tooltip(help);
        ImGui.SameLine(0, 5f * ImGuiHelpers.GlobalScale);
        ImGui.AlignTextToFramePadding();
        Styling.Text(label, Styling.TextSecondary);
        Styling.Tooltip(help);
        return changed;
    }

    private static void DrawProgression(Plugin plugin, Vessel vessel)
    {
        var step = plugin.Planner.AutoStep;
        var prefs = plugin.Config.PlannerFor(vessel);
        if (!prefs.ProgressionAutoInclude)
            return;

        var width = ImGui.GetContentRegionAvail().X;
        var origin = Card.BeginFlat();
        if (step == null)
        {
            Styling.Text("Progression: nothing left to unlock from here at this rank.", Styling.TextDim);
        }
        else
        {
            var data = plugin.Data;
            var visit = data.Sector(vessel.Type, step.VisitSector);
            Styling.Text("Progression:", Styling.AccentTealSoft);
            ImGui.SameLine();
            Styling.Text($"visit {visit.Letter}. {visit.Name}", Styling.TextStrong);
            ImGui.SameLine();
            Styling.Text("→ " + GrantText(data, vessel.Type, step.Rewards), Styling.TextSecondary);
            if (prefs.UnlockFocus)
            {
                ImGui.SameLine();
                Pill.Draw("UNLOCK FOCUS", Styling.AccentTeal, 0.72f);
                Styling.Text("Discovery is a roll each time this sector is surveyed, so routes are ranked shortest-first to roll as often as possible. The Builder page ranks part sets for it.", Styling.TextDim);
            }
            else
            {
                Styling.Text("Discovery is a roll each time this sector is surveyed; the roll is not guaranteed. Turn on Unlock focus to plan around it.", Styling.TextMuted);
            }
        }

        Card.EndFlat(origin, width, Styling.CardBgSoft, Styling.AccentTeal);
        Styling.VSpace(4f);
    }

    internal static string GrantText(GameData data, VesselType type, Progression.Grants g)
    {
        var parts = new List<string>();
        if (g.Sectors.Count > 0)
            parts.Add("unlocks " + string.Join(", ", g.Sectors.Select(id => data.Sectors(type).TryGetValue(id, out var s) ? s.Letter : id.ToString())));
        if (g.NewMap)
            parts.Add("opens the next map");
        if (g.NewSlot)
            parts.Add($"registers {(type == VesselType.Submarine ? "submarine" : "airship")} #{g.SlotNumber}");
        return parts.Count == 0 ? "no unlocks" : string.Join(" · ", parts);
    }

    private static void DrawMustInclude(Plugin plugin, Vessel vessel, FreeCompanyRecord fc)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        var scale = ImGuiHelpers.GlobalScale;

        Styling.SectionLabel($"Must include · {planner.EffectiveMustInclude().Count} / {VoyageMath.MaxSectorsPerVoyage}");
        Styling.VSpace(2f);

        var request = planner.BuildRequest();
        var candidates = request == null ? new List<SectorInfo>() : RouteSearch.Candidates(data, request);

        // Chips
        var effective = planner.EffectiveMustInclude();
        var auto = planner.AutoStep?.VisitSector;
        var any = false;
        foreach (var id in effective.OrderBy(x => x))
        {
            var s = data.Sector(vessel.Type, id);
            var isAuto = auto == id && !planner.ManualMustInclude.Contains(id);
            any = true;
            Pill.Draw($"{s.Letter}. {s.Name}{(isAuto ? "  (progression)" : string.Empty)}", isAuto ? Styling.AccentTeal : Styling.AccentAmber);
            if (!isAuto)
            {
                ImGui.SameLine(0, 2f * scale);
                if (Buttons.Icon(FontAwesomeIcon.Times, $"##rm_{id}", 20f * scale, "Remove"))
                    planner.RemoveMust(id);
            }

            ImGui.SameLine(0, 8f * scale);
        }

        if (any)
            ImGui.NewLine();

        ImGui.SetNextItemWidth(320f * scale);
        using (var combo = ImRaii.Combo("##planner_addmust", "Add a sector…"))
        {
            if (combo.Success)
            {
                foreach (var s in candidates.Where(c => !effective.Contains(c.Id)))
                {
                    var grants = Progression.GrantsOf(vessel.Type, s.Id);
                    var suffix = grants.Any ? "  ★" : string.Empty;
                    if (ImGui.Selectable($"{s.Letter}. {s.Name}  (R{s.RankReq}, {Formatting.Number(s.Exp)} EXP){suffix}"))
                        planner.AddMust(s.Id);
                    if (grants.Any && ImGui.IsItemHovered())
                        ImGui.SetTooltip(GrantText(data, vessel.Type, grants));
                }
            }
        }

        if (planner.ManualMustInclude.Count > 0)
        {
            ImGui.SameLine();
            if (Buttons.Action("Clear", true, 70f * scale))
                planner.ClearMust();
        }

        if (planner.VoyageSlotWarning(DateTime.UtcNow) is { } slots)
            Styling.TextWrapped("⚠ " + slots, Styling.AccentAmber);

        if (planner.RepairWarning() is { } repair)
            Styling.TextWrapped("⚠ " + repair, Styling.AccentAmber);

        foreach (var issue in planner.Issues)
            Styling.Text("⚠ " + issue, Styling.AccentRose);

        Styling.VSpace(6f);
    }

    private static void DrawResults(Plugin plugin, Vessel vessel)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        var scale = ImGuiHelpers.GlobalScale;
        var prefs = plugin.Config.PlannerFor(vessel);
        var useAverage = prefs.AverageBonus || vessel.Type == VesselType.Airship;

        Styling.SectionLabel("Suggested routes");
        Styling.VSpace(2f);

        if (planner.Computing)
        {
            var dots = new string('.', (int)((DateTime.UtcNow - planner.ComputeStartedUtc).TotalMilliseconds / 400) % 4);
            Styling.Text($"Searching{dots}", Styling.PulseColor(Styling.AccentTeal, Styling.AccentTealSoft));
            return;
        }

        if (planner.Results.Count == 0)
        {
            Styling.Text("No route fits: check the rank, range, fuel and cap, or drop a must-include sector.", Styling.TextMuted);
            return;
        }

        Build? build = null;
        try { build = Build.From(data, vessel); } catch (KeyNotFoundException) { }

        var width = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < planner.Results.Count; i++)
        {
            var r = planner.Results[i];
            var chosen = i == planner.Chosen;
            using var rowScope = ImRaii.PushId(i);
            var origin = Card.BeginFlat();

            var letters = string.Join(" → ", r.Sectors.Select(id => data.Sector(vessel.Type, id).Letter));
            Styling.TextScaled(letters, chosen ? Styling.AccentTealSoft : Styling.TextStrong, 1.2f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join("\n", r.Sectors.Select(id => { var s = data.Sector(vessel.Type, id); return $"{s.Letter}. {s.Name}  (R{s.RankReq}, {Formatting.Number(s.Exp)} EXP, {s.Fuel} fuel)"; })));

            ImGui.SameLine();
            Styling.Text($"#{i + 1}", Styling.TextDim);

            var exp = r.ExpUsed(useAverage);
            var perHour = r.ExpPerHour(useAverage);
            var line = $"{Formatting.VoyageLength(r.Duration)} · {r.Distance}/{build?.Range ?? 0} range · {r.Fuel} fuel · " +
                       $"{Formatting.Number(exp)} EXP ({Formatting.Number(r.Exp.Guaranteed)}–{Formatting.Number(r.Exp.Maximum)}) · {Formatting.Number((long)perHour)}/h";
            Styling.Text(line, Styling.TextSecondary);

            if (prefs.FarmItem != 0)
            {
                var yieldText = vessel.Type == VesselType.Submarine
                    ? $"{Sheets.ItemName(prefs.FarmItem)}: ≈{r.ItemUnits:0.##} per voyage · {r.ItemUnitsPerHour * 24:0.##} per day"
                    : $"{Sheets.ItemName(prefs.FarmItem)}: {r.ItemUnits:0} of {r.Sectors.Length} sectors can drop it";
                Styling.Text(yieldText, r.ItemUnits > 0 ? Styling.AccentMint : Styling.TextMuted);
            }

            if (build != null)
            {
                var after = RankSim.Apply(data, vessel.Type, vessel.Rank, vessel.CurrentExp, exp);
                var extra = after.Rank > vessel.Rank ? $"rank {vessel.Rank} → {after.Rank}" : $"stays rank {vessel.Rank}";
                var route = r.Sectors.Select(id => data.Sector(vessel.Type, id)).ToList();
                var repair = Repair.VoyagesUntilRepair(build, route);
                if (repair > 0)
                    extra += $" · repair after {repair} voyage{(repair == 1 ? "" : "s")}";
                Styling.Text(extra, Styling.TextDim);
            }

            var grants = r.Sectors.Select(id => (id, g: Progression.GrantsOf(vessel.Type, id))).Where(t => t.g.Any).ToList();
            if (grants.Count > 0)
            {
                var text = string.Join(" · ", grants.Select(t => $"{data.Sector(vessel.Type, t.id).Letter}: {GrantText(data, vessel.Type, t.g)}"));
                Styling.Text(text, Styling.AccentTealSoft);
            }

            ImGui.SameLine(width - 11f * scale - 90f * scale);
            if (Buttons.Action(chosen ? "Chosen" : "Use", !chosen, 90f * scale))
                planner.Chosen = i;

            Card.EndFlat(origin, width, Styling.CardBgSoft, chosen ? Styling.AccentTeal : null);
            Styling.VSpace(3f);
        }

        Styling.Text("Apply the chosen route from the overlay next to the in-game voyage planner.", Styling.TextMuted);
        Styling.VSpace(6f);
    }

    private static void DrawSectorTable(Plugin plugin, Vessel vessel, FreeCompanyRecord fc)
    {
        var planner = plugin.Planner;
        var data = plugin.Data;
        if (!ImGui.CollapsingHeader("All sectors on this map"))
            return;

        Build? build = null;
        try { build = Build.From(data, vessel); } catch (KeyNotFoundException) { }

        var unlocked = planner.Unlocked(fc, vessel.Type);
        using var table = ImRaii.Table("##planner_sectors", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Sector", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Rank");
        ImGui.TableSetupColumn("EXP");
        ImGui.TableSetupColumn("Fuel");
        ImGui.TableSetupColumn("Bonus");
        ImGui.TableSetupColumn("Unlocked");
        ImGui.TableSetupColumn("Grants", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var s in data.DestinationsOf(vessel.Type, planner.Map))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var reachable = s.RankReq <= vessel.Rank && (build == null || s.SurveillanceReq <= build.Surveillance);
            Styling.Text($"{s.Letter}. {s.Name}", reachable ? Styling.TextStrong : Styling.TextDim);
            ImGui.TableNextColumn();
            Styling.Text(s.RankReq.ToString(), s.RankReq <= vessel.Rank ? Styling.TextSecondary : Styling.AccentRose);
            ImGui.TableNextColumn();
            Styling.Text(Formatting.Number(s.Exp), Styling.TextSecondary);
            ImGui.TableNextColumn();
            Styling.Text(s.Fuel.ToString(), Styling.TextSecondary);
            ImGui.TableNextColumn();
            DrawThresholds(build, vessel.Type, s.Id);
            ImGui.TableNextColumn();
            Styling.Text(unlocked.Contains(s.Id) ? "yes" : "no", unlocked.Contains(s.Id) ? Styling.AccentMint : Styling.TextMuted);
            ImGui.TableNextColumn();
            var g = Progression.GrantsOf(vessel.Type, s.Id);
            Styling.Text(g.Any ? GrantText(data, vessel.Type, g) : "—", g.Any ? Styling.AccentTealSoft : Styling.TextMuted);
        }
    }

    /// <summary>Four dots: retrieval Optimal, surveillance T2, T3, favor — lit when the build meets the threshold.</summary>
    private static void DrawThresholds(Build? build, VesselType type, uint sectorId)
    {
        var t = ExpModel.ThresholdsFor(type, sectorId);
        if (!t.Known || build == null)
        {
            Styling.Text("—", Styling.TextMuted);
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y + ImGui.GetTextLineHeight() * 0.5f;
        var checks = type == VesselType.Submarine
            ? new (bool Met, string Tip)[]
            {
                (build.Retrieval >= t.Optimal, $"Retrieval {build.Retrieval} / optimal {t.Optimal} (guaranteed +25%)"),
                (build.Surveillance >= t.T2, $"Surveillance {build.Surveillance} / tier 2 {t.T2}"),
                (build.Surveillance >= t.T3, $"Surveillance {build.Surveillance} / tier 3 {t.T3}"),
                (build.Favor >= t.Favor, $"Favor {build.Favor} / {t.Favor}"),
            }
            : new (bool Met, string Tip)[]
            {
                (build.Surveillance >= t.T2 && t.T2 > 0, $"Surveillance {build.Surveillance} / tier 2 {t.T2}"),
                (build.Surveillance >= t.T3, $"Surveillance {build.Surveillance} / tier 3 {t.T3}"),
                (build.Favor >= t.Favor && t.Favor > 0, $"Favor {build.Favor} / {t.Favor}"),
            };

        var x = origin.X + 4f * scale;
        foreach (var (met, tip) in checks)
        {
            dl.AddCircleFilled(new Vector2(x, y), 4f * scale, ImGui.GetColorU32(met ? Styling.AccentMint : Styling.WithAlpha(Styling.BorderDim, 0.8f)));
            x += 12f * scale;
        }

        ImGui.Dummy(new Vector2(checks.Length * 12f * scale, ImGui.GetTextLineHeight()));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(string.Join("\n", checks.Select(c => (c.Met ? "● " : "○ ") + c.Tip)));
    }
}
