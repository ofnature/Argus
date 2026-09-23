using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Calc;
using Argus.Core.Data;
using Argus.Core.Model;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Argus.Core.Game;

/// <summary>
/// Repairs a vessel's broken parts through the game's own windows, the way AutoRetainer's TaskRepairAll does.
///
/// <para>From the vessel's menu on the Voyage Control Panel: pick the repair-components entry, which opens the same
/// component window the parts swap uses (<c>CompanyCraftSupply</c> for submarines, <c>AirShipPartsMenu</c> for
/// airships); ask it to repair a slot; the game asks to confirm spending Magitek Repair Materials; confirm; wait for the
/// part's condition to come back; next slot; close.</para>
///
/// <para>This spends repair materials, so it only starts from a button the player pressed, only touches parts at 0%,
/// checks the kits first, makes sure the menu belongs to the vessel it was asked for, and clicks Yes only on a prompt
/// that is a repair confirmation naming the part it asked to repair. Anything else stops the run and leaves the prompt
/// to the player.</para>
/// </summary>
internal sealed unsafe class RepairInterop
{
    private const string ConfirmAddon = "SelectYesno";

    /// <summary>The repair-components entry per vessel type across the client languages (AutoRetainer's list).</summary>
    private static readonly string[] SubmarineEntry = { "repair submersible components", "パーツの修理", "bauteile reparieren", "réparer des éléments", "修理配件", "부품 수리" };

    private static readonly string[] AirshipEntry = { "repair airship components", "パーツの修理", "bauteile reparieren", "réparer des éléments", "修理配件", "부품 수리" };

    /// <summary>
    /// What the game's confirmation (Addon 6587, "Use your last … to repair your vessel's …?") carries in each language,
    /// from AutoRetainer's list. The part's own name has to be in the prompt as well.
    /// </summary>
    private static readonly string[] ConfirmPhrases =
    {
        "repair", "下記のアイテムを修理しますか", "reparieren", "réparer", "要修理下列部件吗", "要修理下列部件嗎",
        "要修理下列元件嗎", "수리하시겠습니까?", "要修理下列組件嗎",
    };

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    /// <summary>How long to leave a repair request before asking again; the window drops requests while settling.</summary>
    private static readonly TimeSpan RequestRetry = TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private enum Stage
    {
        None,

        /// <summary>Pick the repair entry on the vessel's menu.</summary>
        SelectMenu,

        /// <summary>Wait for the component window the game opens in response.</summary>
        WaitWindow,

        /// <summary>Ask the window to repair the next broken slot.</summary>
        RequestRepair,

        /// <summary>Check the confirmation is the one asked for, then accept it.</summary>
        Confirm,

        /// <summary>Wait for the part's condition to come back before the next slot.</summary>
        WaitRepaired,

        /// <summary>Close the component window once every slot is done.</summary>
        Close,
    }

    private readonly Queue<(int Slot, uint ItemId, string ItemName)> pending = new();
    private Vessel? target;
    private Stage stage;
    private DateTime stageSince;
    private DateTime lastStep = DateTime.MinValue;
    private DateTime lastRequest = DateTime.MinValue;
    private int repaired;

    public string? LastError { get; private set; }

    public string? LastResult { get; private set; }

    /// <summary>Caller-supplied tag of whatever started the last run, so a UI shows the outcome only where it was asked.</summary>
    public string LastRunId { get; private set; } = string.Empty;

    public bool Running => stage != Stage.None;

    public string StageName => stage.ToString();

    public int Remaining => pending.Count;

    public int Repaired => repaired;

    /// <summary>Why a repair of <paramref name="vessel"/> could not start from what is on screen, or null when it could.</summary>
    public string? Blocker(Vessel vessel, bool partsBusy)
    {
        if (Running)
            return "already running";
        if (partsBusy)
            return "a parts install is running";
        if (PartsInterop.PartsWindow() != null)
            return "close the components window first";
        if (PartsInterop.Addon(PartsInterop.MenuAddon) == null)
            return $"select {vessel.Name} on the Voyage Control Panel";

        // The menu does not say whose it is, so ask the workshop which vessel it has selected.
        switch (WorkshopReader.IsSelected(vessel.Type, vessel.Slot))
        {
            case null:
                return $"select {vessel.Name} on the Voyage Control Panel";
            case false:
                return "that menu is for another vessel";
        }

        return PartsInterop.FindMenuEntry(EntryFor(vessel.Type)) >= 0 ? null : "that menu has no repair entry";
    }

    private static string[] EntryFor(VesselType type) => type == VesselType.Airship ? AirshipEntry : SubmarineEntry;

    /// <summary>Magitek Repair Materials a repair of these slots takes: each part has a fixed cost, whatever its condition.</summary>
    public static int Cost(GameData data, Vessel vessel, IEnumerable<int> slots)
        => slots.Sum(slot => data.Parts(vessel.Type).TryGetValue(vessel.PartRow(slot), out var part) ? part.RepairMaterials : 0);

    /// <summary>
    /// Queue a repair of every part that is broken right now. Returns false with <see cref="LastError"/> or
    /// <see cref="LastResult"/> set when nothing needs it, the kits are short, or the screen is not in the right place.
    /// </summary>
    public bool Start(GameData data, Vessel vessel, bool partsBusy, string runId = "")
    {
        LastError = null;
        LastResult = null;
        LastRunId = runId;
        Cancel();

        // The live reading, not the cached card: only a part the game says is broken now gets repaired.
        var live = WorkshopReader.ReadCondition(vessel);
        var broken = Enumerable.Range(0, live.Length).Where(i => live[i] == 0).ToList();
        if (broken.Count == 0)
        {
            if (live.Any(c => c < 0))
                LastError = "Could not read the part condition; select the vessel on the Voyage Control Panel.";
            else
                LastResult = "No broken parts.";
            return false;
        }

        var cost = Cost(data, vessel, broken);
        var kits = PartsInterop.Carried(Supplies.MagitekRepairMaterialsItem);
        if (kits < cost)
        {
            LastError = $"Needs {cost} Magitek Repair Materials; carrying {kits}.";
            return false;
        }

        if (Blocker(vessel, partsBusy) is { } blocker)
        {
            LastError = $"Cannot start: {blocker}.";
            return false;
        }

        foreach (var slot in broken)
        {
            var item = PartItems.ItemFor(vessel.Type, vessel.PartRow(slot));
            if (item == 0)
            {
                LastError = $"No known item for the {Build.SlotName(vessel.Type, slot).ToLowerInvariant()} part.";
                pending.Clear();
                return false;
            }

            pending.Enqueue((slot, item, Sheets.ItemName(item)));
        }

        target = vessel;
        repaired = 0;
        Service.Log.Information("Argus: repairing {Count} broken parts on {Vessel} ({Cost} kits)", pending.Count, vessel.Name, cost);
        Advance(Stage.SelectMenu, DateTime.UtcNow);
        return true;
    }

    public void Update(DateTime nowUtc)
    {
        if (stage == Stage.None)
            return;

        if (nowUtc - stageSince > Timeout)
        {
            LastError = $"Timed out after {repaired} of {repaired + pending.Count} repairs ({stage}); finish it in the game window.";
            Service.Log.Warning("Argus: {Error}", LastError);
            Cancel();
            return;
        }

        if (nowUtc - lastStep < Interval)
            return;
        lastStep = nowUtc;

        try
        {
            Step(nowUtc);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Argus: repairing failed");
            LastError = "Repairing failed; see the log.";
            Cancel();
        }
    }

    private void Step(DateTime nowUtc)
    {
        var vessel = target!;
        switch (stage)
        {
            case Stage.SelectMenu:
            {
                if (PartsInterop.PartsWindow() != null)
                {
                    Advance(Stage.RequestRepair, nowUtc);
                    return;
                }

                var addon = PartsInterop.Addon(PartsInterop.MenuAddon);
                if (addon == null)
                    return;

                // Checked again at the moment of the click, not only when the button was pressed.
                if (WorkshopReader.IsSelected(vessel.Type, vessel.Slot) != true)
                {
                    Fail($"The open menu is not {vessel.Name}'s.");
                    return;
                }

                var index = PartsInterop.FindMenuEntry(EntryFor(vessel.Type));
                if (index < 0)
                {
                    Fail("Could not find the repair entry on that menu.");
                    return;
                }

                var menu = new AddonMaster.SelectString(addon);
                Service.Log.Information("Argus: selecting \"{Entry}\"", menu.Entries[index].Text);
                menu.Entries[index].Select();
                Advance(Stage.WaitWindow, nowUtc);
                return;
            }

            case Stage.WaitWindow:
                if (PartsInterop.PartsWindow() != null)
                    Advance(Stage.RequestRepair, nowUtc);
                return;

            case Stage.RequestRepair:
            {
                var window = PartsInterop.PartsWindow();
                if (window == null)
                {
                    Fail("The components window closed before the repair finished.");
                    return;
                }

                if (pending.Count == 0)
                {
                    Advance(Stage.Close, nowUtc);
                    return;
                }

                if (PartsInterop.Addon(ConfirmAddon) != null)
                {
                    Advance(Stage.Confirm, nowUtc);
                    return;
                }

                if (nowUtc - lastRequest < RequestRetry)
                    return;

                var slot = pending.Peek().Slot;
                if (WorkshopReader.ReadCondition(vessel)[slot] != 0)
                {
                    // Not broken any more (or no longer readable): never spend kits on it.
                    pending.Dequeue();
                    return;
                }

                lastRequest = nowUtc;
                Callback.Fire(window, true, 3, Callback.ZeroAtkValue, slot, Callback.ZeroAtkValue, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
                return;
            }

            case Stage.Confirm:
            {
                var addon = PartsInterop.Addon(ConfirmAddon);
                if (addon == null)
                {
                    // Gone without us answering; ask for the slot again.
                    lastRequest = DateTime.MinValue;
                    Advance(Stage.RequestRepair, nowUtc);
                    return;
                }

                var prompt = new AddonMaster.SelectYesno(addon) { RespectDisabledButtons = true };
                var text = prompt.Text;
                var wanted = pending.Peek();
                if (!IsRepairPrompt(text, wanted.ItemName))
                {
                    Fail($"Stopped at a prompt that is not the repair of {wanted.ItemName}; it is left for you: \"{text.Trim()}\"");
                    return;
                }

                var yes = prompt.Addon->YesButton;
                if (yes == null || !yes->IsEnabled)
                {
                    Fail($"The game will not allow repairing {wanted.ItemName} right now; the prompt is left for you.");
                    return;
                }

                Service.Log.Information("Argus: confirming the repair of {Item} (slot {Slot})", wanted.ItemName, wanted.Slot);
                prompt.Yes();
                Advance(Stage.WaitRepaired, nowUtc);
                return;
            }

            case Stage.WaitRepaired:
            {
                if (PartsInterop.Addon(ConfirmAddon) != null)
                    return;

                var slot = pending.Peek().Slot;
                if (WorkshopReader.ReadCondition(vessel)[slot] <= 0)
                    return;

                pending.Dequeue();
                repaired++;
                lastRequest = DateTime.MinValue;
                Advance(Stage.RequestRepair, nowUtc);
                return;
            }

            case Stage.Close:
            {
                var window = PartsInterop.PartsWindow();
                if (window != null)
                    Callback.Fire(window, true, 5);
                LastResult = $"Repaired {repaired} part{(repaired == 1 ? string.Empty : "s")}.";
                Service.Log.Information("Argus: {Result}", LastResult);
                Cancel();
                return;
            }
        }
    }

    /// <summary>A repair confirmation, in any client language, that names the part being repaired.</summary>
    internal static bool IsRepairPrompt(string text, string itemName)
    {
        var lower = text.ToLowerInvariant();
        if (!ConfirmPhrases.Any(p => lower.Contains(p)))
            return false;

        var wanted = PartsInterop.Normalise(itemName);
        return wanted.Length > 0 && PartsInterop.Normalise(text).Contains(wanted);
    }

    private void Fail(string error)
    {
        LastError = error;
        Service.Log.Warning("Argus: {Error}", error);
        Cancel();
    }

    private void Advance(Stage next, DateTime nowUtc)
    {
        stage = next;
        stageSince = nowUtc;
    }

    public void Cancel()
    {
        pending.Clear();
        stage = Stage.None;
        lastRequest = DateTime.MinValue;
    }
}
