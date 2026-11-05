using System;
using Argus.Core;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Core.Store;
using Argus.Windows;
using Argus.Windows.Dock;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

namespace Argus;

public sealed class Plugin : IDalamudPlugin
{
    public const string PluginVersion = "0.1.2";

    private const string CommandName = "/argus";

    /// <summary>Short alias, fleet convention (/dae, /cha, /seal). No native TextCommand or alias is "/arg" (checked 2026-09-05).</summary>
    private const string CommandAlias = "/arg";

    internal static Plugin Instance { get; private set; } = null!;

    internal Configuration Config { get; }
    internal GameData Data { get; }
    internal FleetService Fleet { get; }
    internal PlannerService Planner { get; }
    internal PlannerInterop PlannerInterop { get; }
    internal PartsInterop PartsInterop { get; }
    internal RepairInterop RepairInterop { get; }
    internal VoyageRunner Runner { get; }
    internal FleetSync FleetSync { get; }
    internal PartLocator PartLocator { get; }
    internal CharonFetch CharonFetch { get; }
    internal LootStore Loot { get; }
    internal MainWindow MainWindow { get; }

    private readonly WindowSystem windowSystem = new("Argus");
    private readonly PlannerOverlay plannerOverlay;
    private readonly MapSelectOverlay mapSelectOverlay;
    private readonly DockLayoutShare dockLayout;
    private readonly EdgeDockWindow edgeDock;
    private readonly RepairOverlay repairOverlay;
    private readonly VoyageRunOverlay runOverlay;
    private readonly DtrStatus dtr;
    private readonly LootHook lootHook;
    private readonly ArgusIpc ipc;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Instance = this;
        pluginInterface.Create<Service>();
        ECommonsMain.Init(pluginInterface, this);

        Sheets.Initialize();
        Data = GameDataLoader.Load();
        ClientVoyageMath.Install();

        Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Fleet = new FleetService(Config, Service.PluginInterface.GetPluginConfigDirectory());

        Planner = new PlannerService(this);
        PlannerInterop = new PlannerInterop();
        PartsInterop = new PartsInterop();
        RepairInterop = new RepairInterop();
        Runner = new VoyageRunner(this);
        FleetSync = new FleetSync(pluginInterface, Fleet, Config);
        PartLocator = new PartLocator(pluginInterface);
        CharonFetch = new CharonFetch(pluginInterface);
        Loot = new LootStore(Service.PluginInterface.GetPluginConfigDirectory());
        lootHook = new LootHook(this, Loot);
        lootHook.Recorded += OnLootRecorded;

        MainWindow = new MainWindow(this);
        plannerOverlay = new PlannerOverlay(this);
        repairOverlay = new RepairOverlay(this);
        mapSelectOverlay = new MapSelectOverlay(this);
        runOverlay = new VoyageRunOverlay(this);
        windowSystem.AddWindow(MainWindow);
        windowSystem.AddWindow(plannerOverlay);
        windowSystem.AddWindow(repairOverlay);
        windowSystem.AddWindow(mapSelectOverlay);
        windowSystem.AddWindow(runOverlay);

        // The edge tab: the fleet at a glance behind a tab on a screen edge, at its own edge and spot (dragging
        // stores new ones). The layout is shared with every other plugin's dock so none cover each other; tabs
        // wanting the same spot go in order: Daedalus 10, Charon 20, Ariadne and Mercury 30, Demeter 40, Argus 50.
        dockLayout = new DockLayoutShare(pluginInterface, "Argus", order: 50);
        edgeDock = new EdgeDockWindow(
            [new EdgeDockWindow.Tab("main", "Argus", () => Service.ClientState.IsLoggedIn, () => DockPanel.Draw(this), DockPanel.Width)],
            _ => (Config.EdgeDockEdge, Config.EdgeDockAlong),
            (_, edge, along) =>
            {
                (Config.EdgeDockEdge, Config.EdgeDockAlong) = (edge, along);
                Config.Save();
            },
            dockLayout);
        windowSystem.AddWindow(edgeDock);

        dtr = new DtrStatus(Fleet, Config, () => MainWindow.IsOpen = true);
        ipc = new ArgusIpc(this);

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
        PlannerInterop.Update(now);
        PartsInterop.Update(now);
        RepairInterop.Update(now);
        Runner.Update(now);
        FleetSync.Update(now);
        edgeDock.IsOpen = Config.EdgeDockEnabled && edgeDock.AnyTab();
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

    private void OnLootRecorded(System.Collections.Generic.IReadOnlyList<Argus.Core.Model.LootEntry> _) => Loot.SaveIfDirty();

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

        ipc.Dispose();
        FleetSync.Dispose();
        lootHook.Recorded -= OnLootRecorded;
        lootHook.Dispose();
        Loot.SaveIfDirty();
        Fleet.Store.SaveIfDirty();
        dtr.Dispose();
        windowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        plannerOverlay.Dispose();
        repairOverlay.Dispose();
        mapSelectOverlay.Dispose();
        runOverlay.Dispose();
        dockLayout.Dispose();
        Planner.Dispose();

        ClientVoyageMath.Uninstall();
        ECommonsMain.Dispose();
    }
}
