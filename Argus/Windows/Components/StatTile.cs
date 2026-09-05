using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Argus.Windows.Components;

/// <summary>One metric: dim uppercase label on top, strong value bottom-left, optional dim sub bottom-right, accent tick on the left.</summary>
internal static class StatTile
{
    public static void Draw(string label, string value, string? sub, Vector4 accent, float width, string? tooltip = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.StatTileHeight * scale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(width, height);
        var dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Styling.CardBgSoft), 6f * scale);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.6f)), 6f * scale);
        dl.AddRectFilled(origin, new Vector2(origin.X + 3f * scale, end.Y), ImGui.GetColorU32(Styling.WithAlpha(accent, 0.85f)), 2f);

        var padX = 11f * scale;
        var padY = 7f * scale;

        ImGui.SetWindowFontScale(0.80f);
        dl.AddText(new Vector2(origin.X + padX, origin.Y + padY), ImGui.GetColorU32(Styling.TextDim), label.ToUpperInvariant());
        ImGui.SetWindowFontScale(1.25f);
        var valSize = ImGui.CalcTextSize(value);
        var valY = end.Y - padY - valSize.Y;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, valY));
        Styling.Text(value, Styling.TextStrong);
        ImGui.SetWindowFontScale(1f);

        if (!string.IsNullOrEmpty(sub))
        {
            ImGui.SetWindowFontScale(0.80f);
            var subSize = ImGui.CalcTextSize(sub);
            dl.AddText(new Vector2(end.X - padX - subSize.X, valY + valSize.Y - subSize.Y), ImGui.GetColorU32(Styling.TextDim), sub);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        if (tooltip != null)
            Styling.Tooltip(tooltip);
    }
}
