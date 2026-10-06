using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXIV.Venues.Directory.Infrastructure.RichText;

internal enum RichBlockKind
{
    Paragraph,
    Heading,
    Image,
    Divider,
}

internal enum RichAlign
{
    Left,
    Center,
    Right,
}

[Flags]
internal enum RichStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
    Code = 16,

    // Drawn in capitals; the text keeps its case, so a link's address is unchanged.
    Uppercase = 32,
}

// A stretch of text in one style. A link run carries its URL. A time run carries an instant and no text: it is written out in the viewer's local time, in the plugin's clock format, when it is laid out.
internal sealed record RichRun(string Text, RichStyle Style, string? Url = null, DateTimeOffset? Time = null)
{
    public bool IsTime => Time.HasValue;
}

internal sealed record RichBlock(
    RichBlockKind Kind,
    RichAlign Align,
    IReadOnlyList<RichRun> Runs,
    int HeadingLevel = 0,
    string? ImageUrl = null,
    string? ImageLink = null)
{
    public bool HasText => Runs.Any(run => run.IsTime || !string.IsNullOrWhiteSpace(run.Text));
}

// A formatted description, reduced to what the plugin can draw.
internal sealed record RichDocument(IReadOnlyList<RichBlock> Blocks)
{
    public static RichDocument Empty { get; } = new([]);

    // Space between paragraphs, in lines: none for Partake (its editor spaces things with blank paragraphs), some for FFXIV Venues (its website gives every line paragraph margins).
    public float ParagraphSpacing { get; init; }

    public bool IsEmpty => Blocks.All(block => block.Kind != RichBlockKind.Image && !block.HasText);

    // Returns the text a reader sees, one line per block (empty blocks, images and dividers skipped); used for search and to check the parser against Partake's own plain-text export.
    public string ToPlainText(Func<DateTimeOffset, string> formatTime)
    {
        var builder = new StringBuilder();
        foreach (var block in Blocks)
        {
            if (!block.HasText)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            foreach (var run in block.Runs)
            {
                builder.Append(run.Time is { } time ? formatTime(time) : run.Text);
            }
        }

        return builder.ToString();
    }

    // Returns the same document with every text run passed through a function.
    public RichDocument MapText(Func<string, string> map) =>
        this with
        {
            Blocks = Blocks.Select(block => block with
            {
                Runs = block.Runs.Select(run => run.IsTime ? run : run with { Text = map(run.Text) }).ToArray(),
            }).ToArray(),
        };
}
