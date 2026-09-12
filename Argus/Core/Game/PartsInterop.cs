using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Data;
using Argus.Core.Model;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Argus.Core.Game;

/// <summary>
/// Installs a build through the game's own parts windows, the way AutoRetainer's part swapper does.
///
/// <para>The chain starts at the vessel's menu on the Voyage Control Panel: pick the change-components entry, which
/// opens <c>CompanyCraftSupply</c>; each slot opens a <c>ContextIconMenu</c> of the parts you own; picking one
/// installs it. Only slots that differ from the target are touched, and the run aborts rather than guessing.</para>
///
/// <para>This spends crafted parts, so nothing here starts on its own: it runs only from the Builder's apply button.
/// The menu entry is matched strictly, never by position, because the same menu also offers decommissioning.</para>
/// </summary>
internal sealed unsafe class PartsInterop
{
    private const string MenuAddon = "SelectString";
    private const string PartsAddon = "CompanyCraftSupply";
    private const string PickerAddon = "ContextIconMenu";

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

        /// <summary>Close the parts window once every slot is done.</summary>
        Close,
    }

    private readonly Queue<(int Slot, uint ItemId, string ItemName)> pending = new();
    private Stage stage;
    private DateTime stageSince;
    private DateTime lastStep = DateTime.MinValue;
    private int installed;

    public string? LastError { get; private set; }

    public string? LastResult { get; private set; }

    public bool Running => stage != Stage.None;

    private static AtkUnitBase* Addon(string name)
    {
        var ptr = Service.GameGui.GetAddonByName(name).Address;
        if (ptr == nint.Zero)
            return null;
        var addon = (AtkUnitBase*)ptr;
        return addon->IsVisible && addon->IsReady ? addon : null;
    }

    public bool IsMenuOpen => Addon(MenuAddon) != null;

    public bool IsPartsWindowOpen => Addon(PartsAddon) != null;

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
    public bool ApplyBuild(Vessel vessel, Build target)
    {
        LastError = null;
        LastResult = null;
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

        if (!IsPartsWindowOpen && !(IsMenuOpen && FindChangeEntry() >= 0))
        {
            LastError = "Open the vessel on the Voyage Control Panel first.";
            return false;
        }

        foreach (var (slot, _, item) in changes)
            pending.Enqueue((slot, item, Sheets.ItemName(item)));

        installed = 0;
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
                var supply = Addon(PartsAddon);
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

                var slot = pending.Peek().Slot;
                Callback.Fire(supply, true, 2, 1, slot, Callback.ZeroAtkValue, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
                Advance(Stage.PickPart, nowUtc);
                return;
            }

            case Stage.PickPart:
            {
                var pickerPtr = Service.GameGui.GetAddonByName(PickerAddon).Address;
                if (pickerPtr == nint.Zero)
                    return;

                var picker = (AddonContextIconMenu*)pickerPtr;
                if (!picker->AtkUnitBase.IsVisible || !picker->AtkUnitBase.IsReady)
                    return;

                var count = picker->AtkValues[4];
                if (count.Type != ValueType.UInt)
                    return;

                var wanted = pending.Peek();
                for (var i = 0; i < count.UInt; i++)
                {
                    var value = picker->AtkValues[13 + (8 * i)];
                    if (value.Type is not (ValueType.String or ValueType.ManagedString))
                        continue;
                    if (!string.Equals(value.GetValueAsString(), wanted.ItemName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Callback.Fire(&picker->AtkUnitBase, true, Callback.ZeroAtkValue, i, wanted.ItemId, Callback.ZeroAtkValue, Callback.ZeroAtkValue);
                    Service.Log.Information("Argus: installing {Item} in slot {Slot}", wanted.ItemName, wanted.Slot);
                    pending.Dequeue();
                    installed++;
                    Advance(pending.Count == 0 ? Stage.Close : Stage.OpenSlot, nowUtc);
                    return;
                }

                LastError = $"{wanted.ItemName} was not offered for that slot.";
                Cancel();
                return;
            }

            case Stage.Close:
            {
                var supply = Addon(PartsAddon);
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
    }
}
