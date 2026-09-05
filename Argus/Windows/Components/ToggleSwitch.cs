using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Argus.Windows.Components;

internal static class ToggleSwitch
{
    public const float Height = 20f;

    public static bool Draw(string id, ref bool value)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var accent = Styling.AccentTeal;
        var trackWidth = 38f * scale;
        var trackHeight = Height * scale;
        var knobRadius = (trackHeight - 4f * scale) * 0.5f;
        var padX = 3f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(trackWidth, trackHeight);
        var dl = ImGui.GetWindowDrawList();
        var hovered = ImGui.IsMouseHoveringRect(origin, end);

        var track = value
            ? Vector4.Lerp(accent * 0.7f, accent, hovered ? 0.6f : 0.3f)
            : Vector4.Lerp(Styling.CardBgSoft, Styling.CardBgHover, hovered ? 1f : 0f);
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(track), trackHeight * 0.5f);
        dl.AddRect(origin, end, ImGui.GetColorU32(value ? accent : Styling.BorderDim), trackHeight * 0.5f);

        var knobX = value ? end.X - knobRadius - padX : origin.X + knobRadius + padX;
        dl.AddCircleFilled(new Vector2(knobX, origin.Y + trackHeight * 0.5f), knobRadius,
            ImGui.GetColorU32(value ? Styling.TextStrong : Styling.TextSecondary));

        ImGui.Dummy(new Vector2(trackWidth, trackHeight));

        if (!hovered)
            return false;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return false;

        value = !value;
        return true;
    }
}
