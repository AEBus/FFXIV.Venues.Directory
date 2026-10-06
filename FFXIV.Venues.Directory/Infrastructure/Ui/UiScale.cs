using System;
using System.Numerics;
using System.Threading;
using Dalamud.Interface.Utility;

namespace FFXIV.Venues.Directory.Infrastructure.Ui;

// How big the plugin draws: Dalamud's global UI scale times the plugin's own interface size. Every size in the windows (fonts, paddings, the layout's pixel constants, the minimum window size) goes through here, so they grow and shrink together.
internal static class UiScale
{
    public static readonly float[] Presets = [0.9f, 1f, 1.15f, 1.3f];

    private static float s_factor = 1f;

    // The plugin's own size, 1 = the size Dalamud draws at.
    public static float Factor => Volatile.Read(ref s_factor);

    public static float Total => ImGuiHelpers.GlobalScale * Factor;

    public static void SetFactor(float factor) =>
        Volatile.Write(ref s_factor, Math.Clamp(factor, Presets[0], Presets[^1]));

    public static float Of(float value) => value * Total;

    public static Vector2 Vector(float x, float y) => new(x * Total, y * Total);

    // Returns a window size, in the units Dalamud's Window takes (it multiplies them by the global scale), no larger than the game window, so a large UI scale on a small screen cannot push a window off the screen.
    public static Vector2 FitToScreen(Vector2 size) => FitToScreen(size, ImGuiHelpers.MainViewport.Size, ImGuiHelpers.GlobalScale);

    // screen: the game window in pixels; globalScale: Dalamud's global scale. A size is returned unchanged until both are known.
    internal static Vector2 FitToScreen(Vector2 size, Vector2 screen, float globalScale) =>
        screen.X <= 0f || screen.Y <= 0f || globalScale <= 0f ? size : Vector2.Min(size, screen / globalScale);
}
