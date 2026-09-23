using System;
using System.Linq;
using System.Numerics;
using Argus.Core.Calc;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Argus.Windows.Sections;

/// <summary>One card per vessel: state, rank and EXP bar, build with the five stats, part condition, current route, timers.</summary>
internal static class VesselsSection
{
    public static void Draw(Plugin plugin)
    {
        var now = DateTime.UtcNow;
        MainWindow.PageHeader("Vessels", "Builds, stats and EXP progress for every vessel Argus has seen.");

        var companies = plugin.Fleet.VisibleCompanies().ToList();
        if (companies.Count == 0)
        {
            Styling.Text("No vessels yet. Stand in a company workshop to read them.", Styling.TextMuted);
            return;
        }

        foreach (var fc in companies)
        {
            var title = string.IsNullOrEmpty(fc.Tag) ? $"FC {fc.Id:X}" : $"«{fc.Tag}»";
            Styling.SectionLabel($"{title} · {fc.CharacterName}");
            Styling.VSpace(2f);
            foreach (var v in fc.Vessels.OrderBy(v => v.Type).ThenBy(v => v.Slot))
                DrawVessel(plugin, v, now);
        }
    }

    private static void DrawVessel(Plugin plugin, Vessel v, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var returned = v.IsReturned(now);
        var accent = Styling.StateColor(returned, v.IsDeployed);

        // Every card carries a Repair button, and ImGui keys buttons by label.
        using var id = ImRaii.PushId(Configuration.VesselKey(v));
        var origin = Card.BeginFlat();

        // Title row: name, type, state pill, timer right-aligned.
        Styling.TextScaled(v.Name, Styling.TextStrong, 1.15f);
        ImGui.SameLine();
        Styling.Text(v.Type == VesselType.Submarine ? "submarine" : "airship", Styling.TextDim);
        ImGui.SameLine();
        Pill.Draw(returned ? "RETURNED" : v.IsDeployed ? "OUT" : "IDLE", accent);

        var timer = returned ? $"back since {Formatting.Duration(now - v.ReturnUtc)}"
            : v.IsDeployed ? $"returns in {Formatting.Duration(v.Remaining(now))} · {Formatting.LocalTime(v.ReturnUtc)}"
            : "in the workshop";
        var timerSize = ImGui.CalcTextSize(timer);
        ImGui.SameLine(width - 11f * scale - timerSize.X);
        Styling.Text(timer, returned ? Styling.AccentAmberSoft : Styling.TextSecondary);

        // Rank + EXP bar.
        var data = plugin.Data;
        var last = data.LastRank(v.Type);
        var toNext = v.Rank >= last ? 0u : data.Rank(v.Type, v.Rank).ExpToNext;
        var frac = toNext == 0 ? 1f : Math.Clamp(v.CurrentExp / (float)toNext, 0f, 1f);
        var rankLabel = v.Rank >= last ? $"Rank {v.Rank} (max)" : $"Rank {v.Rank}";
        Styling.Text(rankLabel, Styling.TextSecondary);
        ImGui.SameLine();
        Styling.Text(toNext == 0 ? "" : $"{Formatting.Number(v.CurrentExp)} / {Formatting.Number(toNext)} EXP", Styling.TextDim);
        DrawBar(frac, width - 22f * scale, v.Type == VesselType.Submarine ? Styling.AccentTeal : Styling.AccentBlue);

        // Build line.
        Build? build = null;
        try { build = Build.From(data, v); } catch (Exception) { /* unknown part ids after a patch */ }

        if (build != null)
        {
            var parts = string.Join(" · ", build.Parts.Select((p, i) =>
                v.Type == VesselType.Submarine
                    ? $"{Build.SlotName(v.Type, i)}: {Build.SubmarineClassName(p.Id)}"
                    : $"{Build.SlotName(v.Type, i)}: {Build.AirshipClassName(p.Class)}"));
            Styling.Text($"{build.Identifier}  ", Styling.TextStrong);
            ImGui.SameLine();
            Styling.Text(parts, Styling.TextDim);
        }

        // Stats strip: game-reported totals (rank bonus included for subs).
        var gap = 6f * scale;
        var tile = (width - 22f * scale - gap * 4) / 5f;
        Styling.VSpace(2f);
        StatTile.Draw("Surveillance", v.Surveillance.ToString(), null, Styling.AccentCyan, tile);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Retrieval", v.Retrieval.ToString(), null, Styling.AccentMint, tile);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Speed", v.Speed.ToString(), null, Styling.AccentBlue, tile);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Range", v.Range.ToString(), null, Styling.AccentViolet, tile);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Favor", v.Favor.ToString(), null, Styling.AccentAmber, tile);

        DrawCondition(plugin, v);

        if (v.Type == VesselType.Submarine && v.Points.Count > 0)
        {
            var names = v.Points.Select(p => data.SubmarineSectors.TryGetValue(p, out var s) ? s.Letter : p.ToString());
            Styling.Text($"Current route: {string.Join(" → ", names)}", Styling.TextSecondary);
            if (build != null)
            {
                var route = v.Points.Where(p => data.SubmarineSectors.ContainsKey(p)).Select(p => data.SubmarineSectors[p]).ToList();
                if (route.Count > 0)
                {
                    var exp = ExpModel.Route(build, route);
                    var after = RankSim.Apply(data, v.Type, v.Rank, v.CurrentExp, exp.Guaranteed);
                    ImGui.SameLine();
                    Styling.Text($"· {Formatting.Number(exp.Guaranteed)}–{Formatting.Number(exp.Maximum)} EXP · rank {after.Rank} on return", Styling.TextDim);
                }
            }
        }

        Card.EndFlat(origin, width, Styling.CardBgSoft, accent);
        Styling.VSpace(4f);
    }

    /// <summary>Each part's condition, and a Repair button once one of them is broken.</summary>
    private static void DrawCondition(Plugin plugin, Vessel v)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (!v.ConditionKnown)
        {
            Styling.Text("Part condition not read yet: select the vessel on the Voyage Control Panel.", Styling.TextDim);
            return;
        }

        Styling.Text("Condition", Styling.TextSecondary);
        for (var slot = 0; slot < v.Condition.Length; slot++)
        {
            var c = v.Condition[slot];

            // Rounded up, so only a part that is actually broken reads 0%.
            var percent = c <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(c * 100.0 / Vessel.FullCondition));
            var color = c < 0 ? Styling.TextDim : c == 0 ? Styling.AccentRose : percent <= 25 ? Styling.AccentAmberSoft : Styling.TextDim;
            ImGui.SameLine();
            Styling.Text($"{Build.SlotName(v.Type, slot)} {(c < 0 ? "?" : $"{percent}%")}", color);
        }

        var broken = v.BrokenSlots();
        var repair = plugin.RepairInterop;
        var rowId = "repair:" + Configuration.VesselKey(v);
        if (broken.Count > 0)
        {
            var cost = RepairInterop.Cost(plugin.Data, v, broken);
            var kits = PartsInterop.Carried(Supplies.MagitekRepairMaterialsItem);
            var blocker = kits < cost ? $"needs {cost} Magitek Repair Materials, carrying {kits}"
                : repair.Blocker(v, plugin.PartsInterop.Running);

            var label = $"Repair {broken.Count} part{(broken.Count == 1 ? string.Empty : "s")} ({cost} kits)";
            if (Buttons.Action(label, blocker == null, 200f * scale, Styling.AccentAmber))
            {
                Service.Log.Information("Argus: repair pressed for {Vessel}, slots {Slots}", v.Name, string.Join(",", broken));
                if (!repair.Start(plugin.Data, v, plugin.PartsInterop.Running, rowId))
                    Service.Log.Information("Argus: repair refused: {Reason}", repair.LastError ?? repair.LastResult ?? "unknown");
            }

            if (blocker != null)
            {
                ImGui.SameLine();
                Styling.Text(blocker, Styling.TextMuted);
            }
        }

        // Only the card that asked: the interop's state is global, and every card would otherwise claim the result.
        if (repair.LastRunId != rowId)
            return;

        if (repair.Running)
            Styling.Text($"Repairing… {repair.Repaired} done, {repair.Remaining} to go ({repair.StageName})", Styling.PulseColor(Styling.AccentAmber, Styling.AccentAmberSoft));
        else if (repair.LastError != null)
            Styling.TextWrapped(repair.LastError, Styling.AccentRose);
        else if (repair.LastResult != null)
            Styling.Text(repair.LastResult, Styling.AccentMint);
    }

    private static void DrawBar(float fraction, float width, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var height = 6f * scale;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.5f)), height * 0.5f);
        if (fraction > 0f)
            dl.AddRectFilled(origin, origin + new Vector2(width * fraction, height), ImGui.GetColorU32(color), height * 0.5f);
        ImGui.Dummy(new Vector2(width, height + 4f * scale));
    }
}
