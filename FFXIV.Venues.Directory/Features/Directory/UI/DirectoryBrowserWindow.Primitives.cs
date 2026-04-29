using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private static Vector4 ResolveTextColor(Vector4? color = null) => color ?? UiStyle.BodyText;

    private static uint ResolveTextColorU32(Vector4? color = null) => ImGui.GetColorU32(ResolveTextColor(color));

    private static void DrawVerticalRhythm(float height = 1f)
    {
        ImGuiHelpers.ScaledDummy(0f, UiStyle.CardSpacingUnits * height);
    }

    private static void DrawExactVerticalGap(float gap)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y + gap);
    }

    private static void AlignCursorRight(float itemWidth)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - itemWidth));
    }

    private static void DrawCenteredFixedWidthRow(float buttonWidth, float spacing, params Action<float>[] drawButtons)
    {
        if (drawButtons.Length == 0)
        {
            return;
        }

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X);
        var totalWidth = drawButtons.Length * buttonWidth + MathF.Max(0, drawButtons.Length - 1) * spacing;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (availableWidth - totalWidth) * 0.5f));
        for (var i = 0; i < drawButtons.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, spacing);
            }

            drawButtons[i](buttonWidth);
        }
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
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextUnformatted(icon.ToIconString());
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

    private static void DrawBodyText(string text)
    {
        DrawText(text, UiStyle.BodyText);
    }

    private static void DrawBodyTextWrapped(string text)
    {
        DrawTextWrapped(text, UiStyle.BodyText);
    }

    private static void DrawDisplayTitleText(string text, float? wrapPos = null)
    {
        ImGui.SetWindowFontScale(1.4f);
        try
        {
            using var textColor = ImRaii.PushColor(ImGuiCol.Text, UiStyle.DisplayTitleText);
            if (wrapPos.HasValue)
            {
                ImGui.PushTextWrapPos(wrapPos.Value);
                try
                {
                    ImGui.TextUnformatted(text);
                }
                finally
                {
                    ImGui.PopTextWrapPos();
                }
            }
            else
            {
                ImGui.TextUnformatted(text);
            }
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }

    private static bool DrawActionButton(string label, UiButtonTone tone)
    {
        var background = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonBackground
            : UiStyle.SecondaryButtonBackground;
        var hovered = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonHoveredBackground
            : UiStyle.SecondaryButtonHoveredBackground;
        var active = tone == UiButtonTone.Primary
            ? UiStyle.PrimaryButtonActiveBackground
            : UiStyle.SecondaryButtonActiveBackground;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, UiStyle.ActionButtonPadding)
            .Push(ImGuiStyleVar.FrameRounding, UiStyle.CardRounding);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, background)
            .Push(ImGuiCol.ButtonHovered, hovered)
            .Push(ImGuiCol.ButtonActive, active);
        return ImGui.Button(label);
    }

    private static bool DrawActionButton(FontAwesomeIcon icon, string label, UiButtonTone tone, float? forcedWidth = null, float textOffsetX = 0f)
    {
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
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

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            drawList.AddText(iconPos, textColor, iconText);
        }

        drawList.AddText(textPos, textColor, label);
        return pressed;
    }

    private static bool DrawIconActionButton(string id, FontAwesomeIcon icon, UiButtonTone tone, string? tooltip = null)
    {
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
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
        using (ImRaii.PushFont(UiBuilder.IconFont))
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
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        return new Vector2(
            iconSize.X + UiStyle.IconActionButtonPadding.X * 2f,
            iconSize.Y + UiStyle.IconActionButtonPadding.Y * 2f);
    }

    private static Vector2 MeasureActionButtonSize(string label)
    {
        var textSize = ImGui.CalcTextSize(label);
        return new Vector2(
            textSize.X + UiStyle.ActionButtonPadding.X * 2f,
            textSize.Y + UiStyle.ActionButtonPadding.Y * 2f);
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

    private static bool DrawToggleChip(string id, FontAwesomeIcon icon, string label, bool selected, float forcedWidth = 0f)
    {
        var background = selected ? UiStyle.AccentChipBackground : UiStyle.TagChipBackground;
        var hovered = selected ? UiStyle.PrimaryButtonHoveredBackground : UiStyle.SecondaryButtonHoveredBackground;
        var active = selected ? UiStyle.PrimaryButtonActiveBackground : UiStyle.SecondaryButtonActiveBackground;
        var chipPadding = UiStyle.ToggleChipPadding;
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var textSize = ImGui.CalcTextSize(label);
        var gap = UiStyle.InlineSpacing;
        var size = new Vector2(
            iconSize.X + gap + textSize.X + chipPadding.X * 2f,
            MathF.Max(iconSize.Y, textSize.Y) + chipPadding.Y * 2f);
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

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            drawList.AddText(iconPos, textColor, iconText);
        }

        drawList.AddText(textPos, textColor, label);
        return pressed;
    }

    private static void DrawStaticChip(string id, string text, UiChipTone tone, Vector2 size, bool centerText)
    {
        var drawList = ImGui.GetWindowDrawList();
        var background = tone switch
        {
            UiChipTone.Accent => UiStyle.AccentChipBackground,
            UiChipTone.Warning => UiStyle.WarningChipBackground,
            _ => UiStyle.TagChipBackground
        };

        ImGui.InvisibleButton($"##{id}", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(background), UiStyle.CardRounding);
        drawList.AddRect(min, max, border, UiStyle.CardRounding);

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

    private static Vector2 MeasureActionButtonSize(FontAwesomeIcon icon, string label)
    {
        var iconText = icon.ToIconString();
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var textSize = ImGui.CalcTextSize(label);
        return new Vector2(
            iconSize.X + UiStyle.InlineSpacing + textSize.X + UiStyle.ActionButtonPadding.X * 2f,
            MathF.Max(iconSize.Y, textSize.Y) + UiStyle.ActionButtonPadding.Y * 2f);
    }
}
