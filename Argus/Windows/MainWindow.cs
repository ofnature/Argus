using System;
using System.Numerics;
using Argus.Core.Model;
using Argus.Windows.Components;
using Argus.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Argus.Windows;

public sealed class MainWindow : Window, IDisposable
{
    public enum Page { Overview, Vessels, Planner, Builder, Loot, Settings, Debug }

    private readonly Plugin plugin;
    private Page page = Page.Overview;

    public MainWindow(Plugin plugin) : base("Argus###ArgusMain")
    {
        this.plugin = plugin;
        Size = new Vector2(860, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public void ShowPage(Page target)
    {
        page = target;
        IsOpen = true;
    }

    public override void Draw()
    {
        using var style = Styling.PushWindowStyle();

        ArgusHeader.Draw(plugin);
        Styling.VSpace(6f);

        var sidebarWidth = Layout.SidebarWidth * ImGuiHelpers.GlobalScale;
        using (ImRaii.Child("##argus_sidebar", new Vector2(sidebarWidth, -1), false))
            DrawSidebar();

        ImGui.SameLine();

        using (ImRaii.Child("##argus_content", new Vector2(-1, -1), false))
        {
            switch (page)
            {
                case Page.Overview: OverviewSection.Draw(plugin); break;
                case Page.Vessels: VesselsSection.Draw(plugin); break;
                case Page.Planner: PlannerSection.Draw(plugin); break;
                case Page.Builder: BuilderSection.Draw(plugin); break;
                case Page.Loot: LootSection.Draw(plugin); break;
                case Page.Settings: SettingsSection.Draw(plugin); break;
#if DEBUG
                case Page.Debug: DebugSection.Draw(plugin); break;
#endif
            }
        }
    }

    private void DrawSidebar()
    {
        var now = DateTime.UtcNow;
        var subs = plugin.Fleet.CountsFor(VesselType.Submarine, now);
        var air = plugin.Fleet.CountsFor(VesselType.Airship, now);
        var ready = subs.Ready + air.Ready;
        var readyBadge = ready > 0 ? ready.ToString() : null;

        ImGui.Spacing();
        if (SidebarTab.Draw("Overview", FontAwesomeIcon.Eye, page == Page.Overview, readyBadge)) page = Page.Overview;
        if (SidebarTab.Draw("Vessels", FontAwesomeIcon.Ship, page == Page.Vessels)) page = Page.Vessels;
        if (SidebarTab.Draw("Planner", FontAwesomeIcon.Route, page == Page.Planner)) page = Page.Planner;
        if (SidebarTab.Draw("Builder", FontAwesomeIcon.Tools, page == Page.Builder)) page = Page.Builder;
        if (SidebarTab.Draw("Loot", FontAwesomeIcon.Gem, page == Page.Loot)) page = Page.Loot;
        if (SidebarTab.Draw("Settings", FontAwesomeIcon.Cog, page == Page.Settings)) page = Page.Settings;
#if DEBUG
        if (SidebarTab.Draw("Debug", FontAwesomeIcon.Bug, page == Page.Debug)) page = Page.Debug;
#endif
    }

    internal static void PageHeader(string title, string subtitle)
    {
        ImGui.SetWindowFontScale(1.45f);
        Styling.Text(title, Styling.TextStrong);
        ImGui.SetWindowFontScale(1f);
        Styling.TextWrapped(subtitle, Styling.TextMuted);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }
}
