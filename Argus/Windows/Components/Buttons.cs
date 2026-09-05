using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Argus.Windows.Components;

internal static class Buttons
{
    /// <summary>Teal-filled action button. Disabled buttons are flat and muted.</summary>
    public static bool Action(string label, bool enabled = true, float width = 0f, Vector4? accent = null)
    {
        var a = accent ?? Styling.AccentTeal;
        using var disabled = ImRaii.Disabled(!enabled);
        using var color = enabled
            ? ImRaii.PushColor(ImGuiCol.Button, a * 0.55f)
                .Push(ImGuiCol.ButtonHovered, a * 0.75f)
                .Push(ImGuiCol.ButtonActive, a)
            : ImRaii.PushColor(ImGuiCol.Button, Styling.CardBgSoft)
                .Push(ImGuiCol.ButtonHovered, Styling.CardBgSoft)
                .Push(ImGuiCol.ButtonActive, Styling.CardBgSoft);

        return ImGui.Button(label, new Vector2(width, Layout.ActionButtonHeight * ImGuiHelpers.GlobalScale));
    }

    /// <summary>Square icon-font button; the id must start with "##".</summary>
    public static bool Icon(FontAwesomeIcon icon, string id, float size, string? tooltip = null, Vector4? textColor = null)
    {
        bool clicked;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, textColor ?? Styling.TextSecondary))
        using (ImRaii.PushColor(ImGuiCol.Button, Styling.CardBgSoft))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Styling.CardBgHover))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, Styling.AccentTeal * 0.55f))
            clicked = ImGui.Button(icon.ToIconString() + id, new Vector2(size, size));

        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return clicked;
    }
}
