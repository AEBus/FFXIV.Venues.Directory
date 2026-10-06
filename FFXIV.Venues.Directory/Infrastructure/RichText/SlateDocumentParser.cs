using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using FFXIV.Venues.Directory.Infrastructure.Net;

namespace FFXIV.Venues.Directory.Infrastructure.RichText;

// Reads Partake's RAW event description: the Slate editor document the website renders from, a JSON array of block nodes. Partake's other description formats each lose something (MARKDOWN drops paragraphs and links, HTML can come back as escaped JSON, PLAIN_TEXT has no structure), so this is the only one read.
//
// Blocks: paragraph and heading-one..heading-six (with "align"), image ("url", optional "linkUrl"), divider.
// Inlines: text leaves (bold, italic, underline, code, strikethrough), link ("url"), time-tag ("date").
// Anything else is kept as text: unknown blocks become paragraphs, unknown inlines give their text. Links and images count only with web addresses: a link to anything else is plain text, an image without one is left out.
internal static class SlateDocumentParser
{
    public static RichDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return RichDocument.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var blocks = new List<RichBlock>();
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in root.EnumerateArray())
                {
                    AddBlock(node, blocks, RichAlign.Left);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                AddBlock(root, blocks, RichAlign.Left);
            }

            return new RichDocument(blocks);
        }
        catch (JsonException)
        {
            // Not an editor document (an old plain-text description): one paragraph per line.
            return FromPlainText(json);
        }
    }

    public static RichDocument FromPlainText(string text)
    {
        var blocks = new List<RichBlock>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            blocks.Add(new RichBlock(RichBlockKind.Paragraph, RichAlign.Left, line.Length == 0 ? [] : [new RichRun(line, RichStyle.None)]));
        }

        return new RichDocument(blocks);
    }

    private static void AddBlock(JsonElement node, List<RichBlock> blocks, RichAlign inheritedAlign)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = GetString(node, "type");
        var align = ParseAlign(GetString(node, "align")) ?? inheritedAlign;
        switch (type)
        {
            case "image":
                var url = GetString(node, "url");
                if (WebLink.IsWebAddress(url))
                {
                    blocks.Add(new RichBlock(RichBlockKind.Image, align, [], ImageUrl: url, ImageLink: WebAddressOrNull(GetString(node, "linkUrl"))));
                }

                return;

            case "divider":
                blocks.Add(new RichBlock(RichBlockKind.Divider, align, []));
                return;

            case "bulleted-list":
            case "numbered-list":
                var number = 1;
                foreach (var item in Children(node))
                {
                    var marker = type == "numbered-list" ? $"{number++}. " : "• ";
                    var itemRuns = new List<RichRun> { new(marker, RichStyle.None) };
                    CollectRuns(item, itemRuns, RichStyle.None, null);
                    blocks.Add(new RichBlock(RichBlockKind.Paragraph, ParseAlign(GetString(item, "align")) ?? align, itemRuns));
                }

                return;

            case "block-quote":
                foreach (var child in Children(node))
                {
                    if (IsBlock(child))
                    {
                        AddBlock(child, blocks, align);
                    }
                }

                return;
        }

        var level = HeadingLevel(type);
        var runs = new List<RichRun>();
        if (IsTextLeaf(node))
        {
            AddLeaf(node, runs, RichStyle.None, null);
        }
        else
        {
            CollectRuns(node, runs, RichStyle.None, null);
        }

        blocks.Add(new RichBlock(level > 0 ? RichBlockKind.Heading : RichBlockKind.Paragraph, align, runs, level));
    }

    private static void CollectRuns(JsonElement node, List<RichRun> runs, RichStyle inherited, string? url)
    {
        foreach (var child in Children(node))
        {
            if (child.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (GetString(child, "type"))
            {
                case "link":
                    // A link with no text (the editor leaves these behind) draws nothing, as on the website. A link to anything but a web address stays plain text.
                    CollectRuns(child, runs, inherited, WebAddressOrNull(GetString(child, "url")) ?? url);
                    continue;

                case "time-tag":
                    if (TryGetDate(child, out var date))
                    {
                        runs.Add(new RichRun(string.Empty, inherited | LeafStyle(child), url, date));
                    }

                    continue;
            }

            // Leaves sometimes carry a block type ("type": "paragraph") next to their text; the text wins.
            if (IsTextLeaf(child))
            {
                AddLeaf(child, runs, inherited, url);
                continue;
            }

            CollectRuns(child, runs, inherited, url);
        }
    }

    private static void AddLeaf(JsonElement leaf, List<RichRun> runs, RichStyle inherited, string? url)
    {
        var text = leaf.GetProperty("text").GetString();
        if (!string.IsNullOrEmpty(text))
        {
            runs.Add(new RichRun(text, inherited | LeafStyle(leaf), url));
        }
    }

    private static RichStyle LeafStyle(JsonElement leaf)
    {
        var style = RichStyle.None;
        if (IsTrue(leaf, "bold"))
        {
            style |= RichStyle.Bold;
        }

        if (IsTrue(leaf, "italic"))
        {
            style |= RichStyle.Italic;
        }

        if (IsTrue(leaf, "underline"))
        {
            style |= RichStyle.Underline;
        }

        if (IsTrue(leaf, "strikethrough"))
        {
            style |= RichStyle.Strikethrough;
        }

        if (IsTrue(leaf, "code"))
        {
            style |= RichStyle.Code;
        }

        return style;
    }

    private static int HeadingLevel(string? type) => type switch
    {
        "heading-one" => 1,
        "heading-two" => 2,
        "heading-three" => 3,
        "heading-four" => 4,
        "heading-five" => 5,
        "heading-six" => 6,
        _ => 0,
    };

    private static RichAlign? ParseAlign(string? align) => align switch
    {
        "center" => RichAlign.Center,
        "right" => RichAlign.Right,
        "left" or "justify" => RichAlign.Left,
        _ => null,
    };

    private static bool IsBlock(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && !IsTextLeaf(node) && GetString(node, "type") is not ("link" or "time-tag");

    private static bool IsTextLeaf(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty("text", out var text) &&
        text.ValueKind == JsonValueKind.String;

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty("children", out var children) &&
        children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray()
            : [];

    private static bool TryGetDate(JsonElement node, out DateTimeOffset date)
    {
        date = default;
        var text = GetString(node, "date");
        return text != null &&
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }

    private static string? GetString(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsTrue(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? WebAddressOrNull(string? value) => WebLink.IsWebAddress(value) ? value : null;
}
