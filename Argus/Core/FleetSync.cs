using System;
using System.Collections.Generic;
using System.Linq;
using Argus.Core.Model;
using Argus.Core.Store;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace Argus.Core;

/// <summary>
/// Shares the fleet with other game clients over Daedalus's LAN relay (<c>Daedalus.Relay.Publish</c> /
/// <c>Daedalus.Relay.Message</c>, contract in <c>D:\Dev\Olympus\.cursor\rules\charon-lan-integration.md</c>), so another
/// PC can check vessels it has not seen in a workshop. Both ways: every client with sharing on broadcasts what it knows
/// and files what it hears, and the newer reading of a vessel wins.
///
/// <para>Relay semantics: UDP broadcast, so nothing is retried or kept for a client that starts later, hence the
/// periodic rebroadcast. The publisher never hears its own frames. Publishing is a silent no-op while Daedalus is
/// loaded with its LAN off, and throws while Daedalus is absent. Messages arrive on the framework thread.</para>
/// </summary>
internal sealed class FleetSync : IDisposable
{
    public const string Channel = "argus.fleet";

    /// <summary>How often everything is sent again, for a client that started after the last change.</summary>
    private static readonly TimeSpan BroadcastEvery = TimeSpan.FromMinutes(2);

    private readonly FleetService fleet;
    private readonly Configuration config;
    private readonly ICallGateSubscriber<string, string, object?> publish;
    private readonly ICallGateSubscriber<string, string, object?> message;
    private readonly Queue<(ulong Fc, VesselType Type, int Slot)> outbox = new();
    private readonly HashSet<(ulong Fc, VesselType Type, int Slot)> queued = new();
    private DateTime lastBroadcast = DateTime.MinValue;

    public DateTime LastSentUtc { get; private set; }

    public DateTime LastReceivedUtc { get; private set; }

    public int Received { get; private set; }

    /// <summary>Daedalus's relay is not registered, so nothing can be shared.</summary>
    public bool DaedalusMissing { get; private set; }

    public FleetSync(IDalamudPluginInterface pluginInterface, FleetService fleet, Configuration config)
    {
        this.fleet = fleet;
        this.config = config;
        publish = pluginInterface.GetIpcSubscriber<string, string, object?>("Daedalus.Relay.Publish");
        message = pluginInterface.GetIpcSubscriber<string, string, object?>("Daedalus.Relay.Message");
        message.Subscribe(OnMessage);
        fleet.FleetChanged += OnFleetChanged;
    }

    /// <summary>A workshop read changed this FC's vessels: send them now rather than at the next rebroadcast.</summary>
    private void OnFleetChanged(ulong fcId)
    {
        if (config.ShareFleetOverLan && fleet.Store.TryGet(fcId, out var fc))
            Queue(fc);
    }

    private void Queue(FreeCompanyRecord fc)
    {
        foreach (var v in fc.Vessels)
        {
            var key = (fc.Id, v.Type, v.Slot);
            if (queued.Add(key))
                outbox.Enqueue(key);
        }
    }

    public void Update(DateTime nowUtc)
    {
        if (!config.ShareFleetOverLan)
        {
            outbox.Clear();
            queued.Clear();
            return;
        }

        if (nowUtc - lastBroadcast >= BroadcastEvery)
        {
            lastBroadcast = nowUtc;
            foreach (var fc in fleet.Store.Companies)
                Queue(fc);
        }

        // One frame per tick: Daedalus drops a second frame from this toon that carries the same send timestamp.
        if (outbox.Count == 0)
            return;

        var key = outbox.Dequeue();
        queued.Remove(key);
        if (!fleet.Store.TryGet(key.Fc, out var record))
            return;

        var vessel = record.Vessels.FirstOrDefault(v => v.Type == key.Type && v.Slot == key.Slot);
        if (vessel == null)
            return;

        try
        {
            publish.InvokeAction(Channel, FleetSyncMessage.From(record, vessel).ToJson());
            LastSentUtc = nowUtc;
            DaedalusMissing = false;
        }
        catch (IpcNotReadyError)
        {
            // Daedalus is not loaded; try again at the next rebroadcast.
            DaedalusMissing = true;
            outbox.Clear();
            queued.Clear();
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Argus: sharing a vessel over the LAN failed");
        }
    }

    private void OnMessage(string channel, string json)
    {
        if (channel != Channel || !config.ShareFleetOverLan)
            return;

        try
        {
            if (FleetSyncMessage.Parse(json) is not { } shared)
                return;

            Received++;
            LastReceivedUtc = DateTime.UtcNow;
            fleet.Store.MergeRemote(shared);
        }
        catch (Exception ex)
        {
            // Never let a bad frame from the LAN reach Daedalus's bus pump.
            Service.Log.Warning(ex, "Argus: a shared vessel from the LAN could not be filed");
        }
    }

    public void Dispose()
    {
        fleet.FleetChanged -= OnFleetChanged;
        message.Unsubscribe(OnMessage);
    }
}
