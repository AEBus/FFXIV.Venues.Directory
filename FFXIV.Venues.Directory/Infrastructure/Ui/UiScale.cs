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
}
