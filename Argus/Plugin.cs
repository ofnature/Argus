using System;
using Argus.Core;
using Argus.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

namespace Argus;

public sealed class Plugin : IDalamudPlugin
{
    public const string PluginVersion = "0.1.0";

    private const string CommandName = "/argus";

    /// <summary>Short alias, fleet convention (/dae, /cha, /seal). No native TextCommand or alias is "/arg" (checked 2026-09-05).</summary>
    private const string CommandAlias = "/arg";

    internal static Plugin Instance { get; private set; } = null!;

    internal Configuration Config { get; }
    internal FleetService Fleet { get; }
    internal MainWindow MainWindow { get; }

    private readonly WindowSystem windowSystem = new("Argus");
    private readonly DtrStatus dtr;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Instance = this;
        pluginInterface.Create<Service>();
        ECommonsMain.Init(pluginInterface, this);

        Sheets.Initialize();

        Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Fleet = new FleetService(Config, Service.PluginInterface.GetPluginConfigDirectory());

        MainWindow = new MainWindow(this);
        windowSystem.AddWindow(MainWindow);

        dtr = new DtrStatus(Fleet, Config, () => MainWindow.IsOpen = true);

        Fleet.WorkshopEntered += OnWorkshopEntered;

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Argus. /argus planner | loot | config",
        });
        Service.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Short alias for /argus.",
        });

        Service.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        Service.Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        Fleet.Update(now);
        dtr.Update(now);
    }

    private void OnWorkshopEntered()
    {
        if (Config.OpenOnWorkshopEnter)
            MainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();
        if (arg.Equals("config", StringComparison.OrdinalIgnoreCase) || arg.Equals("settings", StringComparison.OrdinalIgnoreCase))
            MainWindow.ShowPage(MainWindow.Page.Settings);
        else if (arg.Equals("planner", StringComparison.OrdinalIgnoreCase) || arg.Equals("plan", StringComparison.OrdinalIgnoreCase))
            MainWindow.ShowPage(MainWindow.Page.Planner);
        else if (arg.Equals("loot", StringComparison.OrdinalIgnoreCase))
            MainWindow.ShowPage(MainWindow.Page.Loot);
        else if (arg.Equals("vessels", StringComparison.OrdinalIgnoreCase))
            MainWindow.ShowPage(MainWindow.Page.Vessels);
        else
            MainWindow.Toggle();
    }

    private void OpenMain() => MainWindow.IsOpen = true;

    private void OpenConfig() => MainWindow.ShowPage(MainWindow.Page.Settings);

    public void Dispose()
    {
        Service.Framework.Update -= OnUpdate;
        Service.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        Fleet.WorkshopEntered -= OnWorkshopEntered;

        Service.CommandManager.RemoveHandler(CommandName);
        Service.CommandManager.RemoveHandler(CommandAlias);

        Fleet.Store.SaveIfDirty();
        dtr.Dispose();
        windowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        ECommonsMain.Dispose();
    }
}
