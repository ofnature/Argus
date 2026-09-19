using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Data;
using Argus.Core.Model;
using ECommons.Automation;
using ECommons.UIHelpers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Argus.Core.Game;

/// <summary>
/// Installs a build through the game's own parts windows, the way AutoRetainer's part swapper does.
///
/// <para>The chain starts at the vessel's menu on the Voyage Control Panel: pick the change-components entry, which
/// opens the component window; each slot opens a <c>ContextIconMenu</c> of the parts you own; picking one
/// installs it. Only slots that differ from the target are touched, and the run aborts rather than guessing.</para>
///
/// <para>This spends crafted parts, so nothing here starts on its own: it runs only from the Builder's apply button.
/// The menu entry is matched strictly, never by position, because the same menu also offers decommissioning.</para>
/// </summary>
internal sealed unsafe class PartsInterop
{
    private const string MenuAddon = "SelectString";
    private const string PickerAddon = "ContextIconMenu";

    /// <summary>
    /// The component window is not one addon for both vessel types: submarines drive the workshop's supply window
    /// (verified in game), airships open <c>AirShipPartsMenu</c> with the inventory in component-select mode beside
    /// it. Whichever of these is on screen is the one that gets the callbacks.
    /// </summary>
    private static readonly string[] PartsAddons = { "CompanyCraftSupply", "AirShipPartsMenu", "SubmersiblePartsMenu" };

    /// <summary>
    /// The change-components entry across the client languages, from AutoRetainer's list. Most languages phrase it
    /// generically ("change parts") for both vessel types; English names the vessel, so both spellings are here.
    /// </summary>
    private static readonly string[] ChangeComponents =
    {
        "change submersible components", "change airship components", "change components",
        "パーツの変更", "bauteile austauschen", "changer les éléments", "부품 변경", "更换配件", "更換配件",
    };

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    /// <summary>How long to leave a slot request before asking again.</summary>
    private static readonly TimeSpan SlotRetry = TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private enum Stage
    {
        None,

        /// <summary>Pick the change-components entry on the vessel's menu.</summary>
        SelectMenu,

        /// <summary>Wait for the parts window the game opens in response.</summary>
        WaitWindow,

        /// <summary>Ask the parts window for the list of parts that fit the current slot.</summary>
        OpenSlot,

        /// <summary>Pick the wanted part out of that list.</summary>
        PickPart,

        /// <summary>Let the list close before asking for the next slot.</summary>
        AfterPick,

        /// <summary>Close the parts window once every slot is done.</summary>
        Close,
    }

    private readonly Queue<(int Slot, uint ItemId, string ItemName)> pending = new();
    private Stage stage;
    private DateTime stageSince;
    private DateTime lastStep = DateTime.MinValue;
    private DateTime lastSlotFire = DateTime.MinValue;
    private int installed;

    public string? LastError { get; private set; }

    public string? LastResult { get; private set; }

    /// <summary>Caller-supplied tag of whatever started the last run, so a UI can show the outcome only where it was asked for.</summary>
    public string LastRunId { get; private set; } = string.Empty;

    public bool Running => stage != Stage.None;

    /// <summary>Where a run has got to, for the Debug page.</summary>
    public string StageName => stage.ToString();

    public int Remaining => pending.Count;

    public int Installed => installed;

    /// <summary>Why <see cref="CanStart"/> is false, or null when a run could begin.</summary>
    public string? Blocker
    {
        get
        {
            if (Running)
                return "already running";
            if (IsPartsWindowOpen)
                return null;
            if (!IsMenuOpen)
                return "open the vessel on the Voyage Control Panel and choose to change its components";
            return FindChangeEntry() >= 0 ? null : "that menu has no change-components entry";
        }
    }

    /// <summary>The entries Argus can see on the open menu, for the Debug page.</summary>
    public List<string> MenuEntries()
    {
        var addon = Addon(MenuAddon);
        if (addon == null)
            return [];
        return new AddonMaster.SelectString(addon).Entries.Select(e => e.Text).ToList();
    }

    private static AtkUnitBase* Addon(string name)
    {
        var ptr = Service.GameGui.GetAddonByName(name).Address;
        if (ptr == nint.Zero)
            return null;
        var addon = (AtkUnitBase*)ptr;
        return addon->IsVisible && addon->IsReady ? addon : null;
    }

    /// <summary>The component window that is open, or null when none of them is.</summary>
    private static AtkUnitBase* PartsWindow()
    {
        foreach (var name in PartsAddons)
        {
            var addon = Addon(name);
            if (addon != null)
                return addon;
        }

        return null;
    }

    /// <summary>Which component window is open, for the Debug page and the log.</summary>
    public static string? PartsWindowName
    {
        get
        {
            foreach (var name in PartsAddons)
            {
                if (Addon(name) != null)
                    return name;
            }

            return null;
        }
    }

    private static AddonContextIconMenu* Picker()
    {
        var ptr = Service.GameGui.GetAddonByName(PickerAddon).Address;
        if (ptr == nint.Zero)
            return null;
        var picker = (AddonContextIconMenu*)ptr;
        return picker->AtkUnitBase.IsVisible && picker->AtkUnitBase.IsReady ? picker : null;
    }

    public bool IsPickerOpen => Picker() != null;

    /// <summary>
    /// The part names the open list is offering, in the game's order. Entry text is an SeString and can carry item
    /// link payloads, so it is extracted rather than read raw.
    /// </summary>
    public List<string> PickerEntries()
    {
        var entries = new List<string>();
        var picker = Picker();
        if (picker == null)
            return entries;

        var reader = new PickerReader(&picker->AtkUnitBase);
        var count = reader.Count;
        for (var i = 0; i < count; i++)
            entries.Add(reader.Entry(i));

        return entries;
    }

    /// <summary>AtkValue layout of the part list: the count at 4, then a name every eight values from 13.</summary>
    private sealed class PickerReader(AtkUnitBase* unitBase, int beginOffset = 0) : AtkReader(unitBase, beginOffset)
    {
        public uint Count => ReadUInt(4) ?? 0;

        public string Entry(int index) => (ReadSeString(13 + (8 * index))?.TextValue ?? string.Empty).Trim();
    }

    /// <summary>Letters and digits only: the list can add quality marks and spacing the item name does not have.</summary>
    private static string Normalise(string text)
        => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int IndexOfPart(List<string> entries, string itemName)
    {
        var wanted = Normalise(itemName);
        if (wanted.Length == 0)
            return -1;

        for (var i = 0; i < entries.Count; i++)
        {
            if (Normalise(entries[i]) == wanted)
                return i;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (Normalise(entries[i]).Contains(wanted))
                return i;
        }

        return -1;
    }

    /// <summary>DEBUG helper: ask the parts window for one slot's list, to check the callback in isolation.</summary>
    public bool RequestSlot(int slot)
    {
        var supply = PartsWindow();
        if (supply == null)
            return false;
        Callback.Fire(supply, true, 2, 1, slot, Callback.ZeroAtkValue, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
        return true;
    }

    public bool IsMenuOpen => Addon(MenuAddon) != null;

    public bool IsPartsWindowOpen => PartsWindow() != null;

    /// <summary>Can a build be applied from what is on screen right now.</summary>
    public bool CanStart => !Running && (IsPartsWindowOpen || (IsMenuOpen && FindChangeEntry() >= 0));

    /// <summary>Slots where the vessel differs from the target, as (slot, part row, item).</summary>
    public static List<(int Slot, uint PartRow, uint ItemId)> Differences(Vessel vessel, Build target)
    {
        var current = new[] { vessel.Hull, vessel.Stern, vessel.Bow, vessel.Bridge };
        var wanted = target.Parts.ToArray();
        var changes = new List<(int, uint, uint)>();
        for (var slot = 0; slot < 4; slot++)
        {
            var row = wanted[slot].Id;
            if (current[slot] == row)
                continue;
            changes.Add((slot, row, PartItems.ItemFor(vessel.Type, row)));
        }

        return changes;
    }

    /// <summary>How many of a part the player is carrying; installing takes it from the inventory.</summary>
    public static int Carried(uint itemId)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : manager->GetInventoryItemCount(itemId, false, false);
    }

    /// <summary>
    /// Queue the slots that differ. Returns false with <see cref="LastError"/> when the screen is not in the right
    /// place, a part has no known item, or one is not in the inventory.
    /// </summary>
    public bool ApplyBuild(Vessel vessel, Build target, string runId = "")
    {
        LastError = null;
        LastResult = null;
        LastRunId = runId;
        Cancel();

        var changes = Differences(vessel, target);
        if (changes.Count == 0)
        {
            LastResult = "Already built that way.";
            return false;
        }

        foreach (var (slot, row, item) in changes)
        {
            if (item == 0)
            {
                LastError = $"No known item for the {Build.SlotName(vessel.Type, slot).ToLowerInvariant()} part.";
                return false;
            }

            if (Carried(item) <= 0)
            {
                LastError = $"{Sheets.ItemName(item)} is not in your inventory.";
                return false;
            }
        }

        if (Blocker is { } blocker)
        {
            LastError = $"Cannot start: {blocker}.";
            return false;
        }

        foreach (var (slot, _, item) in changes)
            pending.Enqueue((slot, item, Sheets.ItemName(item)));

        installed = 0;
        Service.Log.Information("Argus: applying a build through {Window}", PartsWindowName ?? "the vessel menu");
        stage = IsPartsWindowOpen ? Stage.OpenSlot : Stage.SelectMenu;
        stageSince = DateTime.UtcNow;
        return true;
    }

    /// <summary>Index of the change-components entry, or -1 when it is absent or ambiguous.</summary>
    private int FindChangeEntry()
    {
        var addon = Addon(MenuAddon);
        if (addon == null)
            return -1;

        var menu = new AddonMaster.SelectString(addon);
        var matches = new List<int>();
        foreach (var entry in menu.Entries)
        {
            var text = entry.Text.Trim().ToLowerInvariant();
            if (ChangeComponents.Any(c => text == c || text.Contains(c)))
                matches.Add(entry.Index);
        }

        // Never fall back to a position: this menu also offers decommissioning the vessel.
        return matches.Count == 1 ? matches[0] : -1;
    }

    public void Update(DateTime nowUtc)
    {
        if (stage == Stage.None)
            return;

        if (nowUtc - stageSince > Timeout)
        {
            LastError = $"Timed out after {installed} of {installed + pending.Count} parts; finish it in the game window.";
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
            Service.Log.Error(ex, "Argus: applying a build failed");
            LastError = "Applying the build failed; see the log.";
            Cancel();
        }
    }

    private void Step(DateTime nowUtc)
    {
        switch (stage)
        {
            case Stage.SelectMenu:
            {
                if (IsPartsWindowOpen)
                {
                    Advance(Stage.OpenSlot, nowUtc);
                    return;
                }

                var addon = Addon(MenuAddon);
                if (addon == null)
                    return;

                var index = FindChangeEntry();
                if (index < 0)
                {
                    LastError = "Could not find the change-components entry on that menu.";
                    Cancel();
                    return;
                }

                var menu = new AddonMaster.SelectString(addon);
                Service.Log.Information("Argus: selecting \"{Entry}\"", menu.Entries[index].Text);
                menu.Entries[index].Select();
                Advance(Stage.WaitWindow, nowUtc);
                return;
            }

            case Stage.WaitWindow:
                if (IsPartsWindowOpen)
                    Advance(Stage.OpenSlot, nowUtc);
                return;

            case Stage.OpenSlot:
            {
                var supply = PartsWindow();
                if (supply == null)
                {
                    LastError = "The parts window closed before the build was applied.";
                    Cancel();
                    return;
                }

                if (pending.Count == 0)
                {
                    Advance(Stage.Close, nowUtc);
                    return;
                }

                if (IsPickerOpen)
                {
                    Advance(Stage.PickPart, nowUtc);
                    return;
                }

                // Ask again rather than assuming one request took: the window drops it while it is settling.
                if (nowUtc - lastSlotFire < SlotRetry)
                    return;

                lastSlotFire = nowUtc;
                var slot = pending.Peek().Slot;
                Callback.Fire(supply, true, 2, 1, slot, Callback.ZeroAtkValue, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
                return;
            }

            case Stage.PickPart:
            {
                var picker = Picker();
                if (picker == null)
                {
                    // It closed without us choosing; ask for the slot again.
                    Advance(Stage.OpenSlot, nowUtc);
                    lastSlotFire = DateTime.MinValue;
                    return;
                }

                var entries = PickerEntries();
                if (entries.Count == 0)
                    return;

                var wanted = pending.Peek();
                var index = IndexOfPart(entries, wanted.ItemName);
                if (index < 0)
                {
                    var offered = string.Join(", ", entries.Where(e => e.Length > 0));
                    LastError = $"Slot {wanted.Slot} does not list {wanted.ItemName}. It offered: {offered}";
                    Service.Log.Warning("Argus: {Error}", LastError);
                    Cancel();
                    return;
                }

                Callback.Fire(&picker->AtkUnitBase, true, Callback.ZeroAtkValue, index, wanted.ItemId, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
                Service.Log.Information("Argus: installing {Item} in slot {Slot} (entry {Index})", wanted.ItemName, wanted.Slot, index);
                pending.Dequeue();
                installed++;
                Advance(Stage.AfterPick, nowUtc);
                return;
            }

            case Stage.AfterPick:
                // Wait for the list to close, or the next slot request lands on the one that is still up.
                if (IsPickerOpen)
                    return;
                lastSlotFire = DateTime.MinValue;
                Advance(pending.Count == 0 ? Stage.Close : Stage.OpenSlot, nowUtc);
                return;

            case Stage.Close:
            {
                var supply = PartsWindow();
                if (supply == null)
                {
                    Finish();
                    return;
                }

                Callback.Fire(supply, true, 5);
                Finish();
                return;
            }
        }
    }

    private void Advance(Stage next, DateTime nowUtc)
    {
        stage = next;
        stageSince = nowUtc;
    }

    private void Finish()
    {
        LastResult = $"Installed {installed} part{(installed == 1 ? string.Empty : "s")}.";
        Service.Log.Information("Argus: {Result}", LastResult);
        Cancel();
    }

    public void Cancel()
    {
        pending.Clear();
        stage = Stage.None;
        lastSlotFire = DateTime.MinValue;
    }
}
