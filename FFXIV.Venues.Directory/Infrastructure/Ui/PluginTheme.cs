using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace FFXIV.Venues.Directory.Infrastructure.Ui;

// The plugin's own look for its windows, popups and tooltips, independent of the Dalamud style: colors, roundings, and the paddings and spacings the layout is measured against (the values of Dalamud's standard style). Everything is still scaled by Dalamud's global UI scale.
internal static class PluginTheme
{
    public static readonly Vector4 Background = Rgb(0x10, 0x13, 0x1f);
    public static readonly Vector4 Surface = Rgb(0x18, 0x1d, 0x2d);
    public static readonly Vector4 SurfaceRaised = Rgb(0x1c, 0x22, 0x35);
    public static readonly Vector4 Line = Rgb(0x2f, 0x36, 0x50);
    public static readonly Vector4 Control = Rgb(0x26, 0x2d, 0x45);
    public static readonly Vector4 ControlHovered = Rgb(0x31, 0x39, 0x57);
    public static readonly Vector4 ControlActive = Rgb(0x1e, 0x24, 0x38);
    public static readonly Vector4 Accent = Rgb(0x37, 0x52, 0x8a);
    public static readonly Vector4 AccentHovered = Rgb(0x42, 0x60, 0x9e);
    public static readonly Vector4 AccentActive = Rgb(0x2e, 0x46, 0x77);
    public static readonly Vector4 Text = Rgb(0xe3, 0xe6, 0xef);
    public static readonly Vector4 TextDim = Rgb(0x8e, 0x95, 0xab);

    public static IDisposable Push()
    {
        var colors = ImRaii.PushColor(ImGuiCol.Text, Text)
            .Push(ImGuiCol.TextDisabled, TextDim)
            .Push(ImGuiCol.WindowBg, Background)
            .Push(ImGuiCol.ChildBg, Vector4.Zero)
            .Push(ImGuiCol.PopupBg, WithAlpha(Surface, 0.98f))
            .Push(ImGuiCol.Border, Line)
            .Push(ImGuiCol.BorderShadow, Vector4.Zero)
            .Push(ImGuiCol.FrameBg, Rgb(0x14, 0x18, 0x28))
            .Push(ImGuiCol.FrameBgHovered, Rgb(0x1b, 0x21, 0x36))
            .Push(ImGuiCol.FrameBgActive, Rgb(0x21, 0x28, 0x40))
            .Push(ImGuiCol.TitleBg, Rgb(0x0d, 0x10, 0x1b))
            .Push(ImGuiCol.TitleBgActive, Rgb(0x14, 0x19, 0x2a))
            .Push(ImGuiCol.TitleBgCollapsed, Rgb(0x0d, 0x10, 0x1b))
            .Push(ImGuiCol.MenuBarBg, Rgb(0x0d, 0x10, 0x1b))
            .Push(ImGuiCol.ScrollbarBg, WithAlpha(Background, 0.6f))
            .Push(ImGuiCol.ScrollbarGrab, Line)
            .Push(ImGuiCol.ScrollbarGrabHovered, Rgb(0x3c, 0x45, 0x66))
            .Push(ImGuiCol.ScrollbarGrabActive, Rgb(0x4a, 0x55, 0x7d))
            .Push(ImGuiCol.CheckMark, Rgb(0x8c, 0xb2, 0xff))
            .Push(ImGuiCol.SliderGrab, Accent)
            .Push(ImGuiCol.SliderGrabActive, AccentHovered)
            .Push(ImGuiCol.Button, Control)
            .Push(ImGuiCol.ButtonHovered, ControlHovered)
            .Push(ImGuiCol.ButtonActive, ControlActive)
            .Push(ImGuiCol.Header, Rgb(0x2b, 0x3a, 0x64))
            .Push(ImGuiCol.HeaderHovered, Rgb(0x30, 0x40, 0x6c))
            .Push(ImGuiCol.HeaderActive, Accent)
            .Push(ImGuiCol.Separator, Line)
            .Push(ImGuiCol.SeparatorHovered, AccentHovered)
            .Push(ImGuiCol.SeparatorActive, Accent)
            .Push(ImGuiCol.ResizeGrip, WithAlpha(Line, 0.6f))
            .Push(ImGuiCol.ResizeGripHovered, AccentHovered)
            .Push(ImGuiCol.ResizeGripActive, Accent)
            .Push(ImGuiCol.TableHeaderBg, Rgb(0x1b, 0x20, 0x34))
            .Push(ImGuiCol.TableBorderStrong, Line)
            .Push(ImGuiCol.TableBorderLight, Rgb(0x22, 0x28, 0x3d))
            .Push(ImGuiCol.TableRowBg, Vector4.Zero)
            .Push(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.025f))
            .Push(ImGuiCol.TextSelectedBg, WithAlpha(Accent, 0.6f))
            .Push(ImGuiCol.NavHighlight, AccentHovered);
        var style = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 8f)
            .Push(ImGuiStyleVar.ChildRounding, 6f)
            .Push(ImGuiStyleVar.PopupRounding, 6f)
            .Push(ImGuiStyleVar.FrameRounding, 5f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 6f)
            .Push(ImGuiStyleVar.GrabRounding, 4f)
            .Push(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.ChildBorderSize, 1f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f)
            .Push(ImGuiStyleVar.FrameBorderSize, 0f)
            .Push(ImGuiStyleVar.WindowPadding, UiScale.Vector(8f, 8f))
            .Push(ImGuiStyleVar.FramePadding, UiScale.Vector(4f, 3f))
            .Push(ImGuiStyleVar.ItemSpacing, UiScale.Vector(8f, 4f))
            .Push(ImGuiStyleVar.ItemInnerSpacing, UiScale.Vector(4f, 4f))
            .Push(ImGuiStyleVar.CellPadding, UiScale.Vector(4f, 2f))
            .Push(ImGuiStyleVar.IndentSpacing, 21f * UiScale.Total)
            .Push(ImGuiStyleVar.ScrollbarSize, 14f * UiScale.Total)
            .Push(ImGuiStyleVar.GrabMinSize, 13f * UiScale.Total)
            .Push(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f))
            .Push(ImGuiStyleVar.SelectableTextAlign, Vector2.Zero)
            .Push(ImGuiStyleVar.WindowTitleAlign, new Vector2(0.5f, 0.5f));
        return new Pushed(colors, style);
    }

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    private static Vector4 WithAlpha(Vector4 color, float alpha) => color with { W = alpha };

    private sealed class Pushed(IDisposable colors, IDisposable style) : IDisposable
    {
        public void Dispose()
        {
            style.Dispose();
            colors.Dispose();
        }
    }
}
