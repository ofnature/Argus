using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Argus.Windows.Components;

/// <summary>Small rounded status chip with a dot, e.g. "RETURNED", "OUT · 3h 12m".</summary>
internal static class Pill
{
    public static Vector2 Measure(string label, float fontScale = 0.78f)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetWindowFontScale(fontScale);
        var textSize = ImGui.CalcTextSize(label);
        ImGui.SetWindowFontScale(1f);
        return new Vector2(textSize.X + 28f * scale, 22f * scale);
    }

    /// <summary>Draws at an absolute screen position using the draw list; does not move the cursor.</summary>
    public static void DrawAt(Vector2 origin, string label, Vector4 accent, float fontScale = 0.78f)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var size = Measure(label, fontScale);
        var end = origin + size;
        var dot = origin + new Vector2(9f * scale, size.Y * 0.5f);

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, accent, 0.16f)), size.Y * 0.5f);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(accent, 0.55f)), size.Y * 0.5f);
        dl.AddCircleFilled(dot, 2.8f * scale, ImGui.GetColorU32(accent));

        ImGui.SetWindowFontScale(fontScale);
        var textSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(dot.X + 7f * scale, origin.Y + (size.Y - textSize.Y) * 0.5f), ImGui.GetColorU32(accent), label);
        ImGui.SetWindowFontScale(1f);
    }

    /// <summary>Draws inline at the cursor and advances it.</summary>
    public static void Draw(string label, Vector4 accent, float fontScale = 0.78f)
    {
        var origin = ImGui.GetCursorScreenPos();
        DrawAt(origin, label, accent, fontScale);
        ImGui.Dummy(Measure(label, fontScale));
    }
}
