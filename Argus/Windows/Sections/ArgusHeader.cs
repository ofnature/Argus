using System;
using System.Numerics;
using Argus.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace Argus.Windows.Sections;

/// <summary>
/// Identity strip across the top of the main window: the eye mark, the name, a status pill (how many vessels are
/// waiting) and the settings shortcut.
/// </summary>
internal static class ArgusHeader
{
    public static void Draw(Plugin plugin)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Layout.HeaderHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();

        var left = Vector4.Lerp(Styling.CardBg, Styling.AccentTeal, 0.16f);
        var right = Vector4.Lerp(Styling.CardBg, Styling.AccentBlue, 0.06f);
        dl.AddRectFilledMultiColor(origin, end,
            ImGui.GetColorU32(left), ImGui.GetColorU32(right), ImGui.GetColorU32(right), ImGui.GetColorU32(left));
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(Styling.AccentTealSoft, 0.38f)),
            Styling.CardRounding * scale, ImDrawFlags.None, 1.2f * scale);

        DrawEye(origin + new Vector2(40f, 44f) * scale, scale, dl);

        var textX = origin.X + 80f * scale;
        ImGui.SetWindowFontScale(1.62f);
        dl.AddText(new Vector2(textX, origin.Y + 16f * scale), ImGui.GetColorU32(Styling.TextStrong), "ARGUS");
        ImGui.SetWindowFontScale(0.78f);
        dl.AddText(new Vector2(textX, origin.Y + 50f * scale), ImGui.GetColorU32(Styling.AccentTealSoft), "DEPLOYABLE WATCH");
        ImGui.SetWindowFontScale(1f);

        var (status, detail, accent) = plugin.Fleet.HeaderStatus(DateTime.UtcNow);
        var pillSize = Pill.Measure(status);
        Pill.DrawAt(new Vector2(end.X - 18f * scale - pillSize.X, origin.Y + 16f * scale), status, accent);

        ImGui.SetWindowFontScale(0.82f);
        var detailSize = ImGui.CalcTextSize(detail);
        dl.AddText(new Vector2(end.X - 18f * scale - detailSize.X, origin.Y + 46f * scale), ImGui.GetColorU32(Styling.TextDim), detail);
        ImGui.SetWindowFontScale(1f);

        var button = 24f * scale;
        ImGui.SetCursorScreenPos(new Vector2(end.X - 18f * scale - button, end.Y - 30f * scale));
        if (Buttons.Icon(FontAwesomeIcon.Cog, "##argus_settings", button, "Settings"))
            plugin.MainWindow.ShowPage(MainWindow.Page.Settings);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    /// <summary>The mark: a slow-orbiting ring of small eyes around a teal iris.</summary>
    private static void DrawEye(Vector2 center, float scale, ImDrawListPtr dl)
    {
        var phase = Styling.Phase(Styling.PulseOrbit) * MathF.PI * 2f;
        var orbit = 18f * scale;
        const int eyes = 8;
        for (var i = 0; i < eyes; i++)
        {
            var angle = phase + i * MathF.PI * 2f / eyes;
            var p = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * orbit;
            var alpha = 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(angle * 2f + phase));
            dl.AddCircleFilled(p, 2.6f * scale, ImGui.GetColorU32(Styling.WithAlpha(Styling.AccentTealSoft, alpha)));
        }

        var breath = 0.85f + 0.15f * Styling.Pulse(Styling.PulseBreath);
        dl.AddCircleFilled(center, 10f * scale, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, Styling.AccentTeal, 0.55f)));
        dl.AddCircle(center, 10f * scale, ImGui.GetColorU32(Styling.AccentTealSoft), 32, 1.5f * scale);
        dl.AddCircleFilled(center, 4.2f * scale * breath, ImGui.GetColorU32(Styling.AccentCyan));
        dl.AddCircleFilled(center + new Vector2(-1.6f, -1.6f) * scale, 1.2f * scale, ImGui.GetColorU32(Styling.TextStrong));
    }
}
