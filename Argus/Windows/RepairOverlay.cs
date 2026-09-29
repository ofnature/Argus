using System;
using System.Linq;
using System.Numerics;
using Argus.Core.Game;
using Argus.Core.Model;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Argus.Windows;

/// <summary>
/// Docked beside a vessel's menu on the Voyage Control Panel while that vessel has a broken part: the game refuses the
/// voyage at Deploy, so the repair belongs here, one click before it. Kits are spent only when the button is pressed.
/// </summary>
public sealed class RepairOverlay : Window, IDisposable
{
    private const float Width = 280f;

    /// <summary>How long the outcome of a run stays up once the menu is back.</summary>
    private static readonly TimeSpan OutcomeShown = TimeSpan.FromSeconds(30);

    private readonly Plugin plugin;
    private Vessel? vessel;
    private DateTime lastRunningUtc = DateTime.MinValue;

    public RepairOverlay(Plugin plugin) : base("Argus Repair Overlay###ArgusRepairOverlay")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ForceMainWindow = true;
    }

    public void Dispose() { }

    public override void PreOpenCheck()
    {
        IsOpen = false;
        var now = DateTime.UtcNow;
        var repair = plugin.RepairInterop;

        // The menu is hidden while the component window is up, so remember the run to show its outcome afterwards.
        if (repair.Running)
            lastRunningUtc = now;

        var rect = RepairInterop.MenuRect();
        var fcId = plugin.Fleet.CurrentFreeCompanyId;
        if (rect == null || fcId == 0 || !plugin.Fleet.Store.TryGet(fcId, out var fc))
        {
            vessel = null;
            return;
        }

        vessel = fc.Vessels.FirstOrDefault(v => WorkshopReader.IsSelected(v.Type, v.Slot) == true);
        if (vessel == null || !RepairInterop.IsVesselMenu(vessel.Type))
            return;

        var outcome = repair.LastRunId == RepairControls.RunId(vessel) && now - lastRunningUtc < OutcomeShown;
        if (vessel.BrokenSlots().Count == 0 && !outcome)
            return;

        var (x, y, w, _) = rect.Value;
        Position = new Vector2(x + w + 4f, y + 4f);
        PositionCondition = ImGuiCond.Always;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(Width * ImGuiHelpers.GlobalScale, 0));
    }

    public override void Draw()
    {
        using var style = Styling.PushWindowStyle();
        using var bg = ImRaii.PushColor(ImGuiCol.WindowBg, Styling.CardBg);
        var v = vessel;
        if (v == null)
            return;

        Styling.TextScaled("ARGUS", Styling.AccentTealSoft, 0.85f);
        ImGui.SameLine();
        Styling.Text($"{v.Name} · R{v.Rank}", Styling.TextStrong);

        var broken = v.BrokenSlots();
        if (broken.Count > 0)
        {
            var parts = string.Join(", ", broken.Select(s => Build.SlotName(v.Type, s).ToLowerInvariant()));
            Styling.TextWrapped($"Broken: {parts}. The game will not send it until repaired.", Styling.AccentAmber);
        }

        RepairControls.Draw(plugin, v, ImGui.GetContentRegionAvail().X, stackBlocker: true);
    }
}
