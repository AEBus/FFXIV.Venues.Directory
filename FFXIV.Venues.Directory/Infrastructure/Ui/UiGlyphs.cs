using System.Collections;
using System.Threading;
using Dalamud.Bindings.ImGui;

namespace FFXIV.Venues.Directory.Infrastructure.Ui;

// Which characters the plugin's UI font can draw, captured whenever that font is built. Venue text is prepared on background threads and reads this to keep what renders and replace only what would show as a fallback box.
internal static class UiGlyphs
{
    private static Snapshot? current;

    // Null until the font has been built once.
    public static Snapshot? Current => Volatile.Read(ref current);

    public static void Capture(ImFontPtr font)
    {
        var drawable = new BitArray(0x10000);
        var glyphs = font.Glyphs;
        for (var i = 0; i < glyphs.Size; i++)
        {
            var codepoint = glyphs[i].Codepoint;
            if (codepoint < 0x10000)
            {
                drawable[(int)codepoint] = true;
            }
        }

        // A rebuild for a new scale draws the same characters; only a different set moves the generation.
        var previous = Current;
        if (previous != null && previous.SameAs(drawable))
        {
            return;
        }

        Volatile.Write(ref current, new Snapshot(drawable, (previous?.Generation ?? 0) + 1));
    }

    internal sealed class Snapshot(BitArray drawable, int generation)
    {
        public int Generation { get; } = generation;

        // ImGui fonts hold the Basic Multilingual Plane only: nothing above U+FFFF can be drawn.
        public bool CanDraw(int codepoint) => codepoint is >= 0 and < 0x10000 && drawable[codepoint];

        public bool SameAs(BitArray other)
        {
            var difference = new BitArray(other).Xor(drawable);
            for (var i = 0; i < difference.Length; i++)
            {
                if (difference[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
