using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Data;
using Argus.Core.Model;
using Argus.Windows;
using Argus.Windows.Sections;
using Dalamud.Game.ClientState.Conditions;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Argus.Core.Game;

/// <summary>
/// "Collect and resend": from the Voyage Control Panel, every vessel that is back from a voyage has its report
/// finalized (the loot), its broken parts repaired, and goes out again on the route its own Argus planner picks now:
/// progression moves on to the next unlock, farmers keep farming. One button press runs the whole panel; the player
/// still opens it.
///
/// <para>It reacts to whatever is on screen rather than replaying a fixed sequence, the way AutoRetainer's voyage
/// scheduler does: the panel's type selector, a vessel list, the voyage report, the vessel's menu, the map list, the
/// planner. The steps that spend something are the ones Argus already had, with their own checks: the repair
/// (<see cref="RepairInterop"/>, 0% parts only, kits counted, the prompt must name the part) and the planner's apply
/// and deploy (<see cref="PlannerInterop"/>, only when the game would accept the voyage). A window it does not
/// recognise, a prompt it did not ask for, or a step that goes nowhere stops the run and leaves the screen to the
/// player.</para>
/// </summary>
internal sealed unsafe class VoyageRunner
{
    private const string ResultAddon = "AirShipExplorationResult";
    private const string DetailAddon = "AirShipExplorationDetail";
    private const string ConfirmAddon = "SelectYesno";

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    /// <summary>After a click, how long to let the game move on before clicking again.</summary>
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(1200);

    /// <summary>How long the screen may stay the same, with nothing being waited on, before the run gives up.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The departure cutscene after a deploy, and anything else the game plays.</summary>
    private static readonly TimeSpan CutsceneTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a window the run did not expect may stay up before it stops: usually it is one closing as the next
    /// opens (the components window after a repair, the planner after a deploy).
    /// </summary>
    private static readonly TimeSpan UnexpectedGrace = TimeSpan.FromSeconds(2.5);

    /// <summary>How many times a vessel is picked from its list before the run decides it will not open.</summary>
    private const int MaxPicks = 3;

    public enum TargetState
    {
        Pending,

        /// <summary>Clicked in its list; its voyage report should open.</summary>
        Picking,

        /// <summary>Report finalized; waiting for its menu, with the voyage cleared from the workshop data.</summary>
        Collecting,

        /// <summary>At its own menu: repair, plan, deploy.</summary>
        AtMenu,

        Repairing,

        /// <summary>Deploy chosen: map list, planner, apply, confirm, cutscene.</summary>
        Deploying,

        /// <summary>Being taken back to its list without a voyage.</summary>
        Leaving,

        Sent,
        Skipped,
    }

    public sealed class Target(VesselType type, int slot, string name)
    {
        public VesselType Type { get; } = type;
        public int Slot { get; } = slot;
        public string Name { get; } = name;
        public TargetState State { get; set; } = TargetState.Pending;

        /// <summary>What happened, for the panel: the route it went on, or why it stayed.</summary>
        public string Note { get; set; } = string.Empty;

        internal int Picks;
        internal bool Planned;
        internal bool RepairTried;
        internal bool MapChosen;
        internal bool Applied;
        internal uint ReturnBefore;
        internal string Route = string.Empty;

        public bool Finished => State is TargetState.Sent or TargetState.Skipped;
    }

    private readonly Plugin plugin;
    private readonly List<Target> targets = new();
    private Target? current;
    private ulong fcId;
    private DateTime lastStep = DateTime.MinValue;
    private DateTime lastClick = DateTime.MinValue;
    private DateTime lastProgress;
    private string progressSignature = string.Empty;
    private string? oddReason;
    private bool panelSeen;
    private DateTime panelLastSeen;
    private DateTime lastAutoCheck = DateTime.MinValue;
    private DateTime oddSince;
    private bool oddThisStep;

    public VoyageRunner(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public bool Running { get; private set; }

    public IReadOnlyList<Target> Targets => targets;

    /// <summary>What the run is doing right now, for the panel.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Why the last run stopped early, or null when it finished (or none ran).</summary>
    public string? LastError { get; private set; }

    public DateTime FinishedUtc { get; private set; } = DateTime.MinValue;

    /// <summary>Where the panel is, from its SelectString; Unknown when it is not one of the panel's lists.</summary>
    public PanelText.Screen Screen()
    {
        var addon = PartsInterop.Addon(PartsInterop.MenuAddon);
        if (addon == null || !WorkshopReader.IsWorkshopLoaded())
            return PanelText.Screen.Unknown;

        var menu = new AddonMaster.SelectString(addon);
        return PanelText.Classify(menu.Text, menu.Entries.Select(e => e.Text).ToList(), KnownVessels());
    }

    private List<Vessel> KnownVessels()
    {
        var id = plugin.Fleet.CurrentFreeCompanyId;
        return id != 0 && plugin.Fleet.Store.TryGet(id, out var fc) ? fc.Vessels : new List<Vessel>();
    }

    /// <summary>Screen rectangle of the panel's open menu, for docking beside it.</summary>
    public static (float X, float Y, float W, float H)? MenuRect() => RepairInterop.MenuRect();

    /// <summary>Why a run cannot start from what is on screen, or null when it can.</summary>
    public string? Blocker(DateTime nowUtc)
    {
        if (Running)
            return "already running";
        if (plugin.PartsInterop.Running || plugin.RepairInterop.Running || plugin.PlannerInterop.Applying)
            return "another Argus action is running";

        var id = plugin.Fleet.CurrentFreeCompanyId;
        if (id == 0 || !plugin.Fleet.Store.TryGet(id, out var fc))
            return "open the Voyage Control Panel in your workshop";
        if (Screen() == PanelText.Screen.Unknown)
            return "open the Voyage Control Panel";
        if (PanelText.Returned(fc.Vessels, nowUtc).Count == 0)
            return "nothing is back yet";
        return null;
    }

    public bool Start(DateTime nowUtc)
    {
        LastError = null;
        if (Blocker(nowUtc) is { } blocker)
        {
            LastError = $"Cannot start: {blocker}.";
            return false;
        }

        fcId = plugin.Fleet.CurrentFreeCompanyId;
        plugin.Fleet.Store.TryGet(fcId, out var fc);
        targets.Clear();
        foreach (var v in PanelText.Returned(fc!.Vessels, nowUtc))
            targets.Add(new Target(v.Type, v.Slot, v.Name));

        current = null;
        Running = true;
        lastClick = DateTime.MinValue;
        lastProgress = nowUtc;
        progressSignature = string.Empty;
        Status = "Starting…";
        Service.Log.Information("Argus: collect and resend started for {Count} vessels: {Names}",
            targets.Count, string.Join(", ", targets.Select(t => t.Name)));
        return true;
    }

    /// <summary>How long the panel has to be gone before opening it again counts as a new opening.</summary>
    private static readonly TimeSpan PanelGone = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Start a run when the panel's first menu opens with something back, once per opening: a run that stopped, or the
    /// player walking back out to the first menu, does not start another until the panel has closed.
    /// </summary>
    public void AutoStart(DateTime nowUtc, bool enabled)
    {
        if (nowUtc - lastAutoCheck < TimeSpan.FromMilliseconds(500))
            return;
        lastAutoCheck = nowUtc;

        if (Running)
        {
            panelSeen = true;
            panelLastSeen = nowUtc;
            return;
        }

        var screen = Screen();
        if (screen == PanelText.Screen.Unknown)
        {
            if (panelSeen && nowUtc - panelLastSeen > PanelGone)
                panelSeen = false;
            return;
        }

        panelLastSeen = nowUtc;
        if (panelSeen)
            return;
        panelSeen = true;

        if (enabled && screen == PanelText.Screen.TypeSelector && Blocker(nowUtc) == null)
        {
            Service.Log.Information("Argus: the panel opened with vessels back; starting collect and resend");
            Start(nowUtc);
        }
    }

    /// <summary>Stopped by the player: also stops a repair or an apply that is mid-way.</summary>
    public void Cancel()
    {
        if (!Running)
            return;
        plugin.RepairInterop.Cancel();
        plugin.PlannerInterop.Cancel();
        Stop("Stopped.");
    }

    public void Update(DateTime nowUtc)
    {
        if (!Running || nowUtc - lastStep < Interval)
            return;
        lastStep = nowUtc;

        try
        {
            oddThisStep = false;
            Step(nowUtc);
            if (!oddThisStep)
                oddReason = null;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: collect and resend failed");
            Stop("Something went wrong; see the log.");
            return;
        }

        if (!Running)
            return;

        // A screen that has not changed while nothing is being waited on means the game did not take the last click.
        var cutscene = InCutscene();
        var signature = $"{OpenScreenName()}|{current?.Slot}|{current?.State}|{current?.MapChosen}|{current?.Applied}|{plugin.RepairInterop.StageName}|{plugin.Planner.Computing}|{cutscene}";
        if (signature != progressSignature)
        {
            progressSignature = signature;
            lastProgress = nowUtc;
        }
        else if (nowUtc - lastProgress > (cutscene ? CutsceneTimeout : StallTimeout))
        {
            Stop($"Nothing happened for a while ({Status.TrimEnd('…')}); the rest is left for you.");
        }
    }

    private void Step(DateTime nowUtc)
    {
        if (!WorkshopReader.IsWorkshopLoaded() || plugin.Fleet.CurrentFreeCompanyId != fcId)
        {
            Stop("Left the workshop.");
            return;
        }

        CheckCurrent(nowUtc);
        if (!Running)
            return;

        if (InCutscene())
        {
            Status = "Waiting for the cutscene…";
            return;
        }

        // The repair and the planner's apply click on their own; wait for them, then read how they went.
        if (plugin.RepairInterop.Running)
        {
            Status = $"Repairing {current?.Name}…";
            return;
        }

        if (plugin.PlannerInterop.Applying)
        {
            Status = plugin.PlannerInterop.Deploying ? $"Deploying {current?.Name}…" : $"Selecting {current?.Name}'s route…";
            return;
        }

        if (PartsInterop.Addon(ConfirmAddon) != null)
        {
            Unexpected("A prompt Argus did not ask for is open; it is left for you.", nowUtc);
            return;
        }

        if (PartsInterop.Addon(DetailAddon) != null)
        {
            Unexpected("The voyage details window is open without Argus deploying; it is left for you.", nowUtc);
            return;
        }

        if (PartsInterop.PartsWindow() != null)
        {
            Unexpected("A components window is open; the rest is left for you.", nowUtc);
            return;
        }

        var result = PartsInterop.Addon(ResultAddon);
        if (result != null)
        {
            OnReport(result, nowUtc);
            return;
        }

        if (plugin.PlannerInterop.IsPlannerOpen)
        {
            OnPlanner(nowUtc);
            return;
        }

        if (MapSelectInterop.IsOpen)
        {
            OnMapList(nowUtc);
            return;
        }

        var menu = PartsInterop.Addon(PartsInterop.MenuAddon);
        if (menu == null)
        {
            // The last vessel is out and the game closed the panel: nothing is left to walk out of.
            if (current == null && targets.All(t => t.Finished))
            {
                Finish();
                return;
            }

            Status = "Waiting for the game…";
            return;
        }

        OnMenu(menu, nowUtc);
    }

    /// <summary>Move the current vessel on when the workshop data says its step went through.</summary>
    private void CheckCurrent(DateTime nowUtc)
    {
        if (current == null)
            return;

        var vessel = Live(current);
        switch (current.State)
        {
            // Its return time was cleared with the report; a new one in the future means it is on its way.
            case TargetState.Deploying when vessel != null && vessel.IsOut(nowUtc) && vessel.ReturnTime != current.ReturnBefore:
                current.State = TargetState.Sent;
                current.Note = current.Route;
                Service.Log.Information("Argus: {Vessel} sent: {Route}", current.Name, current.Route);
                current = null;
                return;

            case TargetState.Deploying when current.Applied && !plugin.PlannerInterop.Applying && plugin.PlannerInterop.LastError is { } error:
                Stop($"{current.Name}: {error}");
                return;

            case TargetState.Repairing when !plugin.RepairInterop.Running:
                if (plugin.RepairInterop.LastError is { } repairError)
                    Skip(current, $"not repaired: {repairError}");
                else
                    current.State = TargetState.AtMenu;
                return;
        }
    }

    private void OnReport(AtkUnitBase* addon, DateTime nowUtc)
    {
        if (current is not { State: TargetState.Picking or TargetState.Collecting })
        {
            Unexpected("A voyage report Argus did not open is on screen; it is left for you.", nowUtc);
            return;
        }

        // The loot is recorded as the report opens (LootHook); finalizing hands it over and clears the voyage.
        Status = $"Collecting {current.Name}'s report…";
        if (!CanClick(nowUtc))
            return;

        current.State = TargetState.Collecting;
        Clicked(nowUtc, $"finalizing {current.Name}'s report");
        new AddonMaster.AirShipExplorationResult(addon).FinalizeReport();
    }

    private void OnMapList(DateTime nowUtc)
    {
        if (current is not { State: TargetState.Deploying, Type: VesselType.Submarine })
        {
            Unexpected("The map list is open without Argus deploying; it is left for you.", nowUtc);
            return;
        }

        Status = $"Choosing {current.Name}'s map…";
        if (current.MapChosen || !CanClick(nowUtc))
            return;

        current.MapChosen = true;
        Clicked(nowUtc, $"choosing map {plugin.Planner.Map} for {current.Name}");
        if (!MapSelectInterop.Select(plugin.Planner.Map, out var error))
            Stop($"{current.Name}: {error}");
    }

    private void OnPlanner(DateTime nowUtc)
    {
        if (current is not { State: TargetState.Deploying })
        {
            Unexpected("The voyage planner is open without Argus deploying; it is left for you.", nowUtc);
            return;
        }

        var open = plugin.PlannerInterop.CurrentVessel();
        if (open == null || open.Value.Type != current.Type || open.Value.Slot != current.Slot)
        {
            Status = $"Waiting for {current.Name}'s planner…";
            return;
        }

        if (current.Applied)
        {
            Status = $"Deploying {current.Name}…";
            return;
        }

        var vessel = Live(current);
        if (vessel == null)
        {
            Stop($"{current.Name} is not in the workshop data.");
            return;
        }

        // The planner may be on another map than the one planned (a sub), and lists every unlocked sector; plan on what it shows.
        var planner = plugin.Planner;
        planner.Select(vessel);
        planner.SetMap(open.Value.Map);
        if (vessel.Type == VesselType.Airship && Company() is { } company)
            PlannerOverlay.LearnAirshipUnlocks(plugin, company);
        planner.Refresh();
        if (planner.Computing)
        {
            Status = $"Planning {current.Name}'s route…";
            return;
        }

        var route = planner.ChosenRoute;
        if (route == null)
        {
            Stop($"{current.Name}: no route fits in the planner{IssueText(planner.Issues)}; the planner is left open for you.");
            return;
        }

        if (planner.DeployRefusal(vessel, nowUtc) is { } refusal)
        {
            Stop($"{current.Name}: {refusal}");
            return;
        }

        if (!CanClick(nowUtc))
            return;

        current.Route = $"{RouteLetters(vessel.Type, route.Sectors)} · {Formatting.VoyageLength(route.Duration)} · {PlannerSection.ModeLabel(plugin.Config.PlannerFor(vessel))}";
        Clicked(nowUtc, $"applying {current.Route} to {current.Name} and deploying");
        if (!plugin.PlannerInterop.ApplyRoute(plugin.Data, vessel.Type, route.Sectors, deployAfter: true))
        {
            Stop($"{current.Name}: {plugin.PlannerInterop.LastError}");
            return;
        }

        current.Applied = true;
    }

    private void OnMenu(AtkUnitBase* addon, DateTime nowUtc)
    {
        var menu = new AddonMaster.SelectString(addon);
        var entries = menu.Entries.Select(e => e.Text).ToList();

        // A vessel's own menu first: the lists never carry a repair entry.
        if (RepairInterop.IsVesselMenu(VesselType.Submarine) || RepairInterop.IsVesselMenu(VesselType.Airship))
        {
            OnVesselMenu(menu, entries, nowUtc);
            return;
        }

        switch (PanelText.Classify(menu.Text, entries, KnownVessels()))
        {
            case PanelText.Screen.TypeSelector:
                OnTypeSelector(menu, entries, nowUtc);
                return;
            case PanelText.Screen.SubmarineList:
                OnList(menu, entries, VesselType.Submarine, nowUtc);
                return;
            case PanelText.Screen.AirshipList:
                OnList(menu, entries, VesselType.Airship, nowUtc);
                return;
            default:
                Unexpected($"Stopped at a menu Argus does not know (\"{menu.Text.Trim()}\"); it is left for you.", nowUtc);
                return;
        }
    }

    private void OnTypeSelector(AddonMaster.SelectString menu, List<string> entries, DateTime nowUtc)
    {
        if (current != null && current.Finished)
            current = null;
        if (WaitingForVoyage())
            return;
        if (current != null)
        {
            Unexpected($"Back at the panel's first menu with {current.Name} unfinished; the rest is left for you.", nowUtc);
            return;
        }

        var next = targets.FirstOrDefault(t => !t.Finished);
        if (next == null)
        {
            Status = "Done.";
            if (!CanClick(nowUtc))
                return;

            var cancel = PanelText.Find(entries, PanelText.CancelPanel);
            if (cancel >= 0)
            {
                Clicked(nowUtc, "leaving the panel");
                menu.Entries[cancel].Select();
            }

            Finish();
            return;
        }

        var wanted = next.Type == VesselType.Submarine ? PanelText.SubmarineManagement : PanelText.AirshipManagement;
        var index = PanelText.Find(entries, wanted);
        if (index < 0)
        {
            Stop($"The panel offers no {(next.Type == VesselType.Submarine ? "submersible" : "airship")} management entry.");
            return;
        }

        Status = $"Opening the {(next.Type == VesselType.Submarine ? "submarines" : "airships")}…";
        if (!CanClick(nowUtc))
            return;
        Clicked(nowUtc, $"opening \"{entries[index]}\"");
        menu.Entries[index].Select();
    }

    private void OnList(AddonMaster.SelectString menu, List<string> entries, VesselType type, DateTime nowUtc)
    {
        if (WaitingForVoyage())
            return;
        if (current != null)
        {
            switch (current.State)
            {
                case TargetState.Sent or TargetState.Skipped:
                    current = null;
                    break;

                case TargetState.Leaving:
                    current.State = TargetState.Skipped;
                    current = null;
                    break;

                // Clicked: give the report time to open, then try again a few times.
                case TargetState.Picking when current.Type == type:
                    if (nowUtc - lastClick < ClickGap * 2)
                        return;
                    if (current.Picks >= MaxPicks)
                    {
                        Skip(current, "its report did not open");
                        current = null;
                        return;
                    }

                    break;

                default:
                    Unexpected($"Back at the list with {current.Name} unfinished; the rest is left for you.", nowUtc);
                    return;
            }
        }

        var next = current ?? targets.FirstOrDefault(t => !t.Finished && t.Type == type);
        if (next == null)
        {
            Status = "Back to the first menu…";
            if (!CanClick(nowUtc))
                return;

            var nothing = PanelText.Find(entries, PanelText.NothingList);
            if (nothing < 0)
            {
                Stop("Could not find the way out of the vessel list.");
                return;
            }

            Clicked(nowUtc, "leaving the vessel list");
            menu.Entries[nothing].Select();
            return;
        }

        var index = PanelText.VesselEntry(entries, next.Slot, next.Name);
        if (index < 0)
        {
            Skip(next, "not found in the list");
            return;
        }

        Status = $"Opening {next.Name}…";
        if (!CanClick(nowUtc))
            return;

        current = next;
        next.State = TargetState.Picking;
        next.Picks++;
        Clicked(nowUtc, $"opening {next.Name} (\"{entries[index]}\")");
        menu.Entries[index].Select();
    }

    private void OnVesselMenu(AddonMaster.SelectString menu, List<string> entries, DateTime nowUtc)
    {
        var fc = Company();
        var selected = fc?.Vessels.FirstOrDefault(v => WorkshopReader.IsSelected(v.Type, v.Slot) == true);

        // A vessel this run already sent, back at its menu: walk out to the list.
        if (current == null && selected != null && targets.Any(t => t.Finished && t.Type == selected.Type && t.Slot == selected.Slot))
        {
            Status = "Back to the list…";
            ClickQuit(menu, entries, nowUtc);
            return;
        }

        if (current == null || selected == null || selected.Type != current.Type || selected.Slot != current.Slot)
        {
            Unexpected($"{selected?.Name ?? "A vessel"}'s menu is open, which the run did not open; it is left for you.", nowUtc);
            return;
        }

        var target = current;
        if (target.State == TargetState.Leaving)
        {
            Status = $"Leaving {target.Name}…";
            ClickQuit(menu, entries, nowUtc);
            return;
        }

        // Opened without a report: the workshop says it is still travelling, whatever Argus had stored.
        if (target.State == TargetState.Picking && selected.IsOut(nowUtc))
        {
            Skip(target, "still on its voyage");
            return;
        }

        // The report is finalized once the workshop data no longer has the voyage; plan only on that reading (new rank, new unlocks).
        if (selected.IsDeployed)
        {
            Status = $"Waiting for {target.Name}'s report to clear…";
            return;
        }

        if (target.State is TargetState.Picking or TargetState.Collecting)
            target.State = TargetState.AtMenu;

        if (target.State == TargetState.Deploying)
        {
            // The deploy entry was clicked; the map list or planner is on its way.
            if (nowUtc - lastClick < ClickGap * 2)
                return;
            Stop($"{target.Name}'s deploy entry did not open the planner; the rest is left for you.");
            return;
        }

        // Repair broken parts first: the game refuses the voyage with one.
        var condition = WorkshopReader.ReadCondition(selected);
        if (condition.Any(c => c == 0))
        {
            if (target.RepairTried)
            {
                Skip(target, "a part is still broken after the repair");
                return;
            }

            if (!CanClick(nowUtc))
                return;
            target.RepairTried = true;
            Clicked(nowUtc, $"repairing {target.Name}");
            if (plugin.RepairInterop.Start(plugin.Data, selected, plugin.PartsInterop.Running, RunId(target)))
                target.State = TargetState.Repairing;
            else
                Skip(target, $"not repaired: {plugin.RepairInterop.LastError ?? plugin.RepairInterop.LastResult}");
            return;
        }

        // Plan on the vessel as it is now. Forced once: another vessel with the same build would share its search signature.
        var planner = plugin.Planner;
        planner.Select(selected);
        planner.Refresh(force: !target.Planned);
        target.Planned = true;
        if (planner.Computing)
        {
            Status = $"Planning {target.Name}'s route…";
            return;
        }

        if (planner.ChosenRoute == null)
        {
            Skip(target, $"no route fits{IssueText(planner.Issues)}");
            return;
        }

        if (planner.DeployRefusal(selected, nowUtc) is { } refusal)
        {
            Skip(target, refusal);
            return;
        }

        var deploy = PanelText.FindDeploy(entries, target.Type);
        if (deploy < 0)
        {
            Stop($"Could not find the deploy entry on {target.Name}'s menu.");
            return;
        }

        Status = $"Deploying {target.Name}…";
        if (!CanClick(nowUtc))
            return;

        target.State = TargetState.Deploying;
        target.MapChosen = false;
        target.Applied = false;
        target.ReturnBefore = selected.ReturnTime;
        Clicked(nowUtc, $"choosing \"{entries[deploy]}\" for {target.Name}");
        menu.Entries[deploy].Select();
    }

    /// <summary>Deployed and the cutscene over, but the workshop data does not show the voyage yet: wait for it.</summary>
    private bool WaitingForVoyage()
    {
        if (current is not { State: TargetState.Deploying, Applied: true })
            return false;
        Status = $"Waiting for {current.Name}'s voyage to show…";
        return true;
    }

    private void ClickQuit(AddonMaster.SelectString menu, List<string> entries, DateTime nowUtc)
    {
        if (!CanClick(nowUtc))
            return;

        var quit = PanelText.Find(entries, PanelText.QuitVesselMenu);
        if (quit < 0)
        {
            Stop("Could not find the way out of the vessel's menu.");
            return;
        }

        Clicked(nowUtc, "leaving the vessel's menu");
        menu.Entries[quit].Select();
    }

    /// <summary>Leave this vessel where it is: at its menu it is walked back to the list, anywhere else just passed over.</summary>
    private void Skip(Target target, string reason)
    {
        target.Note = reason;
        Service.Log.Information("Argus: {Vessel} not resent: {Reason}", target.Name, reason);
        target.State = target == current && PartsInterop.Addon(PartsInterop.MenuAddon) != null && RepairInterop.IsVesselMenu(target.Type)
            ? TargetState.Leaving
            : TargetState.Skipped;
    }

    private void Finish()
    {
        Running = false;
        current = null;
        FinishedUtc = DateTime.UtcNow;
        var sent = targets.Count(t => t.State == TargetState.Sent);
        Status = $"Done: {sent} of {targets.Count} sent.";
        Service.Log.Information("Argus: collect and resend done, {Sent} of {Count} sent", sent, targets.Count);
    }

    private void Stop(string reason)
    {
        Running = false;
        LastError = reason;
        FinishedUtc = DateTime.UtcNow;
        Status = string.Empty;
        foreach (var t in targets.Where(t => !t.Finished))
        {
            if (t.State != TargetState.Pending && string.IsNullOrEmpty(t.Note))
                t.Note = "stopped part-way";
        }

        current = null;
        Service.Log.Warning("Argus: collect and resend stopped: {Reason}", reason);
    }

    /// <summary>Stop over a window the run did not expect, once it has stayed up past <see cref="UnexpectedGrace"/>.</summary>
    private void Unexpected(string reason, DateTime nowUtc)
    {
        oddThisStep = true;
        if (oddReason != reason)
        {
            oddReason = reason;
            oddSince = nowUtc;
        }

        if (nowUtc - oddSince >= UnexpectedGrace)
            Stop(reason);
        else
            Status = "Waiting for the game…";
    }

    private bool CanClick(DateTime nowUtc) => nowUtc - lastClick >= ClickGap;

    private void Clicked(DateTime nowUtc, string what)
    {
        lastClick = nowUtc;
        Service.Log.Information("Argus: collect and resend: {What}", what);
    }

    private FreeCompanyRecord? Company() => plugin.Fleet.Store.TryGet(fcId, out var fc) ? fc : null;

    private Vessel? Live(Target t) => Company()?.Vessels.FirstOrDefault(v => v.Type == t.Type && v.Slot == t.Slot);

    /// <summary>The repair's run tag, so the Vessels card does not show this run's outcome as its own.</summary>
    private static string RunId(Target t) => $"run:{t.Type}:{t.Slot}";

    private string RouteLetters(VesselType type, IEnumerable<uint> sectors)
        => string.Concat(sectors.Select(id => plugin.Data.Sector(type, id).Letter));

    private static string IssueText(IReadOnlyList<string> issues) => issues.Count == 0 ? string.Empty : $" ({string.Join("; ", issues)})";

    private static bool InCutscene()
        => Service.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Service.Condition[ConditionFlag.WatchingCutscene78];

    private static string OpenScreenName()
    {
        foreach (var name in new[] { ConfirmAddon, DetailAddon, ResultAddon, PlannerInterop.PlannerAddon, MapSelectInterop.AddonName, PartsInterop.MenuAddon })
        {
            if (PartsInterop.Addon(name) != null)
                return name;
        }

        return PartsInterop.PartsWindowName ?? "none";
    }
}
