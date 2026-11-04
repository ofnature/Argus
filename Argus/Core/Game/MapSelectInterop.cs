using System.Collections.Generic;
using System.Linq;
using ECommons.Automation;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Argus.Core.Game;

/// <summary>
/// The submarine map list ("Nautical Maps", <c>SubmarineExplorationMapSelect</c>) the game shows between the vessel's
/// Deploy entry and the voyage planner. Layout and call from AutoRetainer: the sub's rank at AtkValue 1, then up to
/// seven maps from AtkValue 3, three values each (map id, name, required rank); picking one is
/// <c>Callback.Fire(addon, true, 2, Zero, mapId)</c> with the SubmarineMap row id, not the row's place in the list
/// (the game lists the newest map first). Choosing a map only opens the planner: nothing is sent or spent.
/// </summary>
internal static unsafe class MapSelectInterop
{
    public const string AddonName = "SubmarineExplorationMapSelect";

    public sealed record ListedMap(uint Id, string Name, uint RequiredRank);

    public static bool IsOpen => PartsInterop.Addon(AddonName) != null;

    public static (float X, float Y, float W, float H)? Rect()
    {
        var addon = PartsInterop.Addon(AddonName);
        if (addon == null)
            return null;
        return (addon->X, addon->Y, addon->GetScaledWidth(true), addon->GetScaledHeight(true));
    }

    /// <summary>The maps the open list offers, in its order; empty when it is not open.</summary>
    public static List<ListedMap> Maps()
    {
        var addon = PartsInterop.Addon(AddonName);
        if (addon == null)
            return new List<ListedMap>();

        return new Reader(addon).Maps
            .Where(m => m.Id != 0)
            .Select(m => new ListedMap(m.Id, m.Name, m.RequiredRank))
            .ToList();
    }

    /// <summary>Open the planner on <paramref name="mapId"/>. False, with the reason, when the list is not up or does not offer it.</summary>
    public static bool Select(uint mapId, out string? error)
    {
        error = null;
        var addon = PartsInterop.Addon(AddonName);
        if (addon == null)
        {
            error = "The map list is not open.";
            return false;
        }

        // Only a map the list offers: the game decides which are unlocked, and an id it does not list is not ours to send.
        var listed = new Reader(addon).Maps.FirstOrDefault(m => m.Id == mapId);
        if (listed == null)
        {
            error = "That map is not in the list (not unlocked for this FC?).";
            return false;
        }

        Service.Log.Information("Argus: choosing map {Map} ({Name})", mapId, listed.Name);
        Callback.Fire(addon, true, 2, Callback.ZeroAtkValue, mapId);
        return true;
    }

    private sealed class Reader(AtkUnitBase* unitBase) : AtkReader(unitBase)
    {
        public List<Entry> Maps => Loop<Entry>(3, 3, 7);
    }

    private sealed class Entry(nint unitBasePtr, int offset = 0) : AtkReader(unitBasePtr, offset)
    {
        public uint Id => ReadUInt(0) ?? 0;

        public string Name => ReadString(1);

        public uint RequiredRank => ReadUInt(2) ?? uint.MaxValue;
    }
}
