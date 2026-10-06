using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Text;

// Venue and event text made ready to draw and to search: characters the UI font cannot draw replaced or dropped, lookalike letters made plain for search, Discord markup written out, and descriptions (FFXIV Venues Markdown, Partake's editor documents) turned into rich documents with emoji bullets resolved and plain URLs made links.
internal static class VenueText
{
    // Only real HTML tags; other text in angle brackets, such as Free Company tags, stays text.
    internal static readonly Regex HtmlTagRegex = new(
        @"</?(?:a|b|i|u|s|p|br|hr|em|strong|span|div|font|center|small|big|sub|sup|strike|del|ins|mark|h[1-6]|ul|ol|li|img|blockquote|code|pre)\b[^<>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static readonly Regex HtmlBreakRegex = new(@"<br\s*/?>|</(?:p|div|li)>", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Discord markup pasted into descriptions: timestamps, custom emoji and links in angle brackets.
    internal static readonly Regex DiscordTimestampRegex = new(@"<t:(-?\d{1,12})(?::([tTdDfFR]))?>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Regex DiscordCustomEmojiRegex = new(@"<a?:[A-Za-z0-9_]+:\d+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Regex MarkdownLinkRegex = new(@"\[(.*?)\]\((.*?)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Regex MarkdownStrongRegex = new(@"(\*\*|__|~~)(.*?)\1", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Regex MarkdownEmRegex = new(@"(\*|_)(.*?)\1", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Regex UrlRegex = new(@"https?://[^\s\)\]\}<>\""]+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static readonly Regex RepeatedSpacesRegex = new(@"[ \t]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly Dictionary<int, char> SmallCapsMap = new()
    {
        { 0x1D00, 'A' },
        { 0x0299, 'B' },
        { 0x1D04, 'C' },
        { 0x1D05, 'D' },
        { 0x1D07, 'E' },
        { 0xA730, 'F' },
        { 0x0262, 'G' },
        { 0x029C, 'H' },
        { 0x026A, 'I' },
        { 0x1D0A, 'J' },
        { 0x1D0B, 'K' },
        { 0x029F, 'L' },
        { 0x1D0D, 'M' },
        { 0x0274, 'N' },
        { 0x1D0F, 'O' },
        { 0x1D18, 'P' },
        { 0x01EB, 'Q' },
        { 0x0280, 'R' },
        { 0x1D1B, 'T' },
        { 0x1D1C, 'U' },
        { 0x1D20, 'V' },
        { 0x1D21, 'W' },
        { 0x028F, 'Y' },
        { 0x1D22, 'Z' },
    };

    internal static readonly Dictionary<int, char> SpecialMap = new()
    {
        { 0x210E, 'h' },
        { 0x2113, 'l' },
        { 0x1D70A, 'o' },
        { 0x1D70B, 'o' },
        { 0x1D710, 'u' },
        { 0x15F0, 'M' }, // ᗰ
        { 0x1587, 'R' }, // ᖇ
        { 0x157C, 'H' }, // ᕼ
        { 0x15E9, 'A' }, // ᗩ
    };

    // Stand-ins for symbols the UI font cannot draw, used only when the stand-in itself can be drawn.
    internal static readonly Dictionary<int, char> SymbolFallbacks = new()
    {
        { 0x2728, '★' }, // ✨
        { 0x2726, '★' }, // ✦
        { 0x2727, '☆' }, // ✧
        { 0x2729, '☆' }, // ✩
        { 0x272E, '★' }, // ✮
        { 0x22C6, '★' }, // ⋆
        { 0x2B50, '★' }, // ⭐
        { 0x1F31F, '★' }, // 🌟
        { 0x1F4AB, '★' }, // 💫
        { 0x2764, '♥' }, // ❤
        { 0x2763, '♥' }, // ❣
        { 0x1F48B, '♥' }, // 💋
        { 0x1F495, '♥' }, // 💕
        { 0x1F496, '♥' }, // 💖
        { 0x1F497, '♥' }, // 💗
        { 0x1F499, '♥' }, // 💙
        { 0x1F49A, '♥' }, // 💚
        { 0x1F49B, '♥' }, // 💛
        { 0x1F49C, '♥' }, // 💜
        { 0x1F5A4, '♥' }, // 🖤
        { 0x1F9E1, '♥' }, // 🧡
        { 0x1F90D, '♥' }, // 🤍
        { 0x1F3B5, '♪' }, // 🎵
        { 0x1F3B6, '♪' }, // 🎶
        { 0x1F3A7, '♪' }, // 🎧
        { 0x1F3A4, '♪' }, // 🎤
        { 0x27A1, '→' }, // ➡
        { 0x2794, '→' }, // ➔
        { 0x25C8, '◆' }, // ◈
        { 0x1F4CD, '•' }, // 📍
    };

    internal static readonly HashSet<int> InvisibleRunes = [0xFE0F, 0x200B, 0x200D, 0x2060];

    // Display text keeps every character the UI font can draw (see PluginUiFont) and replaces only the rest, so nothing shows as a fallback box. Descriptions mark a line-leading emoji for ResolveEmojiBullets.
    internal static string NormalizeDisplayText(string text, bool leadingEmojiAsBullet = false) =>
        NormalizeText(text, UiGlyphs.Current, forSearch: false, leadingEmojiAsBullet);

    internal static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = NormalizeText(text, UiGlyphs.Current, forSearch: true, leadingEmojiAsBullet: false);
        normalized = HtmlTagRegex.Replace(normalized, " ");
        normalized = MarkdownLinkRegex.Replace(normalized, "$1");
        normalized = MarkdownStrongRegex.Replace(normalized, "$2");
        normalized = MarkdownEmRegex.Replace(normalized, "$2");
        normalized = normalized.Replace("`", string.Empty);
        normalized = MultiWhitespaceRegex.Replace(normalized, " ").Trim();
        return normalized;
    }

    internal static string NormalizeText(string text, UiGlyphs.Snapshot? glyphs, bool forSearch, bool leadingEmojiAsBullet)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var atLineStart = true;
        foreach (var rune in text.Replace("\r\n", "\n").EnumerateRunes())
        {
            var value = rune.Value;
            if (value == '\n')
            {
                builder.Append('\n');
                atLineStart = true;
                continue;
            }

            var replacement = ReplacementFor(value, glyphs, forSearch);
            if (replacement == null)
            {
                builder.Append(rune.ToString());
                atLineStart &= Rune.IsWhiteSpace(rune);
            }
            else if (replacement.Length > 0)
            {
                builder.Append(replacement);
                atLineStart = false;
            }
            else if (leadingEmojiAsBullet && atLineStart && IsEmoji(value))
            {
                builder.Append(EmojiBulletMark);
                atLineStart = false;
            }
        }

        return RepeatedSpacesRegex.Replace(builder.ToString(), " ").Trim();
    }

    // A noncharacter: it never occurs in venue text, so it can stand in for a line-leading emoji until the lines around it are known.
    internal const char EmojiBulletMark = '\uFDD0';

    // A line-leading emoji the font cannot draw is a bullet when it marks a list (the line right above or below starts with one too; a blank line ends a list) and is dropped when it decorates a single line such as a heading.
    internal static string ResolveEmojiBullets(string text)
    {
        if (!text.Contains(EmojiBulletMark))
        {
            return text;
        }

        var lines = text.Split('\n');
        var marked = lines.Select(line => line.TrimStart().StartsWith(EmojiBulletMark)).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!marked[i])
            {
                continue;
            }

            var inList = (i > 0 && marked[i - 1]) || (i + 1 < lines.Length && marked[i + 1]);
            var rest = lines[i].TrimStart()[1..].TrimStart();
            lines[i] = inList ? "• " + rest : rest;
        }

        return string.Join('\n', lines);
    }

    // Returns what one character becomes: null keeps it, an empty string drops it.
    internal static string? ReplacementFor(int value, UiGlyphs.Snapshot? glyphs, bool forSearch)
    {
        if (InvisibleRunes.Contains(value))
        {
            return string.Empty;
        }

        // Search always matches plain letters, so stylized lookalike letters are found by their plain spelling.
        if (TryGetPlainLetter(value, out var plain) && (forSearch || !CanDraw(glyphs, value)))
        {
            return plain.ToString();
        }

        if (forSearch)
        {
            return IsEmoji(value) ? string.Empty : null;
        }

        if (CanDraw(glyphs, value))
        {
            return null;
        }

        if (SymbolFallbacks.TryGetValue(value, out var fallback) && CanDraw(glyphs, fallback))
        {
            return fallback.ToString();
        }

        // A letter the font lacks is kept so words stay whole; anything else it lacks, decorative characters included, is dropped.
        return !IsEmoji(value) && Rune.IsLetter(new Rune(value)) ? null : string.Empty;
    }

    // Before the UI font is built, the characters known to be missing from the game font count as undrawable.
    internal static bool CanDraw(UiGlyphs.Snapshot? glyphs, int value) =>
        glyphs?.CanDraw(value) ?? (value < 0x10000 && !SymbolFallbacks.ContainsKey(value) && !TryGetPlainLetter(value, out _));

    internal static bool IsEmoji(int value) =>
        value is >= 0x1F000 and <= 0x1FAFF;

    // Lookalike letters from other scripts and the Mathematical Alphanumeric Symbols block (bold, italic, script, fraktur, double-struck, sans-serif and monospace A-Z a-z, then 0-9), which no ImGui font can draw.
    internal static bool TryGetPlainLetter(int value, out char plain)
    {
        if (SmallCapsMap.TryGetValue(value, out plain) || SpecialMap.TryGetValue(value, out plain))
        {
            return true;
        }

        if (value is >= 0x1D400 and <= 0x1D6A3)
        {
            var offset = (value - 0x1D400) % 52;
            plain = offset < 26 ? (char)('A' + offset) : (char)('a' + offset - 26);
            return true;
        }

        if (value is >= 0x1D7CE and <= 0x1D7FF)
        {
            plain = (char)('0' + ((value - 0x1D7CE) % 10));
            return true;
        }

        plain = default;
        return false;
    }

    // A Discord timestamp in the reader's timezone, like Discord shows it; "R" (relative) would go stale, so it gets the full date too.
    internal static string FormatDiscordTimestamp(Match match)
    {
        if (!long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ||
            seconds is < -62135596800 or > 253402300799)
        {
            return match.Value;
        }

        var at = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
        return match.Groups[2].Value switch
        {
            "t" => FormatShortTime(at),
            "T" => at.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "d" => at.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            "D" => at.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
            _ => $"{at.ToString("ddd, MMM d", CultureInfo.InvariantCulture)} {FormatShortTime(at)}",
        };
    }

    internal static string JoinNonEmptyLines(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    internal static string BuildPreparedSearchText(string displayName, string description, IEnumerable<string> tags)
    {
        var builder = new StringBuilder(displayName.Length + description.Length + 64);
        builder.Append(displayName);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append('\n');
            builder.Append(description);
        }

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            builder.Append('\n');
            builder.Append(tag);
        }

        return NormalizeForSearch(builder.ToString());
    }

    internal static RichDocument PreparePartakeDocument(string? raw) => PrepareRichDocument(SlateDocumentParser.Parse(raw));

    // Text without markup, such as a Party Finder ad: one paragraph per line.
    internal static RichDocument PreparePlainTextDocument(string? text) =>
        string.IsNullOrEmpty(text) ? RichDocument.Empty : PrepareRichDocument(SlateDocumentParser.FromPlainText(text));

    // An FFXIV Venues description: the website reads it as Markdown, one paragraph per line. HTML the owner pasted is not drawn by the website either: line breaks become lines, other tags go.
    internal static RichDocument PrepareVenueDescription(IEnumerable<string>? lines)
    {
        if (lines == null)
        {
            return RichDocument.Empty;
        }

        var cleaned = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => HtmlTagRegex.Replace(HtmlBreakRegex.Replace(line, "\n"), string.Empty));
        return PrepareRichDocument(MarkdownDocumentParser.Parse(cleaned));
    }

    // The text treatment every description gets once parsed: Discord markup, the UI font's symbol fallbacks, leading emoji as bullets, plain-text URLs as links.
    internal static RichDocument PrepareRichDocument(RichDocument parsed)
    {
        var blocks = new RichBlock[parsed.Blocks.Count];
        var marked = new bool[blocks.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = PreparePartakeBlock(parsed.Blocks[i]);
            marked[i] = StartsWithEmojiBullet(blocks[i]);
        }

        // Like venue descriptions: a leading emoji the font cannot draw is a bullet when the paragraph next to it starts with one too, and is dropped otherwise.
        for (var i = 0; i < blocks.Length; i++)
        {
            if (marked[i])
            {
                var inList = (i > 0 && marked[i - 1]) || (i + 1 < blocks.Length && marked[i + 1]);
                blocks[i] = ResolveBlockEmojiBullet(blocks[i], inList);
            }
        }

        return parsed with { Blocks = blocks };
    }

    internal static RichBlock PreparePartakeBlock(RichBlock block)
    {
        if (block.Runs.Count == 0)
        {
            return block;
        }

        var runs = new List<RichRun>(block.Runs.Count);
        var atBlockStart = true;
        for (var i = 0; i < block.Runs.Count; i++)
        {
            var run = block.Runs[i];
            if (run.IsTime)
            {
                runs.Add(run);
                atBlockStart = false;
                continue;
            }

            var text = DiscordTimestampRegex.Replace(run.Text, FormatDiscordTimestamp);
            text = DiscordCustomEmojiRegex.Replace(text, string.Empty);

            // The normalizer trims, but a space at the edge of a run separates it from the next run in another style.
            var leading = text.Length - text.TrimStart(' ').Length;
            var trailing = text.Length - text.TrimEnd(' ').Length;
            var core = NormalizeDisplayText(text.Trim(' '), leadingEmojiAsBullet: atBlockStart && block.Kind == RichBlockKind.Paragraph);
            text = leading >= text.Length
                ? (atBlockStart ? string.Empty : " ")
                : new string(' ', Math.Min(leading, 1)) + core + new string(' ', Math.Min(trailing, 1));
            if (atBlockStart)
            {
                // Only the block's first line decides about a bullet here; lines after a soft break inside the run are resolved within the run.
                var firstBreak = text.IndexOf('\n');
                if (firstBreak >= 0)
                {
                    text = text[..(firstBreak + 1)] + ResolveEmojiBullets(text[(firstBreak + 1)..]);
                }
            }

            AddLinkified(runs, run with { Text = text });
            if (!string.IsNullOrWhiteSpace(run.Text))
            {
                atBlockStart = false;
            }
        }

        return block with { Runs = runs.ToArray() };
    }

    // Partake's website makes any URL typed as plain text a link, with or without a link element around it.
    internal static void AddLinkified(List<RichRun> runs, RichRun run)
    {
        if (run.Url != null || !run.Text.Contains("://", StringComparison.Ordinal))
        {
            runs.Add(run);
            return;
        }

        var at = 0;
        foreach (Match match in UrlRegex.Matches(run.Text))
        {
            // Sentence punctuation right after a URL is not part of it.
            var url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '\'', '"');
            if (match.Index > at)
            {
                runs.Add(run with { Text = run.Text[at..match.Index] });
            }

            runs.Add(run with { Text = url, Url = url });
            at = match.Index + url.Length;
        }

        if (at < run.Text.Length)
        {
            runs.Add(run with { Text = run.Text[at..] });
        }
    }

    internal static bool StartsWithEmojiBullet(RichBlock block)
    {
        foreach (var run in block.Runs)
        {
            if (run.IsTime)
            {
                return false;
            }

            var text = run.Text.TrimStart();
            if (text.Length > 0)
            {
                return text[0] == EmojiBulletMark;
            }
        }

        return false;
    }

    internal static RichBlock ResolveBlockEmojiBullet(RichBlock block, bool inList)
    {
        var runs = new RichRun[block.Runs.Count];
        var resolved = false;
        for (var i = 0; i < runs.Length; i++)
        {
            var run = block.Runs[i];
            var trimmed = run.Text.TrimStart();
            if (!resolved && trimmed.Length > 0 && trimmed[0] == EmojiBulletMark)
            {
                var rest = trimmed[1..].TrimStart();
                runs[i] = run with { Text = inList ? "• " + rest : rest };
                resolved = true;
            }
            else
            {
                runs[i] = run;
            }
        }

        return block with { Runs = runs };
    }
}
