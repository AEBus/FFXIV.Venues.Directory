using System;
using System.IO;
using System.Threading;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace FFXIV.Venues.Directory.Infrastructure.Ui;

// The fonts every plugin window draws with, at the plugin's interface size (UiScale):
// - text: Dalamud's default font plus the symbols and letters it lacks that venue text uses, merged from the Segoe UI and Segoe UI Symbol fonts that come with Windows. They are read from the system and never shipped, since their license forbids redistribution; without them, as under Wine, venue text falls back to replacing what the font cannot draw;
// - icons: FontAwesome at the same size, so icons grow with the text.
// A new interface size builds new fonts at the start of the next frame, before anything is drawn with the old ones.
internal sealed class PluginUiFont : IDisposable
{
    // Blocks where venue names and descriptions use characters AXIS lacks. Glyphs AXIS already has keep its look: ImGui never overwrites a glyph when merging.
    private static readonly ushort[] MergedRanges =
    [
        0x0100, 0x024F, // Latin Extended-A/B (ō, Ō, ƒ)
        0x0250, 0x02FF, // IPA Extensions, Spacing Modifier Letters (ʀ, ˚, ˖)
        0x0400, 0x04FF, // Cyrillic lookalikes (є, ѕ)
        0x1D00, 0x1DBF, // Phonetic Extensions (small caps ᴀ ᴇ ᴛ)
        0x2000, 0x209F, // General Punctuation, super- and subscripts (‑, ‿, ₊)
        0x2100, 0x214F, // Letterlike Symbols (ℎ, ℓ)
        0x2190, 0x23FF, // Arrows, Mathematical Operators, Miscellaneous Technical (⋆, ⏰)
        0x2500, 0x27BF, // Box Drawing, Geometric Shapes, Miscellaneous Symbols, Dingbats (✨ ❤ ✦ ☕ ⚔)
        0x27C0, 0x27FF, // Miscellaneous Mathematical Symbols-A, Supplemental Arrows-A (⟡)
        0x2B00, 0x2BFF, // Miscellaneous Symbols and Arrows (⭐)
        0xA720, 0xA7FF, // Latin Extended-D (ꜱ)
        0xFE30, 0xFE4F, // CJK Compatibility Forms (︵)
        0,
    ];

    private static readonly string[] SystemFontFiles = ["segoeui.ttf", "seguisym.ttf"];
    private static int s_version;

    private readonly IUiBuilder _uiBuilder;
    private IFontHandle? _text;
    private IFontHandle? _icons;
    private float _builtFactor = -1f;

    public PluginUiFont(IUiBuilder uiBuilder)
    {
        _uiBuilder = uiBuilder;
        Rebuild();
    }

    // The icon font for this frame: FontAwesome at the interface size, or Dalamud's until it is built.
    public static ImFontPtr IconFont { get; private set; }

    // Goes up whenever a font of the plugin was (re)built, so cached text measurements know to redo themselves.
    public static int Version => Volatile.Read(ref s_version);

    // Called once per frame before any window draws: builds the fonts again for a new interface size, then pushes the text font (Dalamud's default until it is built).
    public IDisposable Push()
    {
        if (_builtFactor != UiScale.Factor)
        {
            Rebuild();
        }

        IconFont = UiBuilder.IconFont;
        if (_icons is { Available: true })
        {
            using (_icons.Push())
            {
                IconFont = ImGui.GetFont();
            }
        }

        return _text!.Push();
    }

    public void Dispose()
    {
        DisposeHandles();
    }

    private void Rebuild()
    {
        DisposeHandles();
        _builtFactor = UiScale.Factor;
        var size = _uiBuilder.FontDefaultSizePx * _builtFactor;
        _text = _uiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit => BuildText(toolkit, size)));
        _text.ImFontChanged += OnTextFontChanged;
        _icons = _uiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit => toolkit.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = size })));
        _icons.ImFontChanged += OnIconFontChanged;
        Interlocked.Increment(ref s_version);
    }

    private void DisposeHandles()
    {
        if (_text != null)
        {
            _text.ImFontChanged -= OnTextFontChanged;
            _text.Dispose();
            _text = null;
        }

        if (_icons != null)
        {
            _icons.ImFontChanged -= OnIconFontChanged;
            _icons.Dispose();
            _icons = null;
        }
    }

    private static void BuildText(IFontAtlasBuildToolkitPreBuild toolkit, float sizePx)
    {
        var font = toolkit.AddDalamudDefaultFont(sizePx);
        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var file in SystemFontFiles)
        {
            var path = Path.Combine(fontsDirectory, file);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                toolkit.AddFontFromFile(path, new SafeFontConfig { SizePx = sizePx, MergeFont = font, GlyphRanges = MergedRanges });
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Warning(ex, "Could not merge {Font} into the UI font.", file);
            }
        }

        toolkit.Font = font;
    }

    private static void OnTextFontChanged(IFontHandle handle, ILockedImFont lockedFont)
    {
        UiGlyphs.Capture(lockedFont.ImFont);
        Interlocked.Increment(ref s_version);
    }

    private static void OnIconFontChanged(IFontHandle handle, ILockedImFont lockedFont) =>
        Interlocked.Increment(ref s_version);
}
