using System;
using System.Collections.Generic;
using System.Numerics;

namespace FFXIV.Venues.Directory.Infrastructure.RichText;

internal interface IRichTextMeasurer
{
    float Measure(string text, float size);
}

// One drawable stretch of text: a word or several words in the same style on one line.
internal readonly record struct RichPiece(
    float X,
    float Y,
    float Width,
    float Height,
    float Size,
    string Text,
    RichStyle Style,
    string? Url,
    DateTimeOffset? Time);

internal readonly record struct RichImageBox(float X, float Y, float Width, float Height, string Url, string? Link, bool Failed);

// What is known about a picture: its natural size once loaded, or that it will not load.
internal readonly record struct RichImageInfo(Vector2? Size, bool Failed);

internal readonly record struct RichRule(float X, float Y, float Width);

// Where everything in a RichDocument goes at a given width. Built once per document, width and text version, and drawn every frame; positions are relative to the top-left corner of the text area.
internal sealed class RichTextLayout
{
    public const float LineSpacing = 1.25f;
    private const float EmptyParagraphLines = 0.5f;
    private const int MaxEmptyParagraphsInARow = 2;

    private static readonly float[] HeadingScales = [1f, 1.55f, 1.35f, 1.2f, 1.1f, 1f, 0.95f];

    private RichTextLayout(float width) => Width = width;

    public float Width { get; }
    public float Height { get; private set; }
    public List<RichPiece> Pieces { get; } = [];
    public List<RichImageBox> Images { get; } = [];
    public List<RichRule> Rules { get; } = [];

    public static float HeadingScale(int level) => HeadingScales[Math.Clamp(level, 0, HeadingScales.Length - 1)];

    // A time tag is drawn as a chip: padding, a clock icon, the time, padding.
    public static float TimeChipPadding(float size) => size * 0.35f;

    public static float TimeChipIconWidth(float size) => size * 1.1f;

    // imageInfo: the natural size of a loaded image, nothing while it loads, or that it failed.
    public static RichTextLayout Build(
        RichDocument document,
        float width,
        float baseSize,
        IRichTextMeasurer measurer,
        Func<DateTimeOffset, string> formatTime,
        Func<string, RichImageInfo> imageInfo)
    {
        var layout = new RichTextLayout(width);
        var builder = new LineBuilder(layout, width, measurer);
        var y = 0f;
        var emptyRun = 0;
        for (var i = 0; i < document.Blocks.Count; i++)
        {
            var block = document.Blocks[i];
            switch (block.Kind)
            {
                case RichBlockKind.Image:
                    emptyRun = 0;
                    y = AddImage(layout, block, width, baseSize, y, imageInfo);
                    continue;

                case RichBlockKind.Divider:
                    emptyRun = 0;
                    y += baseSize * 0.5f;
                    layout.Rules.Add(new RichRule(0f, y, width));
                    y += baseSize * 0.5f;
                    continue;
            }

            var size = block.Kind == RichBlockKind.Heading ? baseSize * HeadingScale(block.HeadingLevel) : baseSize;
            if (!block.HasText)
            {
                // Blank paragraphs are how the editor spaces things out; a long stack of them is capped.
                if (++emptyRun <= MaxEmptyParagraphsInARow)
                {
                    y += size * LineSpacing * EmptyParagraphLines;
                }

                continue;
            }

            emptyRun = 0;
            if (y > 0f)
            {
                // A heading gets at least the space a paragraph gets, and a little more.
                y += size * LineSpacing * document.ParagraphSpacing + (block.Kind == RichBlockKind.Heading ? size * 0.3f : 0f);
            }

            y = builder.Layout(block, size, y, formatTime);
            if (block.Kind == RichBlockKind.Heading)
            {
                y += size * 0.15f;
            }
        }

        layout.Height = y;
        return layout;
    }

    private static float AddImage(RichTextLayout layout, RichBlock block, float width, float baseSize, float y, Func<string, RichImageInfo> imageInfo)
    {
        var url = block.ImageUrl!;
        var info = imageInfo(url);
        float boxWidth;
        float boxHeight;
        if (info.Size is { X: > 0f, Y: > 0f } size)
        {
            boxWidth = MathF.Min(width, size.X);
            boxHeight = boxWidth * size.Y / size.X;
        }
        else if (info.Failed)
        {
            // A slim bar that says so and opens the picture in the browser.
            boxWidth = width;
            boxHeight = baseSize * 2.2f;
        }
        else
        {
            // Not loaded yet: a placeholder the width of the text, about as tall as a typical flyer banner.
            boxWidth = width;
            boxHeight = MathF.Min(width * 0.5f, baseSize * 12f);
        }

        var x = block.Align switch
        {
            RichAlign.Center => (width - boxWidth) * 0.5f,
            RichAlign.Right => width - boxWidth,
            _ => 0f,
        };
        y += baseSize * 0.25f;
        layout.Images.Add(new RichImageBox(x, y, boxWidth, boxHeight, url, block.ImageLink, info.Failed && info.Size == null));
        return y + boxHeight + baseSize * 0.25f;
    }

    // Word-wraps the runs of one block into lines, aligned as the block asks.
    private sealed class LineBuilder
    {
        private readonly RichTextLayout _layout;
        private readonly float _width;
        private readonly IRichTextMeasurer _measurer;
        private readonly List<RichPiece> _line = [];
        private float _x;
        private bool _afterWrap;

        public LineBuilder(RichTextLayout layout, float width, IRichTextMeasurer measurer)
        {
            _layout = layout;
            _width = width;
            _measurer = measurer;
        }

        public float Layout(RichBlock block, float size, float y, Func<DateTimeOffset, string> formatTime)
        {
            var style = block.Kind == RichBlockKind.Heading ? RichStyle.Bold : RichStyle.None;
            _line.Clear();
            _x = 0f;
            foreach (var run in block.Runs)
            {
                var runStyle = run.Style | style;
                if (run.Time is { } time)
                {
                    // One chip, never broken across lines.
                    var timeText = formatTime(time);
                    var chipWidth = MeasureStyled(timeText, size, runStyle) + TimeChipIconWidth(size) + TimeChipPadding(size) * 2f;
                    if (_x + chipWidth > _width && _line.Count > 0)
                    {
                        y = Flush(y, size, block.Align);
                        _afterWrap = true;
                    }

                    Append(timeText, chipWidth, size, runStyle, run);
                    continue;
                }

                var text = (runStyle & RichStyle.Uppercase) != 0 ? run.Text.ToUpperInvariant() : run.Text;
                var start = 0;
                for (var i = 0; i <= text.Length; i++)
                {
                    var atEnd = i == text.Length;
                    var c = atEnd ? '\0' : text[i];
                    var isBreak = c == '\n';
                    var isSpace = c == ' ' || c == '\t';
                    if (!atEnd && !isBreak && !isSpace)
                    {
                        continue;
                    }

                    if (i > start)
                    {
                        y = AddWord(text[start..i], size, runStyle, run, y, block.Align);
                    }

                    if (isSpace)
                    {
                        AddSpace(c == '\t' ? "    " : " ", size, runStyle, run);
                    }
                    else if (isBreak)
                    {
                        y = Flush(y, size, block.Align);
                    }

                    start = i + 1;
                }
            }

            return Flush(y, size, block.Align);
        }

        private float AddWord(string word, float size, RichStyle style, RichRun run, float y, RichAlign align)
        {
            var width = MeasureStyled(word, size, style);
            if (_x + width > _width && _line.Count > 0)
            {
                y = Flush(y, size, align);
                _afterWrap = true;
            }

            // A word wider than the whole line (a long link) is broken wherever it has to be.
            while (width > _width && word.Length > 1)
            {
                var fit = FitPrefix(word, size, style);
                Append(word[..fit], MeasureStyled(word[..fit], size, style), size, style, run);
                y = Flush(y, size, align);
                _afterWrap = true;
                word = word[fit..];
                width = MeasureStyled(word, size, style);
            }

            Append(word, width, size, style, run);
            return y;
        }

        private void AddSpace(string space, float size, RichStyle style, RichRun run)
        {
            // Spaces at the start of a wrapped line are dropped; an explicit line start keeps them (indentation).
            if (_line.Count == 0 && _afterWrap)
            {
                return;
            }

            Append(space, _measurer.Measure(space, size), size, style, run);
        }

        private void Append(string text, float width, float size, RichStyle style, RichRun run)
        {
            _afterWrap = false;
            if (_line.Count > 0)
            {
                var last = _line[^1];
                if (last.Style == style && last.Size == size && last.Url == run.Url && last.Time == run.Time)
                {
                    _line[^1] = last with { Text = last.Text + text, Width = last.Width + width };
                    _x += width;
                    return;
                }
            }

            _line.Add(new RichPiece(_x, 0f, width, 0f, size, text, style, run.Url, run.Time));
            _x += width;
        }

        private float Flush(float y, float blockSize, RichAlign align)
        {
            // Trailing spaces take no room at the end of a line.
            while (_line.Count > 0 && _line[^1].Text.AsSpan().Trim(" \t").IsEmpty)
            {
                _line.RemoveAt(_line.Count - 1);
            }

            if (_line.Count > 0 && _line[^1].Text.EndsWith(' '))
            {
                var last = _line[^1];
                var trimmed = last.Text.TrimEnd(' ');
                _line[^1] = last with { Text = trimmed, Width = MeasureStyled(trimmed, last.Size, last.Style) };
            }

            var lineSize = blockSize;
            foreach (var piece in _line)
            {
                lineSize = MathF.Max(lineSize, piece.Size);
            }

            var lineHeight = lineSize * LineSpacing;
            if (_line.Count > 0)
            {
                var lineWidth = _line[^1].X + _line[^1].Width;
                var offset = align switch
                {
                    RichAlign.Center => MathF.Max(0f, (_width - lineWidth) * 0.5f),
                    RichAlign.Right => MathF.Max(0f, _width - lineWidth),
                    _ => 0f,
                };
                foreach (var piece in _line)
                {
                    var pieceHeight = piece.Size * LineSpacing;
                    _layout.Pieces.Add(piece with { X = piece.X + offset, Y = y + lineHeight - pieceHeight, Height = pieceHeight });
                }
            }

            _line.Clear();
            _x = 0f;
            _afterWrap = false;
            return y + lineHeight;
        }

        private int FitPrefix(string word, float size, RichStyle style)
        {
            var fit = 1;
            while (fit < word.Length && MeasureStyled(word[..(fit + 1)], size, style) <= _width)
            {
                fit++;
            }

            return fit;
        }

        // Bold is drawn twice, one pixel apart, so it is a pixel wider.
        private float MeasureStyled(string text, float size, RichStyle style) =>
            _measurer.Measure(text, size) + ((style & RichStyle.Bold) != 0 ? 1f : 0f);
    }
}
