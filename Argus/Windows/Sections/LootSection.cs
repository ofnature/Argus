using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Argus.Core;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Argus.Windows.Sections;

/// <summary>Voyage loot history with FC / type filters, per-sector totals and CSV export.</summary>
internal static class LootSection
{
    private const int MaxRows = 400;

    private static ulong filterFc;
    private static int filterType; // 0 both, 1 subs, 2 airships
    private static string lastExport = string.Empty;

    public static void Draw(Plugin plugin)
    {
        var data = plugin.Data;
        MainWindow.PageHeader("Loot", "What every voyage brought back, recorded from the result screen.");

        var scale = ImGuiHelpers.GlobalScale;
        var companies = plugin.Fleet.Store.Companies.ToList();
        var fcLabel = filterFc == 0 ? "All Free Companies" : companies.FirstOrDefault(c => c.Id == filterFc) is { } f ? $"«{f.Tag}» {f.CharacterName}" : "All Free Companies";
        ImGui.SetNextItemWidth(240f * scale);
        using (var combo = ImRaii.Combo("##loot_fc", fcLabel))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("All Free Companies", filterFc == 0)) filterFc = 0;
                foreach (var c in companies)
                    if (ImGui.Selectable($"«{c.Tag}» {c.CharacterName}", filterFc == c.Id)) filterFc = c.Id;
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f * scale);
        using (var combo = ImRaii.Combo("##loot_type", filterType switch { 1 => "Submarines", 2 => "Airships", _ => "Both types" }))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("Both types", filterType == 0)) filterType = 0;
                if (ImGui.Selectable("Submarines", filterType == 1)) filterType = 1;
                if (ImGui.Selectable("Airships", filterType == 2)) filterType = 2;
            }
        }

        ImGui.SameLine();
        if (Buttons.Action("Export CSV", plugin.Loot.Count > 0, 110f * scale))
            Export(plugin);
        Styling.Tooltip("Copies the whole history as CSV to the clipboard and writes loot-export.csv next to the plugin config.");
        if (lastExport.Length > 0)
        {
            ImGui.SameLine();
            Styling.Text(lastExport, Styling.TextMuted);
        }

        var rows = plugin.Loot.Snapshot()
            .Where(e => filterFc == 0 || e.FreeCompanyId == filterFc)
            .Where(e => filterType == 0 || (filterType == 1 ? e.Type == VesselType.Submarine : e.Type == VesselType.Airship))
            .ToList();

        Styling.VSpace(4f);
        var gap = 6f * scale;
        var tile = (ImGui.GetContentRegionAvail().X - gap * 2) / 3f;
        var voyages = rows.Select(e => (e.FreeCompanyId, e.Type, e.Slot, e.Voyage)).Distinct().Count();
        StatTile.Draw("Sector hauls", Formatting.Number(rows.Count), null, Styling.AccentTeal, tile);
        ImGui.SameLine(0, gap);
        StatTile.Draw("Voyages", Formatting.Number(voyages), null, Styling.AccentBlue, tile);
        ImGui.SameLine(0, gap);
        StatTile.Draw("EXP recorded", Formatting.Number(rows.Sum(e => (long)e.ExpGained)), null, Styling.AccentAmber, tile);
        Styling.VSpace(6f);

        if (rows.Count == 0)
        {
            Styling.Text("Nothing recorded yet. Collect a returned vessel and its result screen will be captured.", Styling.TextMuted);
            return;
        }

        if (ImGui.CollapsingHeader("Totals by sector"))
            DrawTotals(plugin, rows);

        using var table = ImRaii.Table("##loot_rows", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY, new System.Numerics.Vector2(0, 0));
        if (!table.Success)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("When");
        ImGui.TableSetupColumn("Vessel", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Sector", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Rating");
        ImGui.TableSetupColumn("EXP");
        ImGui.TableSetupColumn("Items", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var e in rows.Take(MaxRows))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Styling.Text(e.CollectedUtc.ToLocalTime().ToString("MM-dd HH:mm"), Styling.TextDim);
            ImGui.TableNextColumn();
            Styling.Text($"{e.VesselName} R{e.Rank}", Styling.TextStrong);
            ImGui.TableNextColumn();
            var sector = data.Sectors(e.Type).TryGetValue(e.Sector, out var s) ? $"{s.Letter}. {s.Name}" : e.Sector == 0 ? "?" : e.Sector.ToString();
            Styling.Text(sector, Styling.TextSecondary);
            if (e.UnlockedSector != 0 || e.SlotUnlocked || e.FirstExploration)
            {
                ImGui.SameLine();
                var tags = new List<string>();
                if (e.FirstExploration) tags.Add("first");
                if (e.UnlockedSector != 0) tags.Add("unlock");
                if (e.SlotUnlocked) tags.Add("slot");
                Pill.Draw(string.Join(" · ", tags), Styling.AccentTeal, 0.7f);
            }

            ImGui.TableNextColumn();
            Styling.Text(e.Rating, Styling.TextSecondary);
            ImGui.TableNextColumn();
            Styling.Text(Formatting.Number(e.ExpGained), Styling.TextSecondary);
            ImGui.TableNextColumn();
            Styling.Text(ItemText(e), Styling.TextSecondary);
        }
    }

    private static string ItemText(LootEntry e)
    {
        var parts = new List<string>();
        if (e.PrimaryItem != 0)
            parts.Add($"{Sheets.ItemName(e.PrimaryItem)} ×{e.PrimaryCount}{(e.PrimaryHq ? " HQ" : "")}");
        if (e.AdditionalItem != 0)
            parts.Add($"{Sheets.ItemName(e.AdditionalItem)} ×{e.AdditionalCount}{(e.AdditionalHq ? " HQ" : "")}");
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    private static void DrawTotals(Plugin plugin, List<LootEntry> rows)
    {
        var data = plugin.Data;
        var bySector = rows.GroupBy(e => (e.Type, e.Sector)).OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Sector);
        using var table = ImRaii.Table("##loot_totals", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Sector", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Hauls");
        ImGui.TableSetupColumn("Avg EXP");
        ImGui.TableSetupColumn("Items (total)", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var g in bySector)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var name = data.Sectors(g.Key.Type).TryGetValue(g.Key.Sector, out var s) ? $"{s.Letter}. {s.Name}" : g.Key.Sector.ToString();
            Styling.Text($"{(g.Key.Type == VesselType.Submarine ? "S" : "A")} · {name}", Styling.TextStrong);
            ImGui.TableNextColumn();
            Styling.Text(g.Count().ToString(), Styling.TextSecondary);
            ImGui.TableNextColumn();
            Styling.Text(Formatting.Number((long)g.Average(e => e.ExpGained)), Styling.TextSecondary);
            ImGui.TableNextColumn();
            var items = g.SelectMany(e => new[] { (e.PrimaryItem, e.PrimaryCount), (e.AdditionalItem, e.AdditionalCount) })
                .Where(t => t.Item1 != 0)
                .GroupBy(t => t.Item1)
                .OrderByDescending(x => x.Sum(t => t.Item2))
                .Take(6)
                .Select(x => $"{Sheets.ItemName(x.Key)} ×{x.Sum(t => t.Item2)}");
            Styling.Text(string.Join(", ", items), Styling.TextSecondary);
        }
    }

    private static void Export(Plugin plugin)
    {
        try
        {
            var csv = plugin.Loot.ToCsv(plugin.Data, Sheets.ItemName);
            ImGui.SetClipboardText(csv);
            var path = Path.Combine(Service.PluginInterface.GetPluginConfigDirectory(), "loot-export.csv");
            File.WriteAllText(path, csv);
            lastExport = $"copied · {path}";
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: loot export failed");
            lastExport = "export failed, see log";
        }
    }
}
