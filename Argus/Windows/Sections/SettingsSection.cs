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

        using (var g = SettingsGroup.Begin("Collect and resend"))
        {
            changed |= g.Toggle("Start when I open the Voyage Control Panel",
                "When the panel opens with a vessel back, run collect and resend without the click: finalize each report, repair parts at 0%, and send each vessel out on the route its Argus planner picks. Once per opening; the Stop button beside the panel ends it. The same as pressing Collect and resend.",
                ref cfg.AutoRunAtPanel);
        }

        using (var g = SettingsGroup.Begin("Edge tab"))
        {
            changed |= g.Toggle("Show an Argus tab on the screen edge",
                "Like Daedalus, Charon and Ariadne: point at the tab and the fleet pops out beside it (every vessel's timer, voyage slots, supplies, and buttons into this window); click it to keep it out, click again to let it go. Tabs on the same edge sit side by side. This window still opens with /argus.",
                ref cfg.EdgeDockEnabled);

            if (cfg.EdgeDockEnabled)
            {
                g.Row("Tab edge", "Which edge of the screen the tab sits on. You can also drag the tab: let go near any edge, anywhere along it, and it stays there.", 210f, () =>
                {
                    var edge = (int)cfg.EdgeDockEdge;
                    ImGui.SetNextItemWidth(110f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
                    if (ImGui.Combo("##dock_edge", ref edge, "Left\0Right\0Top\0Bottom\0"))
                    {
                        cfg.EdgeDockEdge = (Argus.Windows.Dock.DockEdge)edge;
                        cfg.Save();
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Centre"))
                    {
                        cfg.EdgeDockAlong = 0.5f;
                        cfg.Save();
                    }

                    Styling.Tooltip("Put the tab back in the middle of its edge.");
                });
            }
        }

        using (var g = SettingsGroup.Begin("Share over the LAN"))
        {
            changed |= g.Toggle("Share the fleet with other PCs",
                "Through Daedalus's LAN relay, both ways: every game client with this on, and Daedalus's LAN enabled, sends the vessels it knows and takes newer readings from the others. Timers, builds, part condition and supplies, with FC tags and character names; nothing else.",
                ref cfg.ShareFleetOverLan);

            if (cfg.ShareFleetOverLan)
            {
                var sync = plugin.FleetSync;
                var now = System.DateTime.UtcNow;
                if (sync.DaedalusMissing)
                {
                    g.Note("Daedalus is not loaded, so nothing is being shared.", Styling.AccentRose);
                }
                else
                {
                    var sent = sync.LastSentUtc == default ? "nothing sent yet" : $"last sent {Formatting.Duration(now - sync.LastSentUtc)} ago";
                    var heard = sync.Received == 0
                        ? "nothing received yet (is Daedalus's LAN on, here and on the other PC?)"
                        : $"{sync.Received} received, last {Formatting.Duration(now - sync.LastReceivedUtc)} ago";
                    g.Note($"{sent} · {heard}");
                }

                g.Row("Planner settings from", "The character whose vessel settings (gil farmer, progression, levelling, map, time cap) every PC follows. Put the same name on every PC: the PC logged in as that character sends them, every other PC takes them in place of its own. Leave empty to keep settings on each PC.", 260f, () =>
                {
                    var master = cfg.SettingsMaster;
                    ImGui.SetNextItemWidth(170f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
                    if (ImGui.InputTextWithHint("##settings_master", "character name", ref master, 32))
                    {
                        cfg.SettingsMaster = master;
                        cfg.Save();
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton("Me") && Service.ObjectTable.LocalPlayer is { } me)
                    {
                        cfg.SettingsMaster = me.Name.TextValue;
                        cfg.Save();
                    }

                    Styling.Tooltip("Use the character logged in here.");
                });

                if (!string.IsNullOrWhiteSpace(cfg.SettingsMaster))
                {
                    g.Note(sync.IsSettingsMaster
                        ? "This PC is the master: its vessel settings go to every PC that follows it."
                        : sync.PrefsReceived == 0
                            ? $"Following {cfg.SettingsMaster.Trim()}: nothing received yet (is that PC on, sharing, and logged in as them?)."
                            : $"Following {cfg.SettingsMaster.Trim()}: {sync.PrefsReceived} received, last {Formatting.Duration(now - sync.LastPrefsReceivedUtc)} ago. Changes made here are replaced by theirs.");
                }
            }
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
