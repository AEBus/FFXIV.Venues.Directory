using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Infrastructure.Ui;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private static Vector4 ResolveTextColor(Vector4? color = null) => color ?? UiStyle.BodyText;

    private static uint ResolveTextColorU32(Vector4? color = null) => ImGui.GetColorU32(ResolveTextColor(color));

    private static void DrawVerticalRhythm(float height = 1f)
    {
        ImGuiHelpers.ScaledDummy(0f, UiStyle.CardSpacingUnits * height * UiScale.Factor);
    }

    private static void DrawExactVerticalGap(float gap)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y + gap);
    }

    private static void AlignCursorRight(float itemWidth)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - itemWidth));
    }

    private static void DrawCenteredDualButtonRow(
        float spacing,
        float targetOuterInset,
        Action<float> drawLeft,
        Action<float> drawRight)
    {
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X);
        var buttonWidth = MathF.Max(0f, (availableWidth - spacing - targetOuterInset * 2f) * 0.5f);
        var totalWidth = buttonWidth * 2f + spacing;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (availableWidth - totalWidth) * 0.5f));
        drawLeft(buttonWidth);
        ImGui.SameLine(0f, spacing);
        drawRight(buttonWidth);
    }

    private static void DrawSectionLabel(string text, Vector4? color = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(color ?? UiStyle.BodyMutedText, text);
    }

    private static void DrawSectionLabel(FontAwesomeIcon icon, string text, Vector4? color = null)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color ?? UiStyle.BodyMutedText))
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushFont(PluginUiFont.IconFont))
            {
                ImGui.TextUnformatted(IconText(icon));
            }

            ImGui.SameLine(0f, UiStyle.InlineSpacing);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
        }
    }

    private static void DrawSectionHeader(string text, Vector4? color = null)
    {
        DrawSectionLabel(text, color ?? UiStyle.SectionHeaderText);
        var itemMax = ImGui.GetItemRectMax();
        var lineStartX = itemMax.X + UiStyle.InlineSpacing;
        var lineY = ImGui.GetItemRectMin().Y + ImGui.GetTextLineHeight() * 0.5f;
        var lineEndX = ImGui.GetCursorScreenPos().X + MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(lineStartX, lineY),
            new Vector2(lineEndX, lineY),
            ImGui.GetColorU32(ImGuiCol.Border));
    }

    private static void DrawSectionHeader(FontAwesomeIcon icon, string text, Vector4? color = null)
    {
        DrawSectionLabel(icon, text, color ?? UiStyle.SectionHeaderText);
        var itemMax = ImGui.GetItemRectMax();
        var lineStartX = itemMax.X + UiStyle.InlineSpacing;
        var lineY = ImGui.GetItemRectMin().Y + ImGui.GetTextLineHeight() * 0.5f;
        var lineEndX = ImGui.GetCursorScreenPos().X + MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(lineStartX, lineY),
            new Vector2(lineEndX, lineY),
            ImGui.GetColorU32(ImGuiCol.Border));
    }

    private static void DrawMutedText(string text)
    {
        ImGui.TextColored(UiStyle.BodyMutedText, text);
    }

    private static void DrawText(string text, Vector4? color = null)
    {
        ImGui.TextColored(ResolveTextColor(color), text);
    }

    private static void DrawTextWrapped(string text, Vector4? color = null)
    {
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, ResolveTextColor(color));
        ImGui.TextWrapped(text);
    }

    private static void DrawBodyTextWrapped(string text)
    {
        DrawTextWrapped(text, UiStyle.BodyText);
    }

    // A picture with rounded corners and a hairline border, like the cards around it.
    private static void DrawRoundedImage(ImTextureID texture, Vector2 size)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImageRounded(texture, min, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF, UiStyle.CardRounding);
        drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), UiStyle.CardRounding);
    }

    private static void DrawSelectedRowAccent(Vector2 min, Vector2 max)
    {
        ImGui.GetWindowDrawList().AddRectFilled(min, new Vector2(min.X + Scale(3f), max.Y), ImGui.GetColorU32(UiStyle.SelectedRowAccent));
    }

    // Returns the longest start of the text that fits the width, with an ellipsis when something was cut.
    private static string FitToWidth(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
        {
            return text;
        }

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(string.Concat(text.AsSpan(0, mid), "…")).X <= width)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return string.Concat(text.AsSpan(0, low).TrimEnd(), "…");
    }

    // A tab in the top bar: text and an optional count in a pill, underlined while active.
    private static bool DrawTab(string id, FontAwesomeIcon icon, string label, string? badge, bool badgePositive, bool active, float height)
    {
        var padding = Scale(10f);
        var gap = UiStyle.InlineSpacing;
        var iconText = IconText(icon);
        Vector2 iconSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var labelSize = ImGui.CalcTextSize(label);
        var badgePadding = UiScale.Vector(7f, 1f);
        var badgeSize = badge == null ? Vector2.Zero : ImGui.CalcTextSize(badge) + badgePadding * 2f;
        var width = padding * 2f + iconSize.X + gap + labelSize.X + (badge == null ? 0f : gap + badgeSize.X);

        using var tabId = ImRaii.PushId(id);
        var pressed = ImGui.InvisibleButton("##Tab", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        if (hovered && !active)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(UiStyle.SecondaryButtonBackground), UiStyle.CardRounding);
        }

        var color = ImGui.GetColorU32(active ? UiStyle.BodyStrongText : hovered ? UiStyle.BodyText : UiStyle.BodyMutedText);
        var x = min.X + padding;
        var centerY = (min.Y + max.Y) * 0.5f;
        drawList.AddText(PluginUiFont.IconFont, iconSize.Y, new Vector2(x, centerY - iconSize.Y * 0.5f), color, iconText);
        x += iconSize.X + gap;
        drawList.AddText(new Vector2(x, centerY - labelSize.Y * 0.5f), color, label);
        x += labelSize.X + gap;
        if (badge != null)
        {
            var badgeMin = new Vector2(x, centerY - badgeSize.Y * 0.5f);
            var badgeBackground = badgePositive ? UiStyle.PositiveChipBackground : UiStyle.TagChipBackground;
            drawList.AddRectFilled(badgeMin, badgeMin + badgeSize, ImGui.GetColorU32(badgeBackground), badgeSize.Y * 0.5f);
            drawList.AddText(badgeMin + badgePadding, ImGui.GetColorU32(badgePositive ? UiStyle.PositiveText : UiStyle.BodyMutedText), badge);
        }

        if (active)
        {
            drawList.AddRectFilled(new Vector2(min.X + padding, max.Y - Scale(2f)), new Vector2(max.X - padding, max.Y), ImGui.GetColorU32(UiStyle.TabUnderline), Scale(1f));
        }

        return pressed;
    }

    private static void DrawDisplayTitleText(string text, float? wrapPos = null)
    {
        ImGui.SetWindowFontScale(1.4f);
        try
        {
            using var textColor = ImRaii.PushColor(ImGuiCol.Text, UiStyle.DisplayTitleText);
            using (ImRaii.TextWrapPos(wrapPos ?? 0f, wrapPos.HasValue))
            {
                ImGui.TextUnformatted(text);
            }
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }

    private static bool DrawActionButton(FontAwesomeIcon icon, string label, UiButtonTone tone, float? forcedWidth = null, float textOffsetX = 0f)
    {
        var iconText = IconText(icon);
        Vector2 iconSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var textSize = ImGui.CalcTextSize(label);
        var gap = UiStyle.InlineSpacing;
        var size = new Vector2(
            iconSize.X + gap + textSize.X + UiStyle.ActionButtonPadding.X * 2f,
            MathF.Max(iconSize.Y, textSize.Y) + UiStyle.ActionButtonPadding.Y * 2f);
        if (forcedWidth.HasValue)
        {
            size.X = forcedWidth.Value;
        }

        var background = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonBackground
            : UiStyle.SecondaryButtonBackground;
        var hovered = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonHoveredBackground
            : UiStyle.SecondaryButtonHoveredBackground;
        var active = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonActiveBackground
            : UiStyle.SecondaryButtonActiveBackground;

        using var buttonId = ImRaii.PushId(HashCode.Combine(icon, label, tone));
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, UiStyle.ActionButtonPadding)
            .Push(ImGuiStyleVar.FrameRounding, UiStyle.CardRounding);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, background)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);

        var pressed = ImGui.Button("##ActionButton", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var iconPos = new Vector2(
            min.X + UiStyle.ActionButtonPadding.X,
            min.Y + (max.Y - min.Y - iconSize.Y) * 0.5f);
        var textPos = new Vector2(
            iconPos.X + iconSize.X + gap + textOffsetX,
            min.Y + (max.Y - min.Y - textSize.Y) * 0.5f);
        var textColor = ResolveTextColorU32();
        var drawList = ImGui.GetWindowDrawList();

        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            drawList.AddText(iconPos, textColor, iconText);
        }

        drawList.AddText(textPos, textColor, label);
        return pressed;
    }

    private static bool DrawIconActionButton(string id, FontAwesomeIcon icon, UiButtonTone tone, string? tooltip = null)
    {
        var iconText = IconText(icon);
        Vector2 iconSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var size = new Vector2(
            iconSize.X + UiStyle.IconActionButtonPadding.X * 2f,
            iconSize.Y + UiStyle.IconActionButtonPadding.Y * 2f);
        var background = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonBackground
            : UiStyle.SecondaryButtonBackground;
        var hovered = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonHoveredBackground
            : UiStyle.SecondaryButtonHoveredBackground;
        var active = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonActiveBackground
            : UiStyle.SecondaryButtonActiveBackground;

        using var buttonId = ImRaii.PushId(id);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, UiStyle.IconActionButtonPadding)
            .Push(ImGuiStyleVar.FrameRounding, UiStyle.CardRounding);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, background)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);

        var pressed = ImGui.Button("##IconActionButton", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var iconPos = new Vector2(
            min.X + (size.X - iconSize.X) * 0.5f,
            min.Y + (size.Y - iconSize.Y) * 0.5f);
        var textColor = ResolveTextColorU32();
        var drawList = ImGui.GetWindowDrawList();
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            drawList.AddText(iconPos, textColor, iconText);
        }

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        return pressed;
    }

    private static Vector2 MeasureIconActionButtonSize(FontAwesomeIcon icon)
    {
        var iconText = IconText(icon);
        Vector2 iconSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        return new Vector2(
            iconSize.X + UiStyle.IconActionButtonPadding.X * 2f,
            iconSize.Y + UiStyle.IconActionButtonPadding.Y * 2f);
    }

    private static bool DrawToggleChip(string id, string label, bool selected, float forcedWidth = 0f)
    {
        var background = selected ? UiStyle.AccentChipBackground : UiStyle.TagChipBackground;
        var hovered = selected ? UiStyle.PrimaryButtonHoveredBackground : UiStyle.SecondaryButtonHoveredBackground;
        var active = selected ? UiStyle.PrimaryButtonActiveBackground : UiStyle.SecondaryButtonActiveBackground;
        var chipPadding = UiStyle.ToggleChipPadding;

        using var chipId = ImRaii.PushId(id);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, chipPadding)
            .Push(ImGuiStyleVar.FrameRounding, UiStyle.CardRounding);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, background)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);
        return forcedWidth > 0f
            ? ImGui.Button(label, new Vector2(forcedWidth, 0f))
            : ImGui.Button(label);
    }

    private static bool DrawToggleChip(string id, FontAwesomeIcon icon, string label, bool selected, float forcedWidth = 0f, float iconScale = 1f)
    {
        var background = selected ? UiStyle.AccentChipBackground : UiStyle.TagChipBackground;
        var hovered = selected ? UiStyle.PrimaryButtonHoveredBackground : UiStyle.SecondaryButtonHoveredBackground;
        var active = selected ? UiStyle.PrimaryButtonActiveBackground : UiStyle.SecondaryButtonActiveBackground;
        var chipPadding = UiStyle.ToggleChipPadding;
        var iconText = IconText(icon);
        Vector2 iconSize;
        float iconFontSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconFontSize = ImGui.GetFontSize() * iconScale;
            iconSize = ImGui.CalcTextSize(iconText) * iconScale;
        }

        var textSize = ImGui.CalcTextSize(label);
        var gap = UiStyle.InlineSpacing;
        var size = new Vector2(
            iconSize.X + gap + textSize.X + chipPadding.X * 2f,
            MathF.Max(MathF.Min(iconSize.Y, textSize.Y * 1.2f), textSize.Y) + chipPadding.Y * 2f);
        if (forcedWidth > 0f)
        {
            size.X = forcedWidth;
        }

        using var chipId = ImRaii.PushId(id);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, chipPadding)
            .Push(ImGuiStyleVar.FrameRounding, UiStyle.CardRounding);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, background)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);

        var pressed = ImGui.Button("##ToggleChip", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var contentWidth = iconSize.X + gap + textSize.X;
        var centeredStartX = min.X + (size.X - contentWidth) * 0.5f;
        var contentStartX = forcedWidth > 0f
            ? centeredStartX
            : min.X + chipPadding.X;
        var iconPos = new Vector2(
            contentStartX,
            min.Y + (max.Y - min.Y - iconSize.Y) * 0.5f);
        var textPos = new Vector2(
            iconPos.X + iconSize.X + gap,
            min.Y + (max.Y - min.Y - textSize.Y) * 0.5f);
        var textColor = ResolveTextColorU32();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddText(PluginUiFont.IconFont, iconFontSize, iconPos, textColor, iconText);
        drawList.AddText(textPos, textColor, label);
        return pressed;
    }

    private static void DrawStaticChip(string id, string text, UiChipTone tone, Vector2 size, bool centerText)
    {
        var drawList = ImGui.GetWindowDrawList();
        var background = GetChipBackground(tone);

        ImGui.InvisibleButton($"##{id}", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        var rounding = size.Y * 0.5f;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(background), rounding);
        drawList.AddRect(min, max, border, rounding);

        var textSize = ImGui.CalcTextSize(text);
        var textPos = centerText
            ? new Vector2(
                min.X + (size.X - textSize.X) * 0.5f,
                min.Y + (size.Y - textSize.Y) * 0.5f)
            : new Vector2(
                min.X + UiStyle.ChipPadding.X,
                min.Y + UiStyle.ChipPadding.Y);
        drawList.AddText(textPos, ResolveTextColorU32(), text);
    }

    private static Vector4 GetChipBackground(UiChipTone tone) => tone switch
    {
        UiChipTone.Accent => UiStyle.AccentChipBackground,
        UiChipTone.Warning => UiStyle.WarningChipBackground,
        UiChipTone.Positive => UiStyle.PositiveChipBackground,
        UiChipTone.Caution => UiStyle.CautionChipBackground,
        _ => UiStyle.TagChipBackground
    };

    // Lays items out in rows: each item goes on the current line if it fits before rightEdge (a cursor X), otherwise on a new line.
    private static void PlaceWrapped(float itemWidth, float rightEdge, ref bool first)
    {
        if (!first)
        {
            ImGui.SameLine(0f, UiStyle.InlineSpacing);
            if (ImGui.GetCursorPosX() + itemWidth > rightEdge)
            {
                ImGui.NewLine();
            }
        }

        first = false;
    }

    private static IDisposable PushChipButtonColors(UiChipTone tone)
    {
        var background = GetChipBackground(tone);
        var hovered = tone == UiChipTone.Neutral ? UiStyle.SecondaryButtonHoveredBackground : Brighten(background, 0.06f);
        var active = tone == UiChipTone.Neutral ? UiStyle.SecondaryButtonActiveBackground : Brighten(background, -0.04f);
        return ImRaii.PushColor(ImGuiCol.Button, background)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);
    }

    private static Vector4 Brighten(Vector4 color, float amount) =>
        new(Math.Clamp(color.X + amount, 0f, 1f), Math.Clamp(color.Y + amount, 0f, 1f), Math.Clamp(color.Z + amount, 0f, 1f), color.W);

    // A pill-shaped button: label, then an icon on the right (a cross for removable filters).
    private static Vector2 MeasureChipButton(string label, FontAwesomeIcon icon)
    {
        Vector2 iconSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconSize = ImGui.CalcTextSize(IconText(icon));
        }

        var textSize = ImGui.CalcTextSize(label);
        var padding = UiStyle.RemovableChipPadding;
        return new Vector2(
            textSize.X + UiStyle.InlineSpacing + iconSize.X + padding.X * 2f,
            MathF.Max(textSize.Y, iconSize.Y) + padding.Y * 2f);
    }

    private static bool DrawChipButton(int id, string label, FontAwesomeIcon icon, Vector2 size, UiChipTone tone, string? tooltip)
    {
        using var chipId = ImRaii.PushId(id);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, size.Y * 0.5f);
        using var colors = PushChipButtonColors(tone);

        var pressed = ImGui.Button("##ChipButton", size);
        var min = ImGui.GetItemRectMin();
        var padding = UiStyle.RemovableChipPadding;
        var textColor = ResolveTextColorU32();
        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(new Vector2(min.X + padding.X, min.Y + (size.Y - textSize.Y) * 0.5f), textColor, label);
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            var iconText = IconText(icon);
            var iconSize = ImGui.CalcTextSize(iconText);
            drawList.AddText(
                new Vector2(min.X + size.X - padding.X - iconSize.X, min.Y + (size.Y - iconSize.Y) * 0.5f),
                textColor,
                iconText);
        }

        if (tooltip != null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        return pressed;
    }

    // An icon button that shows an on/off state: highlighted, and in activeColor, while on.
    private static bool DrawToggleIconButton(string id, FontAwesomeIcon icon, bool active, string tooltip, Vector4? activeColor = null)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, activeColor ?? UiStyle.BodyText, active && activeColor.HasValue);
        return DrawIconActionButton(id, icon, active ? UiButtonTone.Primary : UiButtonTone.Secondary, tooltip);
    }

    // A read-only pill sized to its text.
    private static void DrawInfoChip(string id, string text, UiChipTone tone, float rightEdge, ref bool first)
    {
        var padding = UiStyle.RemovableChipPadding;
        var textSize = ImGui.CalcTextSize(text);
        var size = new Vector2(textSize.X + padding.X * 2f, textSize.Y + padding.Y * 2f);
        PlaceWrapped(size.X, rightEdge, ref first);
        DrawStaticChip(id, text, tone, size, centerText: true);
    }

    private static Vector2 MeasureActionButtonSize(FontAwesomeIcon icon, string label)
    {
        var iconText = IconText(icon);
        Vector2 iconSize;
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var textSize = ImGui.CalcTextSize(label);
        return new Vector2(
            iconSize.X + UiStyle.InlineSpacing + textSize.X + UiStyle.ActionButtonPadding.X * 2f,
            MathF.Max(iconSize.Y, textSize.Y) + UiStyle.ActionButtonPadding.Y * 2f);
    }

    private static readonly Dictionary<FontAwesomeIcon, string> IconTexts = [];

    // Returns the glyph string of an icon, cached because ToIconString allocates on every call.
    private static string IconText(FontAwesomeIcon icon)
    {
        if (!IconTexts.TryGetValue(icon, out var text))
        {
            IconTexts[icon] = text = icon.ToIconString();
        }

        return text;
    }

    private static readonly Dictionary<int, string> CountTexts = [];
    private static readonly Dictionary<int, string> AllTagsLabels = [];

    // Returns a cached string for a count drawn every frame.
    private static string CountText(int count)
    {
        if (!CountTexts.TryGetValue(count, out var text))
        {
            CountTexts[count] = text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return text;
    }

    private static string AllTagsLabel(int count)
    {
        if (!AllTagsLabels.TryGetValue(count, out var text))
        {
            AllTagsLabels[count] = text = $"All {count} tags";
        }

        return text;
    }
}
