using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Argus.Windows;

/// <summary>
/// Argus palette and shared style pushes. Dark cards in the Parcae idiom; teal/cyan is the identity accent
/// ("the eyes"), amber means a vessel is back and waiting, mint is good, rose is a problem.
/// </summary>
internal static class Styling
{
    public static readonly Vector4 AccentTeal      = new(0.16f, 0.78f, 0.74f, 1.00f);
    public static readonly Vector4 AccentTealSoft  = new(0.50f, 0.92f, 0.88f, 1.00f);
    public static readonly Vector4 AccentCyan      = new(0.47f, 0.94f, 0.92f, 1.00f);
    public static readonly Vector4 AccentAmber     = new(0.92f, 0.74f, 0.34f, 1.00f);
    public static readonly Vector4 AccentAmberSoft = new(1.00f, 0.86f, 0.52f, 1.00f);
    public static readonly Vector4 AccentMint      = new(0.46f, 0.86f, 0.66f, 1.00f);
    public static readonly Vector4 AccentRose      = new(0.93f, 0.42f, 0.50f, 1.00f);
    public static readonly Vector4 AccentBlue      = new(0.40f, 0.68f, 0.98f, 1.00f);
    public static readonly Vector4 AccentViolet    = new(0.62f, 0.42f, 0.96f, 1.00f);

    public static readonly Vector4 CardBg      = new(0.070f, 0.090f, 0.105f, 0.85f);
    public static readonly Vector4 CardBgSoft  = new(0.085f, 0.105f, 0.120f, 0.55f);
    public static readonly Vector4 CardBgHover = new(0.100f, 0.125f, 0.145f, 0.95f);
    public static readonly Vector4 BorderDim   = new(0.22f, 0.26f, 0.30f, 1.00f);

    public static readonly Vector4 TextStrong    = new(0.96f, 0.96f, 0.97f, 1.00f);
    public static readonly Vector4 TextSecondary = new(0.78f, 0.80f, 0.84f, 1.00f);
    public static readonly Vector4 TextDim       = new(0.55f, 0.58f, 0.62f, 1.00f);
    public static readonly Vector4 TextMuted     = new(0.40f, 0.42f, 0.46f, 1.00f);

    public static readonly Vector4 Hairline = new(1f, 1f, 1f, 0.055f);

    public const float CardRounding = 7f;
    public const float FrameRounding = 5f;
    public const float WindowRounding = 7f;

    public const double PulseMedium = 800.0;
    public const double PulseBreath = 2600.0;
    public const double PulseOrbit = 3400.0;

    /// <summary>0..1 sine pulse on the given period, for "something is happening" glows.</summary>
    public static float Pulse(double periodMs = PulseMedium)
    {
        var t = (Environment.TickCount % periodMs) / periodMs;
        return (float)((Math.Sin(t * Math.PI * 2.0) + 1.0) * 0.5);
    }

    public static Vector4 PulseColor(Vector4 a, Vector4 b, double periodMs = PulseMedium)
        => Vector4.Lerp(a, b, Pulse(periodMs));

    public static float Phase(double periodMs)
        => (float)((Environment.TickCount % periodMs) / periodMs);

    public static Vector4 WithAlpha(Vector4 c, float a) => c with { W = a };

    /// <summary>Color for a vessel state: amber = returned, teal = out, dim = idle.</summary>
    public static Vector4 StateColor(bool returned, bool deployed)
        => returned ? AccentAmber : deployed ? AccentTeal : TextDim;

    public static void VSpace(float pixels)
        => ImGui.Dummy(new Vector2(0, pixels * ImGuiHelpers.GlobalScale));

    public static void SectionLabel(string label)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted(label.ToUpperInvariant());
    }

    public static void Text(string text, Vector4 color)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
    }

    /// <summary>Text that wraps at the content edge, for sentences rather than labels.</summary>
    public static void TextWrapped(string text, Vector4 color)
    {
        ImGui.PushTextWrapPos(0f);
        Text(text, color);
        ImGui.PopTextWrapPos();
    }

    public static void TextScaled(string text, Vector4 color, float fontScale)
    {
        ImGui.SetWindowFontScale(fontScale);
        Text(text, color);
        ImGui.SetWindowFontScale(1f);
    }

    public static void TextCentered(string text, Vector4 color, float fontScale = 1f)
    {
        if (fontScale != 1f) ImGui.SetWindowFontScale(fontScale);
        var w = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > w) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - w) * 0.5f);
        Text(text, color);
        if (fontScale != 1f) ImGui.SetWindowFontScale(1f);
    }

    public static IDisposable PushWindowStyle()
    {
        var p = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, FrameRounding);
        p.Push(ImGuiStyleVar.WindowRounding, WindowRounding);
        p.Push(ImGuiStyleVar.ChildRounding, CardRounding);
        p.Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7) * ImGuiHelpers.GlobalScale);
        return p;
    }

    public static IDisposable PushCardStyle()
    {
        var p = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, CardRounding * ImGuiHelpers.GlobalScale);
        p.Push(ImGuiStyleVar.ChildBorderSize, 1f);
        p.Push(ImGuiStyleVar.WindowPadding, new Vector2(11, 9) * ImGuiHelpers.GlobalScale);
        p.Push(ImGuiStyleVar.FrameRounding, FrameRounding);
        return p;
    }

    public static void Tooltip(string text, float wrapWidth = 300f)
    {
        if (!ImGui.IsItemHovered())
            return;

        using var tip = ImRaii.Tooltip();
        ImGui.PushTextWrapPos(wrapWidth * ImGuiHelpers.GlobalScale);
        Text(text, TextSecondary);
        ImGui.PopTextWrapPos();
    }
}
