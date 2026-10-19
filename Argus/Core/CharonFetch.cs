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
/// Asks Charon to take parts out of retainers (<c>Charon.Retainers.RequestFetchList</c>, contract in
/// <c>D:\Dev\Charon\Charon\Ipc\RetainerContentsIpc.cs</c>).
///
/// <para>A fetch is ARMED, not run: Charon plans which retainer holds each item, then moves the stack when the player
/// is at a summoning bell with that retainer open, one item of the list at a time. Its state comes back as a value
/// (<c>GetFetchStatusJson</c>: idle, waitingForPerson, moving, done, refused) that this reads for the UI.</para>
///
/// <para>Charon refuses every fetch from another plugin while its <c>RetainerIpcExecuteEnabled</c> setting is off
/// (the default, with no toggle in its window), and that refusal leaves no reason anywhere: the call returns false
/// before its reader runs. It is recognised by elimination, as a refusal while Charon is not busy that did not change
/// Charon's own status line.</para>
/// </summary>
internal sealed class CharonFetch
{
    public sealed record Status(string State, string Retainer, int Moved, int Queued, string Reason);

    private static readonly TimeSpan StatusCacheFor = TimeSpan.FromSeconds(1);

    private readonly ICallGateSubscriber<string, bool> requestList;
    private readonly ICallGateSubscriber<string> statusJson;
    private readonly ICallGateSubscriber<string> statusLine;
    private readonly ICallGateSubscriber<bool> busy;
    private readonly ICallGateSubscriber<bool> stop;
    private (DateTime At, Status? Status) cached;

    public CharonFetch(IDalamudPluginInterface pluginInterface)
    {
        requestList = pluginInterface.GetIpcSubscriber<string, bool>("Charon.Retainers.RequestFetchList");
        statusJson = pluginInterface.GetIpcSubscriber<string>("Charon.Retainers.GetFetchStatusJson");
        statusLine = pluginInterface.GetIpcSubscriber<string>("Charon.Retainers.FetchStatus");
        busy = pluginInterface.GetIpcSubscriber<bool>("Charon.Retainers.FetchBusy");
        stop = pluginInterface.GetIpcSubscriber<bool>("Charon.Retainers.Stop");
    }

    public string? LastError { get; private set; }

    /// <summary>Caller-supplied tag of whatever asked last, so a UI shows the outcome only where it was asked.</summary>
    public string LastRunId { get; private set; } = string.Empty;

    /// <summary>Hand Charon the items to fetch. False with <see cref="LastError"/> when it refuses or is not loaded.</summary>
    public bool Request(IReadOnlyCollection<(uint ItemId, int Quantity)> items, string runId)
    {
        LastError = null;
        LastRunId = runId;
        cached = default;
        try
        {
            var before = statusLine.InvokeFunc();
            if (requestList.InvokeFunc(ListJson(items)))
            {
                Service.Log.Information("Argus: asked Charon to fetch {Count} part(s) from retainers", items.Count);
                return true;
            }

            var after = statusLine.InvokeFunc();
            LastError = busy.InvokeFunc() ? "Charon is busy with another retainer pass; try again when it finishes."
                : after != before ? $"Charon refused: {after}"
                : "Charon refused: its fetch for other plugins is switched off (RetainerIpcExecuteEnabled in Charon's settings).";
        }
        catch (IpcNotReadyError)
        {
            LastError = "Charon is not loaded.";
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Argus: asking Charon to fetch failed");
            LastError = "Asking Charon failed; see the log.";
        }

        Service.Log.Information("Argus: Charon fetch refused: {Reason}", LastError);
        return false;
    }

    /// <summary>Charon's fetch state, polled at most once a second; null without Charon.</summary>
    public Status? Current(DateTime nowUtc)
    {
        if (nowUtc - cached.At < StatusCacheFor)
            return cached.Status;

        Status? status = null;
        try
        {
            status = ParseStatus(statusJson.InvokeFunc());
        }
        catch (IpcNotReadyError)
        {
        }
        catch (Exception ex)
        {
            Service.Log.Debug(ex, "Argus: reading Charon's fetch status failed");
        }

        cached = (nowUtc, status);
        return status;
    }

    /// <summary>Cancel what Charon has armed, including the rest of the list. Always allowed by Charon.</summary>
    public void Stop()
    {
        try
        {
            stop.InvokeFunc();
        }
        catch (Exception ex) when (ex is IpcNotReadyError or InvalidOperationException)
        {
        }

        cached = default;
    }

    /// <summary>Charon's list shape: <c>{ "items": [ { "itemId": 21794, "quantity": 1 } ] }</c>, any quality.</summary>
    internal static string ListJson(IEnumerable<(uint ItemId, int Quantity)> items)
        => JsonConvert.SerializeObject(new
        {
            items = items.Where(i => i.ItemId != 0 && i.Quantity > 0).Select(i => new { itemId = i.ItemId, quantity = i.Quantity }),
        });

    /// <summary>Charon's FetchReport wire form; tolerant, as its contract is extend-only.</summary>
    internal static Status? ParseStatus(string json)
    {
        try
        {
            var root = JObject.Parse(json);
            return new Status(
                (string?)root["state"] ?? "idle",
                (string?)root["retainer"] ?? string.Empty,
                (int?)root["moved"] ?? 0,
                (int?)root["queued"] ?? 0,
                (string?)root["reason"] ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
