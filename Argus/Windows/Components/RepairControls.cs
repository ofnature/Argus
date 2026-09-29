using Argus.Core.Calc;
using Argus.Core.Game;
using Argus.Core.Model;
using Dalamud.Bindings.ImGui;

namespace Argus.Windows.Components;

/// <summary>
/// The Repair button for a vessel with broken parts, its reason when it cannot run, and the outcome of the run it
/// started. Shared by the Vessels card and the panel beside the vessel's menu, so both behave the same.
/// </summary>
internal static class RepairControls
{
    /// <summary>The run tag for a vessel: the card and the menu panel share it, so either shows a run the other started.</summary>
    public static string RunId(Vessel v) => "repair:" + Configuration.VesselKey(v);

    public static void Draw(Plugin plugin, Vessel v, float buttonWidth, bool stackBlocker = false)
    {
        var broken = v.BrokenSlots();
        var repair = plugin.RepairInterop;
        var rowId = RunId(v);
        if (broken.Count > 0)
        {
            var cost = RepairInterop.Cost(plugin.Data, v, broken);
            var kits = PartsInterop.Carried(Supplies.MagitekRepairMaterialsItem);
            var blocker = kits < cost ? $"needs {cost} Magitek Repair Materials, carrying {kits}"
                : repair.Blocker(v, plugin.PartsInterop.Running);

            var label = $"Repair {broken.Count} part{(broken.Count == 1 ? string.Empty : "s")} ({cost} kits)";
            if (Buttons.Action(label, blocker == null, buttonWidth, Styling.AccentAmber))
            {
                Service.Log.Information("Argus: repair pressed for {Vessel}, slots {Slots}", v.Name, string.Join(",", broken));
                if (!repair.Start(plugin.Data, v, plugin.PartsInterop.Running, rowId))
                    Service.Log.Information("Argus: repair refused: {Reason}", repair.LastError ?? repair.LastResult ?? "unknown");
            }

            if (blocker != null)
            {
                if (!stackBlocker)
                    ImGui.SameLine();
                Styling.TextWrapped(blocker, Styling.TextMuted);
            }
        }

        // Only where it was asked: the interop's state is global, and every card would otherwise claim the result.
        if (repair.LastRunId != rowId)
            return;

        if (repair.Running)
            Styling.TextWrapped($"Repairing… {repair.Repaired} done, {repair.Remaining} to go ({repair.StageName})", Styling.PulseColor(Styling.AccentAmber, Styling.AccentAmberSoft));
        else if (repair.LastError != null)
            Styling.TextWrapped(repair.LastError, Styling.AccentRose);
        else if (repair.LastResult != null)
            Styling.TextWrapped(repair.LastResult, Styling.AccentMint);
    }
}
