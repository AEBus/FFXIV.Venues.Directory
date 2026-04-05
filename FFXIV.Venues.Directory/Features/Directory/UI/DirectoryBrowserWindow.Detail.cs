using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Media;
using FFXIV.Venues.Directory.Infrastructure;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private void DrawVenueDetails(PreparedVenue venue, bool allowBanner)
    {
        var detailBuildPending = false;
        var bannerDrawn = false;
        if (allowBanner)
        {
            var banner = _venueService.GetVenueBanner(venue.Id, venue.Source.BannerUri);
            if (banner != null)
            {
                var padding = ImGui.GetStyle().WindowPadding.X * 2f;
                var maxWidth = MathF.Max(0f, _rightPaneWidth - padding);
                var targetWidth = _isNarrowDetailDrawerActive
                    ? Scale(NarrowDetailBannerWidth)
                    : Scale(BannerMaxWidth);
                var width = MathF.Min(maxWidth, targetWidth);
                if (_isNarrowDetailDrawerActive)
                {
                    var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
                    if (availableWidth > width)
                    {
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((availableWidth - width) * 0.5f));
                    }
                }

                var aspect = banner.Width == 0 ? 0.5f : banner.Height / (float)banner.Width;
                var size = new Vector2(width, MathF.Max(Scale(120f), width * aspect));
                ImGui.Image(banner.Handle, size);
                bannerDrawn = true;
            }
        }

        PreparedVenueDetails details = default!;
        var detailsReady = false;
        detailsReady = TryGetPreparedVenueDetails(venue, out details, out _, out detailBuildPending);

        if (bannerDrawn)
        {
            DrawVerticalRhythm(0.5f);
        }

        DrawVenueDetailHeader(venue.DisplayName);
        var routeOptions = detailsReady && details.RouteOptions.Length > 0
            ? details.RouteOptions
            : GetImmediateRouteOptions(venue);
        var selectedRouteIndex = GetSelectedRouteIndex(venue.Id, routeOptions.Length);
        var selectedRoute = routeOptions[selectedRouteIndex];
        DrawVerticalRhythm(0.5f);
        DrawSection("VenueIdentityCard", UiStyle.DetailInsetBackground, () =>
        {
            DrawSectionHeader(routeOptions.Length > 1 ? "Route" : "Address");
            DrawVerticalRhythm(0.25f);

            if (routeOptions.Length > 1)
            {
                using (ImRaii.ItemWidth(-1f))
                using (var routeCombo = ImRaii.Combo("##VenueRouteSelector"u8, selectedRoute.DisplayText))
                {
                    if (routeCombo)
                    {
                        for (var i = 0; i < routeOptions.Length; i++)
                        {
                            var isSelected = i == selectedRouteIndex;
                            if (ImGui.Selectable(routeOptions[i].DisplayText, isSelected))
                            {
                                _selectedRouteIndices[venue.Id] = i;
                                selectedRouteIndex = i;
                                selectedRoute = routeOptions[i];
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }
                }
            }
            else
            {
                using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.BodyText))
                {
                    DrawBodyTextWrapped(selectedRoute.DisplayText);
                }
            }

            if (detailBuildPending && !string.IsNullOrWhiteSpace(venue.Source.Location?.Override))
            {
                DrawVerticalRhythm(0.25f);
                DrawMutedText("Loading additional address options...");
            }

            DrawVerticalRhythm(0.5f);
            DrawSectionHeader("Actions");
            DrawVerticalRhythm(0.25f);

            var hasPreviousAction = false;
            if (DrawWrappedActionButton(FontAwesomeIcon.Copy, "Copy address", UiButtonTone.Secondary, ref hasPreviousAction))
            {
                ImGui.SetClipboardText(selectedRoute.CopyText);
            }

            if (_lifestreamIpc.IsAvailable)
            {
                using var lifestreamDisabled = ImRaii.Disabled(string.IsNullOrWhiteSpace(selectedRoute.LifestreamArguments));
                if (DrawWrappedActionButton(FontAwesomeIcon.LocationArrow, "Visit (Lifestream)", UiButtonTone.Primary, ref hasPreviousAction))
                {
                    var arguments = selectedRoute.LifestreamArguments;
                    if (!string.IsNullOrEmpty(arguments) &&
                        !_lifestreamIpc.TryExecuteCommand(arguments, out var errorMessage))
                    {
                        DalamudServices.ChatGui.PrintError($"Failed to execute Lifestream command: {errorMessage}");
                    }
                }
            }

            if (venue.Source.Website != null &&
                DrawWrappedActionButton(FontAwesomeIcon.Globe, "Website", UiButtonTone.Secondary, ref hasPreviousAction))
            {
                Util.OpenLink(venue.Source.Website.ToString());
            }

            if (venue.Source.Discord != null &&
                DrawWrappedActionButton(FontAwesomeIcon.CommentAlt, "Discord", UiButtonTone.Secondary, ref hasPreviousAction))
            {
                Util.OpenLink(venue.Source.Discord.ToString());
            }

            DrawVerticalRhythm(0.5f);
            DrawSectionHeader("Saved markers");
            DrawVerticalRhythm(0.25f);

            using (ImRaii.PushId(HashCode.Combine("VenuePreferenceActions", venue.Id)))
            {
                var stackPreferences = ImGui.GetContentRegionAvail().X < Scale(280f);
                var isFavorite = IsPreferredVenue(_favoriteVenueIds, venue.Id);
                if (ImGui.Checkbox("Favorite", ref isFavorite))
                {
                    SetPreferredVenue(_configuration.FavoriteVenueIds, _favoriteVenueIds, venue.Id, isFavorite);
                }

                if (!stackPreferences)
                {
                    ImGui.SameLine(0f, UiStyle.InlineGroupSpacing);
                }

                var isVisited = IsPreferredVenue(_visitedVenueIds, venue.Id);
                if (ImGui.Checkbox("Visited", ref isVisited))
                {
                    SetPreferredVenue(_configuration.VisitedVenueIds, _visitedVenueIds, venue.Id, isVisited);
                }
            }
        });

        if (venue.WarningText != null)
        {
            DrawSection("NsfwWarningCard", UiStyle.SectionHighlightBackground, () =>
            {
                DrawSectionHeader(FontAwesomeIcon.ExclamationTriangle, "Warning");
                DrawVerticalRhythm(0.25f);
                DrawBodyTextWrapped(venue.WarningText);
            });
        }

        if (detailsReady)
        {
            var schedulePending = details.SchedulePending;
            if (details.DescriptionLines.Length > 0)
            {
                DrawSection("DescriptionCard", "Description", () =>
                {
                    DrawDescriptionWithLinks(details.DescriptionLines);
                });
            }

            DrawSection("ScheduleCard", "Schedule", UiStyle.SectionHighlightBackground, () =>
            {
                if (!string.IsNullOrWhiteSpace(details.ResolutionSummary))
                {
                    DrawText(details.ResolutionSummary, UiStyle.SectionAccentText);
                    DrawVerticalRhythm(0.25f);
                }

                if (details.ScheduleRows.Length > 0)
                {
                    var tableFlags = ImGuiTableFlags.SizingStretchProp |
                                     ImGuiTableFlags.NoHostExtendX |
                                     ImGuiTableFlags.BordersOuterH |
                                     ImGuiTableFlags.BordersInnerH;
                    var originalCursorX = ImGui.GetCursorPosX();
                    var fullBleedWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X + UiStyle.CardPadding.X);
                    ImGui.SetCursorPosX(MathF.Max(0f, originalCursorX - UiStyle.CardPadding.X));
                    using var scheduleCellPadding = ImRaii.PushStyle(
                        ImGuiStyleVar.CellPadding,
                        new Vector2(UiStyle.CardPadding.X, ImGui.GetStyle().CellPadding.Y));
                    using (var scheduleTable = ImRaii.Table("VenueScheduleTable"u8, 2, tableFlags, new Vector2(fullBleedWidth, 0f)))
                    {
                        if (scheduleTable)
                        {
                            ImGui.TableSetupColumn("Day", ImGuiTableColumnFlags.WidthStretch, 0.62f);
                            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.38f);

                            foreach (var schedule in details.ScheduleRows)
                            {
                                var labelColor = schedule.IsActive ? UiStyle.SectionAccentText : UiStyle.BodyText;

                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                DrawLeftInsetTableText(schedule.Label, labelColor);
                                ImGui.TableNextColumn();
                                DrawRightAlignedTableText(schedule.TimeRange, labelColor);
                            }
                        }
                    }

                    DrawVerticalRhythm(0.25f);
                    DrawMutedText("All times are in your timezone.");
                }
                else if (schedulePending && HasScheduleEntries(venue.Source.Schedule))
                {
                    DrawMutedText("Loading schedule...");
                }
                else if (HasScheduleEntries(venue.Source.Schedule))
                {
                    DrawMutedText("No schedule available.");
                }
            });
        }
        else
        {
            DrawPendingVenueDetails(venue, detailBuildPending);
        }

        if (venue.Tags?.Count > 0)
        {
            DrawSection("TagsCard", "Tags", () => DrawTagChips(venue.Tags));
        }
    }

    private void DrawPendingVenueDetails(PreparedVenue venue, bool detailBuildPending)
    {
        if (HasNonEmptyDescription(venue.Source.Description))
        {
            DrawSection("DescriptionLoadingCard", "Description", () =>
            {
                DrawMutedText(detailBuildPending ? "Loading description..." : "No description available.");
            });
        }

        DrawSection("ScheduleLoadingCard", "Schedule", UiStyle.SectionHighlightBackground, () =>
        {
            var resolutionSummary = BuildResolutionSummary(venue.Source.Resolution);
            if (!string.IsNullOrWhiteSpace(resolutionSummary))
            {
                DrawText(resolutionSummary, UiStyle.SectionAccentText);
                DrawVerticalRhythm(0.25f);
            }

            if (HasScheduleEntries(venue.Source.Schedule))
            {
                DrawMutedText(detailBuildPending ? "Loading schedule..." : "No schedule available.");
            }

            if (detailBuildPending && !string.IsNullOrWhiteSpace(venue.Source.Location?.Override))
            {
                DrawMutedText("Loading address options...");
            }
        });
    }

    private static bool HasNonEmptyDescription(IEnumerable<string>? lines) =>
        lines?.Any(line => !string.IsNullOrWhiteSpace(line)) == true;

    private static bool HasScheduleEntries(IEnumerable<DirectorySchedule>? schedules) =>
        schedules?.Any() == true;

    private static void DrawRightAlignedTableText(string text, Vector4 color)
    {
        var textWidth = ImGui.CalcTextSize(text).X;
        var cursorX = ImGui.GetCursorPosX();
        var rightInset = Scale(12f);
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - rightInset);
        if (availableWidth > textWidth)
        {
            ImGui.SetCursorPosX(cursorX + (availableWidth - textWidth));
        }

        ImGui.PushTextWrapPos(0f);
        try
        {
            DrawText(text, color);
        }
        finally
        {
            ImGui.PopTextWrapPos();
        }
    }

    private static void DrawLeftInsetTableText(string text, Vector4 color)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Scale(12f));
        DrawText(text, color);
    }

    private void DrawSection(string id, Action content) =>
        DrawSection(id, null, null, content);

    private void DrawSection(string id, string title, Action content) =>
        DrawSection(id, title, null, content);

    private void DrawSection(string id, Vector4? backgroundOverride, Action content)
        => DrawSection(id, null, backgroundOverride, content);

    private void DrawSection(string id, string? title, Vector4? backgroundOverride, Action content)
    {
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var contentWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var bg = ImGui.GetColorU32(backgroundOverride ?? UiStyle.SectionBackground);
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        var padding = UiStyle.CardPadding;
        using var sectionId = ImRaii.PushId(id.GetHashCode(StringComparison.Ordinal));

        drawList.ChannelsSplit(2);
        try
        {
            drawList.ChannelsSetCurrent(1);
            using (ImRaii.Group())
            {
                DrawVerticalRhythm(0.5f);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding.X);
                var wrapRightEdge = ImGui.GetCursorPosX() + MathF.Max(0f, contentWidth - padding.X * 2f);
                ImGui.PushTextWrapPos(wrapRightEdge);
                try
                {
                    using (ImRaii.Group())
                    using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.BodyText))
                    {
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            DrawSectionHeader(title);
                            DrawVerticalRhythm(0.25f);
                        }

                        content();
                    }
                }
                finally
                {
                    ImGui.PopTextWrapPos();
                }

                DrawVerticalRhythm(0.5f);
            }

            drawList.ChannelsSetCurrent(0);

            var min = startPos;
            var max = ImGui.GetItemRectMax();
            max = new Vector2(min.X + contentWidth, max.Y);
            drawList.AddRectFilled(min, max, bg, UiStyle.CardRounding);
            drawList.AddRect(min, max, border, UiStyle.CardRounding);
        }
        finally
        {
            drawList.ChannelsMerge();
        }

        DrawVerticalRhythm();
    }

    private static void DrawTagChips(IEnumerable<string> tags)
    {
        var spacing = UiStyle.CompactRowSpacing;
        var framePadding = UiStyle.ChipPadding;
        var startPosX = ImGui.GetCursorPosX();
        var rightInset = UiStyle.TagRightInset;
        var maxWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - rightInset);
        var x = startPosX;
        var y = ImGui.GetCursorPosY();
        var rowHeight = 0f;
        var startScreen = ImGui.GetCursorScreenPos();
        var rightEdge = startScreen.X + maxWidth;

        var tagIndex = 0;
        foreach (var tag in tags)
        {
            var size = ImGui.CalcTextSize(tag);
            var chipWidth = size.X + framePadding.X * 2f;
            var chipHeight = size.Y + framePadding.Y * 2f;

            var screenX = startScreen.X + (x - startPosX);
            if (screenX + chipWidth > rightEdge && x > startPosX)
            {
                x = startPosX;
                y += rowHeight + spacing;
                rowHeight = 0f;
            }

            ImGui.SetCursorPos(new Vector2(x, y));
            DrawStaticChip($"tag{tagIndex++}", tag, UiChipTone.Accent, new Vector2(chipWidth, chipHeight), centerText: false);
            x += chipWidth + spacing;
            rowHeight = MathF.Max(rowHeight, chipHeight);
        }

        ImGui.SetCursorPos(new Vector2(startPosX, y + rowHeight + spacing));
    }

    private static void DrawDescriptionWithLinks(IReadOnlyList<PreparedDescriptionLine> lines)
    {
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.IsBlank)
            {
                DrawVerticalRhythm(0.5f);
                continue;
            }

            if (!DescriptionLineHasLinks(line))
            {
                DrawWrappedDescriptionTextLine(line);
                continue;
            }

            DrawWrappedDescriptionLine(line, lineIndex);
        }
    }

    private static bool DescriptionLineHasLinks(PreparedDescriptionLine line) =>
        line.Segments.Any(segment => !string.IsNullOrEmpty(segment.Url));

    private static void DrawWrappedDescriptionTextLine(PreparedDescriptionLine line)
    {
        var wrapRightEdge = ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - Scale(12f));
        ImGui.PushTextWrapPos(wrapRightEdge);
        try
        {
            ImGui.TextUnformatted(GetPreparedDescriptionLineText(line));
        }
        finally
        {
            ImGui.PopTextWrapPos();
        }
    }

    private static string GetPreparedDescriptionLineText(PreparedDescriptionLine line)
    {
        if (line.Segments.Length == 0)
        {
            return string.Empty;
        }

        if (line.Segments.Length == 1)
        {
            return line.Segments[0].Text;
        }

        var builder = new StringBuilder();
        foreach (var segment in line.Segments)
        {
            builder.Append(segment.Text);
        }

        return builder.ToString();
    }

    private static void DrawWrappedDescriptionLine(PreparedDescriptionLine line, int lineIndex)
    {
        var lineStartScreenX = ImGui.GetCursorScreenPos().X;
        var rightEdge = lineStartScreenX + MathF.Max(0f, ImGui.GetContentRegionAvail().X - Scale(12f));
        var isLineStart = true;
        var lastItemRightEdge = lineStartScreenX;
        var renderChunkIndex = 0;

        foreach (var segment in EnumerateDescriptionRenderTokens(line.Segments))
        {
            var text = segment.Text;
            if (text.Length == 0)
            {
                continue;
            }

            var width = ImGui.CalcTextSize(text).X;
            if (!isLineStart &&
                lastItemRightEdge + width > rightEdge)
            {
                ImGui.NewLine();
                isLineStart = true;
                lastItemRightEdge = lineStartScreenX;
                text = text.TrimStart();
                if (text.Length == 0)
                {
                    continue;
                }

                width = ImGui.CalcTextSize(text).X;
            }

            if (!isLineStart)
            {
                ImGui.SameLine(0f, 0f);
            }

            DrawDescriptionSegment(text, segment.Url, lineIndex, renderChunkIndex);
            lastItemRightEdge = ImGui.GetItemRectMax().X;
            isLineStart = false;
            renderChunkIndex++;
        }
    }

    private static IEnumerable<PreparedDescriptionSegment> EnumerateDescriptionRenderTokens(
        IReadOnlyList<PreparedDescriptionSegment> segments)
    {
        var emittedAny = false;
        var pendingWhitespace = false;

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(segment.Url))
            {
                var linkText = pendingWhitespace && emittedAny
                    ? " " + segment.Text.Trim()
                    : segment.Text.Trim();
                if (linkText.Length == 0)
                {
                    pendingWhitespace = false;
                    continue;
                }

                yield return new PreparedDescriptionSegment(linkText, segment.Url);
                emittedAny = true;
                pendingWhitespace = false;
                continue;
            }

            var start = 0;
            while (start < segment.Text.Length)
            {
                while (start < segment.Text.Length && char.IsWhiteSpace(segment.Text[start]))
                {
                    pendingWhitespace = emittedAny || pendingWhitespace;
                    start++;
                }

                if (start >= segment.Text.Length)
                {
                    break;
                }

                var end = start + 1;
                while (end < segment.Text.Length && !char.IsWhiteSpace(segment.Text[end]))
                {
                    end++;
                }

                var tokenText = segment.Text[start..end];
                if (pendingWhitespace && emittedAny)
                {
                    tokenText = " " + tokenText;
                }

                yield return new PreparedDescriptionSegment(tokenText, null);
                emittedAny = true;
                pendingWhitespace = false;
                start = end;
            }
        }
    }

    private static void DrawDescriptionSegment(string text, string? url, int lineIndex, int segmentIndex)
    {
        if (string.IsNullOrEmpty(url))
        {
            ImGui.TextUnformatted(text);
            return;
        }

        using var linkId = ImRaii.PushId(HashCode.Combine("DescLink", lineIndex, segmentIndex, url));
        using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.LinkText))
        {
            if (ImGui.Selectable(text, false, ImGuiSelectableFlags.DontClosePopups, ImGui.CalcTextSize(text)))
            {
                Util.OpenLink(url);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Open link");
        }
    }

    private static bool DrawWrappedActionButton(string label, UiButtonTone tone, ref bool hasPreviousAction)
    {
        if (hasPreviousAction)
        {
            var nextButtonSize = MeasureActionButtonSize(label);
            var lineRightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            var nextButtonRightEdge = ImGui.GetItemRectMax().X + UiStyle.InlineSpacing + nextButtonSize.X;
            if (nextButtonRightEdge <= lineRightEdge)
            {
                ImGui.SameLine(0f, UiStyle.InlineSpacing);
            }
            else
            {
                DrawVerticalRhythm(0.25f);
            }
        }

        hasPreviousAction = true;
        return DrawActionButton(label, tone);
    }

    private static bool DrawWrappedActionButton(FontAwesomeIcon icon, string label, UiButtonTone tone, ref bool hasPreviousAction) =>
        DrawWrappedActionButton(icon, label, tone, ref hasPreviousAction, MeasureActionButtonSize(icon, label));

    private static bool DrawWrappedActionButton(
        FontAwesomeIcon icon,
        string label,
        UiButtonTone tone,
        ref bool hasPreviousAction,
        Vector2 nextButtonSize)
    {
        if (hasPreviousAction)
        {
            var lineRightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            var nextButtonRightEdge = ImGui.GetItemRectMax().X + UiStyle.InlineSpacing + nextButtonSize.X;
            if (nextButtonRightEdge <= lineRightEdge)
            {
                ImGui.SameLine(0f, UiStyle.InlineSpacing);
            }
            else
            {
                DrawVerticalRhythm(0.25f);
            }
        }

        hasPreviousAction = true;
        return DrawActionButton(icon, label, tone);
    }

    private void DrawVenueDetailHeader(string headerName)
    {
        ImGui.SetWindowFontScale(1.4f);
        try
        {
            var headerSize = ImGui.CalcTextSize(headerName);
            var headerPos = ImGui.GetCursorScreenPos();
            var headerDrawList = ImGui.GetWindowDrawList();
            var headerShadow = ImGui.GetColorU32(UiStyle.HeaderShadowText);
            var headerColor = ImGui.GetColorU32(UiStyle.DisplayTitleText);
            headerDrawList.AddText(new Vector2(headerPos.X - Scale(1f), headerPos.Y), headerShadow, headerName);
            headerDrawList.AddText(new Vector2(headerPos.X + Scale(1f), headerPos.Y), headerShadow, headerName);
            headerDrawList.AddText(new Vector2(headerPos.X, headerPos.Y - Scale(1f)), headerShadow, headerName);
            headerDrawList.AddText(new Vector2(headerPos.X, headerPos.Y + Scale(1f)), headerShadow, headerName);
            headerDrawList.AddText(headerPos, headerColor, headerName);
            ImGui.SetCursorScreenPos(new Vector2(headerPos.X, headerPos.Y + headerSize.Y + UiStyle.CompactRowSpacing));
        }
        finally
        {
            ImGui.SetWindowFontScale(1f);
        }
    }
}
