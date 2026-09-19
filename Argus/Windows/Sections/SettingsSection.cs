using System.Linq;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;

namespace Argus.Windows.Sections;

internal static class SettingsSection
{
    public static void Draw(Plugin plugin)
    {
        var cfg = plugin.Config;
        MainWindow.PageHeader("Settings", "Server info bar, overlay and which Free Companies to show.");

        var changed = false;
        using (var g = SettingsGroup.Begin("Server info bar"))
        {
            changed |= g.Toggle("Show Argus on the server info bar", "Voyage slots in use against the shared limit of four, then ready/total per vessel type. Amber when something is waiting to be collected.", ref cfg.ShowDtrBar);
            changed |= g.Toggle("Include submarines", null, ref cfg.DtrShowSubmarines);
            changed |= g.Toggle("Include airships", null, ref cfg.DtrShowAirships);
        }

        using (var g = SettingsGroup.Begin("Voyage planner overlay"))
        {
            changed |= g.Toggle("Show the route overlay next to the in-game planner",
                "When the voyage window is open, Argus shows the suggested route and an Apply button that selects the sectors for you. You still press Deploy.",
                ref cfg.ShowPlannerOverlay);
            changed |= g.Toggle("Open Argus when entering a workshop", null, ref cfg.OpenOnWorkshopEnter);
        }

        var companies = plugin.Fleet.Store.Companies.ToList();
        if (companies.Count > 0)
        {
            using var g = SettingsGroup.Begin("Free Companies");
            foreach (var fc in companies)
            {
                var shown = !cfg.HiddenFreeCompanies.Contains(fc.Id);
                var label = string.IsNullOrEmpty(fc.Tag) ? $"FC {fc.Id:X}" : $"«{fc.Tag}» · {fc.CharacterName}";
                if (g.Toggle(label, "Hidden FCs are left out of the overview and the server info bar but their data is kept.", ref shown))
                {
                    if (shown) cfg.HiddenFreeCompanies.Remove(fc.Id);
                    else cfg.HiddenFreeCompanies.Add(fc.Id);
                    changed = true;
                }
            }

            g.Note("Vessels are re-read every time a character of that FC stands in its workshop.");
        }

        if (changed)
            cfg.Save();

        ImGui.Spacing();
        Styling.Text($"Argus {Plugin.PluginVersion}", Styling.TextMuted);
    }
}
