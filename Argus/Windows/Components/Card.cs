using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Argus.Windows.Components;

/// <summary>A rounded, bordered child region. Dispose the scope to end it.</summary>
internal static class Card
{
    public static Scope Begin(string id, Vector2 size, Vector4? background = null, Vector4? border = null, float borderSize = 1f,
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        var style = Styling.PushCardStyle();
        var bg = ImRaii.PushColor(ImGuiCol.ChildBg, background ?? Styling.CardBg);
        var br = ImRaii.PushColor(ImGuiCol.Border, border ?? Styling.WithAlpha(Styling.BorderDim, 0.6f));
        var sz = ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, borderSize);
        var child = ImRaii.Child(id, size, true, flags);
        return new Scope(child, sz, br, bg, style);
    }

    /// <summary>Draw a card-shaped background behind content of unknown height: call before, then <see cref="EndFlat"/>.</summary>
    public static Vector2 BeginFlat()
    {
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup();
        ImGui.SetCursorScreenPos(origin + new Vector2(11f, 9f) * ImGuiHelpers.GlobalScale);
        ImGui.BeginGroup();
        return origin;
    }

    public static void EndFlat(Vector2 origin, float width, Vector4? background = null, Vector4? accent = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.EndGroup();
        var end = new Vector2(origin.X + width, ImGui.GetItemRectMax().Y + 9f * scale);
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSetCurrent(0);
        var rounding = Styling.CardRounding * scale;
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(background ?? Styling.CardBgSoft), rounding);
        dl.AddRect(origin, end, ImGui.GetColorU32(Styling.WithAlpha(Styling.BorderDim, 0.55f)), rounding);
        if (accent is { } a)
            dl.AddRectFilled(origin, new Vector2(origin.X + 3f * scale, end.Y), ImGui.GetColorU32(Styling.WithAlpha(a, 0.85f)), 2f);
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(origin.X, end.Y));
        ImGui.Dummy(new Vector2(width, 0f));
        ImGui.EndGroup();
    }

    public ref struct Scope
    {
        private ImRaii.ChildDisposable child;
        private readonly IDisposable sz;
        private readonly IDisposable br;
        private readonly IDisposable bg;
        private readonly IDisposable style;

        internal Scope(ImRaii.ChildDisposable child, IDisposable sz, IDisposable br, IDisposable bg, IDisposable style)
        {
            this.child = child;
            this.sz = sz;
            this.br = br;
            this.bg = bg;
            this.style = style;
        }

        public bool Success => child.Success;

        public void Dispose()
        {
            child.Dispose();
            sz.Dispose();
            br.Dispose();
            bg.Dispose();
            style.Dispose();
        }
    }
}
