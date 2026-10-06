using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIV.Venues.Directory.Infrastructure.Media;
using FFXIV.Venues.Directory.Infrastructure.Net;
using FFXIV.Venues.Directory.Infrastructure.Ui;

namespace FFXIV.Venues.Directory.Infrastructure.RichText;

internal readonly record struct RichTextColors(
    Vector4 Text,
    Vector4 LinkHovered,
    Vector4 Time,
    Vector4 TimeBackground,
    Vector4 Muted,
    Vector4 CodeBackground,
    Vector4 Rule);

// Draws a RichDocument into the current ImGui window at the available width, in the current font: headings at larger sizes, bold as a double stroke, italic as sheared glyphs, underline and strikethrough as lines, links clickable, time tags in local time, images loaded from their URLs as they come into view. One view per place a document is shown; the layout is rebuilt only when the document, the width, the text version or a loaded image changes.
internal sealed class RichTextView
{
    private const float ItalicShear = 0.2f;

    private readonly RemoteImageCache _images;
    private readonly Func<bool> _loadImages;
    private readonly Func<bool> _previewImages;
    private RichDocument? _document;
    private RichTextLayout? _layout;
    private float _width = -1f;
    private float _fontSize = -1f;
    private int _textVersion = -1;
    private int _imageVersion = -1;
    private bool _imagesLoaded;

    // loadImages: whether pictures are fetched; when it says no, each one is a link to open in the browser.
    public RichTextView(RemoteImageCache images, Func<bool> loadImages, Func<bool> previewImages)
    {
        _images = images;
        _loadImages = loadImages;
        _previewImages = previewImages;
    }

    public float Height => _layout?.Height ?? 0f;

    // textVersion: bump it when formatTime would now give other text (the clock format changed).
    public void Draw(RichDocument document, int textVersion, Func<DateTimeOffset, string> formatTime, Func<DateTimeOffset, string> describeTime, RichTextColors colors)
    {
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var loadImages = _loadImages();
        if (_layout == null ||
            !ReferenceEquals(_document, document) ||
            MathF.Abs(_width - width) > 0.5f ||
            _fontSize != fontSize ||
            _textVersion != textVersion ||
            _imageVersion != _images.Version ||
            _imagesLoaded != loadImages)
        {
            _document = document;
            _width = width;
            _fontSize = fontSize;
            _textVersion = textVersion;
            _imageVersion = _images.Version;
            _imagesLoaded = loadImages;
            _layout = RichTextLayout.Build(document, width, fontSize, new FontMeasurer(font), formatTime, ImageInfo);
        }

        var layout = _layout;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, layout.Height));

        var drawList = ImGui.GetWindowDrawList();
        var clipMin = drawList.GetClipRectMin();
        var clipMax = drawList.GetClipRectMax();
        var mouse = ImGui.GetIO().MousePos;
        var windowHovered = ImGui.IsWindowHovered();

        foreach (var piece in layout.Pieces)
        {
            var min = origin + new Vector2(piece.X, piece.Y);
            var max = min + new Vector2(piece.Width, piece.Height);
            if (max.Y < clipMin.Y || min.Y > clipMax.Y)
            {
                continue;
            }

            var hovered = windowHovered && Contains(min, max, mouse);
            var isLink = piece.Url != null;
            var color = isLink && hovered
                ? colors.LinkHovered
                : piece.Time != null ? colors.Time : colors.Text;
            var textY = min.Y + (piece.Height - piece.Size) * 0.5f;

            if (piece.Time != null)
            {
                DrawTimeChip(drawList, font, piece, min, max, textY, colors);
                if (hovered)
                {
                    ImGui.SetTooltip(describeTime(piece.Time.Value));
                }

                continue;
            }

            if ((piece.Style & RichStyle.Code) != 0)
            {
                drawList.AddRectFilled(new Vector2(min.X - 2f, textY - 1f), new Vector2(max.X + 2f, textY + piece.Size + 1f), ImGui.GetColorU32(colors.CodeBackground), 3f);
            }

            DrawStyledText(drawList, font, piece, new Vector2(min.X, textY), ImGui.GetColorU32(color));

            var lineColor = ImGui.GetColorU32(color);
            var thickness = MathF.Max(1f, piece.Size / 16f);
            // Links are underlined, as on the website.
            if ((piece.Style & RichStyle.Underline) != 0 || isLink)
            {
                var underlineY = textY + piece.Size * 0.95f;
                drawList.AddLine(new Vector2(min.X, underlineY), new Vector2(max.X, underlineY), lineColor, thickness);
            }

            if ((piece.Style & RichStyle.Strikethrough) != 0)
            {
                var strikeY = textY + piece.Size * 0.55f;
                drawList.AddLine(new Vector2(min.X, strikeY), new Vector2(max.X, strikeY), lineColor, thickness);
            }

            if (!hovered)
            {
                continue;
            }

            if (isLink)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip(piece.Url!);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    WebLink.Open(piece.Url);
                }
            }
        }

        foreach (var image in layout.Images)
        {
            var min = origin + new Vector2(image.X, image.Y);
            var max = min + new Vector2(image.Width, image.Height);
            if (max.Y < clipMin.Y || min.Y > clipMax.Y)
            {
                continue;
            }

            var failed = true;
            var texture = loadImages ? _images.Get(image.Url, out failed) : null;
            if (texture != null)
            {
                drawList.AddImageRounded(texture.Handle, min, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF, UiScale.Of(4f));
            }
            else
            {
                drawList.AddRect(min, max, ImGui.GetColorU32(colors.Rule), UiScale.Of(4f));
                var label = !loadImages ? "Image · click to open it in the browser"
                    : failed ? "Image didn't load · click to open it in the browser"
                    : "Loading image...";
                var labelSize = ImGui.CalcTextSize(label);
                drawList.PushClipRect(min, max, true);
                drawList.AddText(new Vector2(MathF.Max(min.X + UiScale.Of(6f), (min.X + max.X - labelSize.X) * 0.5f), (min.Y + max.Y - labelSize.Y) * 0.5f), ImGui.GetColorU32(colors.Muted), label);
                drawList.PopClipRect();
            }

            // Hovering shows where a click goes (the image's link, or the image itself when it has none), and the image whole when previews are on.
            if (windowHovered && Contains(min, max, mouse))
            {
                ImagePreview.Show(texture, image.Link ?? image.Url, _previewImages());
            }
        }

        foreach (var rule in layout.Rules)
        {
            var y = origin.Y + rule.Y;
            drawList.AddLine(new Vector2(origin.X + rule.X, y), new Vector2(origin.X + rule.X + rule.Width, y), ImGui.GetColorU32(colors.Rule), 1f);
        }
    }

    // A rounded chip with a clock icon, like the website's time tags.
    private static void DrawTimeChip(ImDrawListPtr drawList, ImFontPtr font, RichPiece piece, Vector2 min, Vector2 max, float textY, RichTextColors colors)
    {
        var padding = RichTextLayout.TimeChipPadding(piece.Size);
        var chipMin = new Vector2(min.X, textY - piece.Size * 0.12f);
        var chipMax = new Vector2(max.X, textY + piece.Size * 1.12f);
        drawList.AddRectFilled(chipMin, chipMax, ImGui.GetColorU32(colors.TimeBackground), piece.Size * 0.3f);

        var color = ImGui.GetColorU32(colors.Time);
        var iconFont = PluginUiFont.IconFont;
        var icon = FontAwesomeIcon.Clock.ToIconString();
        var iconSize = piece.Size * 0.85f;
        drawList.AddText(iconFont, iconSize, new Vector2(min.X + padding, textY + (piece.Size - iconSize) * 0.5f), color, icon);

        var textPiece = piece with { Style = piece.Style & ~(RichStyle.Underline | RichStyle.Strikethrough) };
        DrawStyledText(drawList, font, textPiece, new Vector2(min.X + padding + RichTextLayout.TimeChipIconWidth(piece.Size), textY), color);
    }

    private RichImageInfo ImageInfo(string url)
    {
        if (!_imagesLoaded)
        {
            return new RichImageInfo(null, Failed: true);
        }

        // Only drawing an image loads it: the layout takes what is known, so images out of view neither download nor keep the cache from evicting them.
        var texture = _images.Peek(url, out var failed);
        return new RichImageInfo(texture == null ? null : new Vector2(texture.Width, texture.Height) * UiScale.Total, failed);
    }

    // Bold: the same glyphs twice, a pixel apart. Italic: the glyph quads just drawn, sheared about the baseline.
    private static unsafe void DrawStyledText(ImDrawListPtr drawList, ImFontPtr font, RichPiece piece, Vector2 position, uint color)
    {
        var firstVertex = drawList.VtxBuffer.Size;
        drawList.AddText(font, piece.Size, position, color, piece.Text);
        if ((piece.Style & RichStyle.Bold) != 0)
        {
            drawList.AddText(font, piece.Size, position + new Vector2(1f, 0f), color, piece.Text);
        }

        if ((piece.Style & RichStyle.Italic) == 0)
        {
            return;
        }

        var baseline = position.Y + piece.Size * 0.8f;
        var vertices = drawList.VtxBuffer;
        for (var i = firstVertex; i < vertices.Size; i++)
        {
            ref var vertex = ref vertices.Ref(i);
            vertex.Pos.X += (baseline - vertex.Pos.Y) * ItalicShear;
        }
    }

    private static bool Contains(Vector2 min, Vector2 max, Vector2 point) =>
        point.X >= min.X && point.X < max.X && point.Y >= min.Y && point.Y < max.Y;

    private sealed class FontMeasurer(ImFontPtr font) : IRichTextMeasurer
    {
        public float Measure(string text, float size) => ImGui.CalcTextSizeA(font, size, float.MaxValue, 0f, text, out _).X;
    }
}
