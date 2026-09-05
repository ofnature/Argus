using System;
using System.Text;
using Argus.Core;
using Argus.Core.Model;
using Argus.Windows;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;

namespace Argus;

/// <summary>
/// One server-info-bar entry: "Argus: Subs 2/4 · Air 1/2" as ready/total per vessel type, amber when anything is
/// waiting to be collected. Text is rebuilt only when it changes so the bar does not re-layout every frame.
/// </summary>
internal sealed class DtrStatus : IDisposable
{
    private const ushort ColorReady = 500;   // amber-ish UIColor
    private const ushort ColorOut = 45;      // teal/green
    private const ushort ColorIdle = 3;      // grey

    private readonly IDtrBarEntry? entry;
    private readonly FleetService fleet;
    private readonly Configuration config;

    private string lastText = string.Empty;
    private bool lastShown;
    private bool primed;

    public DtrStatus(FleetService fleet, Configuration config, Action openMain)
    {
        this.fleet = fleet;
        this.config = config;

        try
        {
            entry = Service.DtrBar.Get("Argus");
        }
        catch (Exception ex)
        {
            // Another plugin owns the title; a status readout is not worth failing the load over.
            Service.Log.Warning(ex, "Argus: could not claim the server info bar entry.");
            return;
        }

        entry.OnClick = ev =>
        {
            if (ev.ClickType == MouseClickType.Left)
                openMain();
        };
    }

    public void Update(DateTime nowUtc)
    {
        if (entry == null)
            return;

        var show = config.ShowDtrBar && (config.DtrShowSubmarines || config.DtrShowAirships);
        if (!primed || show != lastShown)
        {
            entry.Shown = show;
            lastShown = show;
        }

        var subs = fleet.CountsFor(VesselType.Submarine, nowUtc);
        var air = fleet.CountsFor(VesselType.Airship, nowUtc);

        // Cache key: rebuilding the SeString every frame makes the whole bar jitter.
        var key = $"{subs.Ready}/{subs.Total}|{air.Ready}/{air.Total}|{config.DtrShowSubmarines}|{config.DtrShowAirships}";
        if (primed && key == lastText)
            return;
        lastText = key;
        primed = true;

        var sb = new SeStringBuilder().AddText("Argus: ");
        var first = true;
        if (config.DtrShowSubmarines)
        {
            Append(sb, "Subs", subs);
            first = false;
        }

        if (config.DtrShowAirships)
        {
            if (!first)
                sb.AddText(" · ");
            Append(sb, "Air", air);
        }

        entry.Text = sb.Build();
        entry.Tooltip = BuildTooltip(subs, air, nowUtc);
    }

    private static void Append(SeStringBuilder sb, string label, FleetService.Counts c)
    {
        var color = c.Total == 0 ? ColorIdle : c.Ready > 0 ? ColorReady : ColorOut;
        sb.AddText($"{label} ");
        sb.AddUiForeground(color).AddText($"{c.Ready}/{c.Total}").AddUiForegroundOff();
    }

    private static string BuildTooltip(FleetService.Counts subs, FleetService.Counts air, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        Line(sb, "Submarines", subs, nowUtc);
        sb.Append('\n');
        Line(sb, "Airships", air, nowUtc);
        sb.Append("\n\nReady = returned or never dispatched. Click to open Argus.");
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string label, FleetService.Counts c, DateTime nowUtc)
    {
        sb.Append(label).Append(": ");
        if (c.Total == 0)
        {
            sb.Append("none seen");
            return;
        }

        sb.Append(c.Out).Append(" out, ").Append(c.Returned).Append(" returned, ").Append(c.Idle).Append(" idle");
        if (c.NextReturn is { } next)
            sb.Append("\n  next back: ").Append(next.Name).Append(" in ").Append(Formatting.Duration(next.Remaining(nowUtc)));
    }

    public void Dispose()
    {
        entry?.Remove();
    }
}
