using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Argus.Core;

/// <summary>
/// Where an item is outside the player's bags, asked of Charon (<c>Charon.Containers.GetItemJson</c>: every retainer
/// and FC chest page Charon has read, each with when; contract in <c>D:\Dev\Charon\Charon\Ipc\ChestContentsIpc.cs</c>).
///
/// <para>Answers are snapshots, not live: the game only fills a retainer's bags when its window is opened at a bell,
/// so a retainer Charon has not read is unknown, never empty. They are cached for a few seconds because the Builder
/// asks for every part on every card on every frame. Null when Charon is not loaded.</para>
/// </summary>
internal sealed class PartLocator
{
    /// <summary>A retainer, or an FC chest page (<paramref name="IsRetainer"/> false): Charon fetches only from retainers.</summary>
    public sealed record Place(string Name, int Count, TimeSpan? Age, bool IsRetainer = true);

    public sealed record Found(IReadOnlyList<Place> Places, int RetainersKnown);

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    private readonly ICallGateSubscriber<uint, string> getItem;
    private readonly Dictionary<uint, (DateTime At, Found? Found)> cache = new();

    public PartLocator(IDalamudPluginInterface pluginInterface)
    {
        getItem = pluginInterface.GetIpcSubscriber<uint, string>("Charon.Containers.GetItemJson");
    }

    public Found? Find(uint itemId, DateTime nowUtc)
    {
        if (cache.TryGetValue(itemId, out var hit) && nowUtc - hit.At < CacheFor)
            return hit.Found;

        Found? found = null;
        try
        {
            found = Parse(getItem.InvokeFunc(itemId), nowUtc);
        }
        catch (IpcNotReadyError)
        {
            // Charon is not loaded: the Builder shows only what the bags hold, as before.
        }
        catch (Exception ex)
        {
            Service.Log.Debug(ex, "Argus: asking Charon where item {Item} is failed", itemId);
        }

        cache[itemId] = (nowUtc, found);
        return found;
    }

    /// <summary>
    /// Charon's answer as places holding the item. Tolerant: Charon's contract is extend-only, and a failed lookup
    /// comes back as a payload without the arrays, which reads as nothing found.
    /// </summary>
    internal static Found? Parse(string json, DateTime nowUtc)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        var places = new List<Place>();
        foreach (var r in root["retainers"] as JArray ?? new JArray())
        {
            var count = ((int?)r["nq"] ?? 0) + ((int?)r["hq"] ?? 0);
            if (count <= 0)
                continue;
            var age = (double?)r["ageMinutes"] is { } minutes ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
            places.Add(new Place((string?)r["retainer"] ?? "a retainer", count, age));
        }

        var chest = root["chest"] as JObject;
        TimeSpan? chestAge = null;
        if (chest?["capturedUtc"] is { Type: JTokenType.String or JTokenType.Date } captured
            && DateTime.TryParse(captured.ToString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var when))
            chestAge = nowUtc - when;

        foreach (var p in chest?["pages"] as JArray ?? new JArray())
        {
            var count = ((int?)p["nq"] ?? 0) + ((int?)p["hq"] ?? 0);
            if (count > 0)
                places.Add(new Place($"FC chest page {(int?)p["page"] ?? 0}", count, chestAge, IsRetainer: false));
        }

        return new Found(places, (int?)root["retainerKnown"] ?? 0);
    }

    /// <summary>"on T'sola (1, 1 d ago), FC chest page 3 (2)", "not on the 2 retainers Charon has read", or null without Charon.</summary>
    public static string? Describe(Found? found, Func<TimeSpan, string> duration)
    {
        if (found == null)
            return null;

        if (found.Places.Count == 0)
        {
            return found.RetainersKnown > 0
                ? $"not on the {found.RetainersKnown} retainer{(found.RetainersKnown == 1 ? string.Empty : "s")} Charon has read"
                : "Charon has not read any retainer yet";
        }

        return "on " + string.Join(", ", found.Places.Select(p =>
            p.Age is { } age ? $"{p.Name} ({p.Count}, {duration(age)} ago)" : $"{p.Name} ({p.Count})"));
    }
}
