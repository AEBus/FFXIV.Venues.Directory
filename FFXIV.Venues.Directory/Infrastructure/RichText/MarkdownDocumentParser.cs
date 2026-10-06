using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FFXIV.Venues.Directory.Infrastructure.RichText;

// Reads an FFXIV Venues description the way ffxivvenues.com shows it: as Markdown, every line its own paragraph.
// Lines: "# " and "## " headings (upper case on the website), "### " to "###### " headings, "- ", "* ", "+ " and "1. " list items, "> " quotes, "---" rules, and Discord's "-# " small text. Inline: **bold**, __bold__, *italic*, _italic_, ~~strikethrough~~, `code`, [label](url) and <url>. Everything else is plain text.
internal static partial class MarkdownDocumentParser
{
    // The website puts paragraph margins between lines; a little over half a line keeps that look in a narrow panel.
    public const float ParagraphSpacing = 0.55f;

    public static RichDocument Parse(IEnumerable<string>? lines)
    {
        var blocks = new List<RichBlock>();
        if (lines == null)
        {
            return RichDocument.Empty;
        }

        foreach (var entry in lines)
        {
            if (entry == null)
            {
                continue;
            }

            foreach (var rawLine in entry.Replace("\r\n", "\n").Split('\n'))
            {
                AddLine(rawLine.TrimEnd(), blocks);
            }
        }

        return new RichDocument(blocks) { ParagraphSpacing = ParagraphSpacing };
    }

    private static void AddLine(string line, List<RichBlock> blocks)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (RuleRegex().IsMatch(trimmed))
        {
            blocks.Add(new RichBlock(RichBlockKind.Divider, RichAlign.Left, []));
            return;
        }

        if (HeadingRegex().Match(trimmed) is { Success: true } heading)
        {
            // The website's h1 and h2 are upper case, h1 a little larger; smaller headings are the text size. The capitals are a drawing style, so the addresses of links in a heading keep their case.
            var hashes = heading.Groups[1].Value.Length;
            var text = heading.Groups[2].Value;
            var level = hashes == 1 ? 3 : 5;
            blocks.Add(new RichBlock(RichBlockKind.Heading, RichAlign.Left, ParseInline(text, hashes <= 2 ? RichStyle.Uppercase : RichStyle.None), level));
            return;
        }

        if (SubtextRegex().Match(trimmed) is { Success: true } subtext)
        {
            blocks.Add(new RichBlock(RichBlockKind.Paragraph, RichAlign.Left, ParseInline(subtext.Groups[1].Value)));
            return;
        }

        if (BulletRegex().Match(trimmed) is { Success: true } bullet)
        {
            var marker = bullet.Groups[1].Value;
            var runs = new List<RichRun> { new(char.IsDigit(marker[0]) ? marker + " " : "• ", RichStyle.None) };
            runs.AddRange(ParseInline(bullet.Groups[2].Value));
            blocks.Add(new RichBlock(RichBlockKind.Paragraph, RichAlign.Left, runs));
            return;
        }

        if (QuoteRegex().Match(trimmed) is { Success: true } quote)
        {
            blocks.Add(new RichBlock(RichBlockKind.Paragraph, RichAlign.Left, ParseInline(quote.Groups[1].Value, RichStyle.Italic)));
            return;
        }

        blocks.Add(new RichBlock(RichBlockKind.Paragraph, RichAlign.Left, ParseInline(trimmed)));
    }

    // A single pass over the line with the styles that are open. A marker only opens when a matching one closes it later on the line, so a lone "*" or "_" stays text; "_" also has to sit at a word edge, so snake_case and URLs keep their underscores.
    public static List<RichRun> ParseInline(string text, RichStyle baseStyle = RichStyle.None)
    {
        var runs = new List<RichRun>();
        var builder = new StringBuilder();
        var style = baseStyle;

        void Flush()
        {
            if (builder.Length > 0)
            {
                runs.Add(new RichRun(builder.ToString(), style));
                builder.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '[' && MarkdownLinkRegex().Match(text, i) is { Success: true } link && link.Index == i)
            {
                Flush();
                foreach (var run in ParseInline(link.Groups[1].Value, style))
                {
                    runs.Add(run with { Url = link.Groups[2].Value });
                }

                i += link.Length;
                continue;
            }

            if (c == '<' && AngleUrlRegex().Match(text, i) is { Success: true } angle && angle.Index == i)
            {
                Flush();
                runs.Add(new RichRun(angle.Groups[1].Value, style, angle.Groups[1].Value));
                i += angle.Length;
                continue;
            }

            if (c == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    Flush();
                    runs.Add(new RichRun(text[(i + 1)..close], style | RichStyle.Code));
                    i = close + 1;
                    continue;
                }
            }

            if (TryToggle(text, ref i, "**", RichStyle.Bold, ref style, Flush) ||
                TryToggle(text, ref i, "__", RichStyle.Bold, ref style, Flush) ||
                TryToggle(text, ref i, "~~", RichStyle.Strikethrough, ref style, Flush) ||
                TryToggle(text, ref i, "*", RichStyle.Italic, ref style, Flush) ||
                TryToggle(text, ref i, "_", RichStyle.Italic, ref style, Flush))
            {
                continue;
            }

            builder.Append(c);
            i++;
        }

        Flush();
        return runs;
    }

    private static bool TryToggle(string text, ref int i, string marker, RichStyle flag, ref RichStyle style, Action flush)
    {
        if (string.CompareOrdinal(text, i, marker, 0, marker.Length) != 0)
        {
            return false;
        }

        var after = i + marker.Length;
        var wordEdgeOnly = marker == "_";
        if ((style & flag) != 0)
        {
            // Closing: right after text, and for "_" not inside a word.
            if (i == 0 || char.IsWhiteSpace(text[i - 1]) || (wordEdgeOnly && after < text.Length && char.IsLetterOrDigit(text[after])))
            {
                return false;
            }

            flush();
            style &= ~flag;
            i = after;
            return true;
        }

        // Opening: right before text, for "_" not inside a word, and only if the same marker closes it later.
        if (after >= text.Length || char.IsWhiteSpace(text[after]) ||
            (wordEdgeOnly && i > 0 && char.IsLetterOrDigit(text[i - 1])) ||
            text.IndexOf(marker, after + 1, StringComparison.Ordinal) < 0)
        {
            return false;
        }

        flush();
        style |= flag;
        i = after;
        return true;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^-#\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex SubtextRegex();

    [GeneratedRegex(@"^([-*+]|\d{1,3}[.)])\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^>\s?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$", RegexOptions.CultureInvariant)]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^\s)]+)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"<(https?://[^\s<>]+)>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AngleUrlRegex();
}
