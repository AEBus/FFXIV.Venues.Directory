using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Net;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenueAddresses;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenuePreparer;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private RichTextView? _venueDescriptionView;

    // Draws a button that opens the zone's map with a flag at the location, when the location has map coordinates.
    private static void DrawShowOnMapButton(OpenWorldPlace? place, ref bool hasPreviousAction)
    {
        if (place?.Coordinates is not { } at)
        {
            return;
        }

        if (DrawWrappedActionButton(FontAwesomeIcon.MapMarkedAlt, "Show on map", UiButtonTone.Secondary, ref hasPreviousAction))
        {
            try
            {
                DalamudServices.GameGui.OpenMapWithMapLink(new MapLinkPayload(place.TerritoryId, place.MapId, at.X, at.Y));
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Warning(ex, "Could not open the map for {Zone}.", place.ZoneName);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Open the map of {place.ZoneName} with a flag on the spot");
        }
    }

    private void DrawVenueDetails(PreparedVenue venue, bool allowBanner)
    {
        var detailBuildPending = false;
        var bannerDrawn = false;
        if (allowBanner && venue.Source.BannerUri is { } bannerUri)
        {
            var banner = _remoteImages.Get(bannerUri.ToString(), out var bannerFailed);
            if (banner != null)
            {
                var padding = ImGui.GetStyle().WindowPadding.X * 2f;
                var maxWidth = MathF.Max(0f, _rightPaneWidth - padding);
                var width = MathF.Min(maxWidth, Scale(BannerMaxWidth));
                var aspect = banner.Width == 0 ? 0.5f : banner.Height / (float)banner.Width;
                var size = new Vector2(width, MathF.Max(Scale(120f), width * aspect));
                DrawRoundedImage(banner.Handle, size);
                ImagePreview.HandleHover(banner, bannerUri.ToString(), _configuration.PreviewImagesOnHover);
                bannerDrawn = true;
            }
            else if (!bannerFailed)
            {
                DrawMutedText("Loading banner...");
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

        var routeOptions = detailsReady && details.RouteOptions.Length > 0
            ? details.RouteOptions
            : GetImmediateRouteOptions(venue);
        var selectedRouteIndex = GetSelectedRouteIndex(venue.Id, routeOptions.Length);
        var selectedRoute = routeOptions[selectedRouteIndex];

        DrawVenueHeader(venue);
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
            DrawTextWrapped(selectedRoute.DisplayText, UiStyle.BodyMutedText);
        }

        if (detailBuildPending && !string.IsNullOrWhiteSpace(venue.Source.Location?.Override))
        {
            DrawMutedText("Loading additional address options...");
        }

        DrawVerticalRhythm(0.25f);
        DrawVenueChips(venue);
        DrawVerticalRhythm(0.35f);

        var hasPreviousAction = false;
        if (_lifestreamIpc.IsAvailable)
        {
            using var lifestreamDisabled = ImRaii.Disabled(string.IsNullOrWhiteSpace(selectedRoute.LifestreamArguments));
            if (DrawWrappedActionButton(FontAwesomeIcon.LocationArrow, "Visit", UiButtonTone.Primary, ref hasPreviousAction))
            {
                var arguments = selectedRoute.LifestreamArguments;
                if (!string.IsNullOrEmpty(arguments) &&
                    !_lifestreamIpc.TryExecuteCommand(arguments, out var errorMessage))
                {
                    DalamudServices.ChatGui.PrintError($"Could not travel with Lifestream: {errorMessage}");
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Travel there with Lifestream");
            }
        }

        DrawShowOnMapButton(selectedRoute.Place, ref hasPreviousAction);
        if (DrawWrappedActionButton(FontAwesomeIcon.Copy, "Copy address", UiButtonTone.Secondary, ref hasPreviousAction))
        {
            ImGui.SetClipboardText(selectedRoute.CopyText);
        }

        if (venue.Source.Website != null &&
            DrawWrappedActionButton(FontAwesomeIcon.Globe, "Website", UiButtonTone.Secondary, ref hasPreviousAction))
        {
            WebLink.Open(venue.Source.Website.ToString());
        }

        if (venue.Source.Discord != null &&
            DrawWrappedActionButton(FontAwesomeIcon.CommentAlt, "Discord", UiButtonTone.Secondary, ref hasPreviousAction))
        {
            WebLink.Open(venue.Source.Discord.ToString());
        }

        DrawVerticalRhythm(0.5f);
        DrawVenueEventsCard(venue);

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
            for (var i = 0; i < details.Notices.Length; i++)
            {
                var notice = details.Notices[i];
                DrawSection($"NoticeCard{i}", notice.IsWarning ? UiStyle.SectionHighlightBackground : null, () =>
                {
                    DrawSectionHeader(
                        notice.IsWarning ? FontAwesomeIcon.ExclamationTriangle : FontAwesomeIcon.InfoCircle,
                        "Notice",
                        notice.IsWarning ? UiStyle.WarningText : null);
                    DrawVerticalRhythm(0.25f);
                    DrawBodyTextWrapped(notice.Message);
                });
            }

            if (!details.Description.IsEmpty)
            {
                DrawSection("DescriptionCard", "Description", () =>
                {
                    _venueDescriptionView ??= new RichTextView(_remoteImages, () => _configuration.LoadDescriptionImages, () => _configuration.PreviewImagesOnHover);
                    _venueDescriptionView.Draw(details.Description, RichTextVersion, FormatRichTime, DescribeRichTime, RichTextColors);
                });
            }

            // A venue known only from its ads has no hours to show.
            if (BuildResolutionSummary(venue.Status) != null || HasScheduleEntries(venue.Source.Schedule) || details.ScheduleAmendments.Length > 0)
            {
                DrawSection("ScheduleCard", "Schedule", UiStyle.SectionHighlightBackground, () =>
                {
                    if (BuildResolutionSummary(venue.Status) is { } resolutionSummary)
                    {
                        DrawText(resolutionSummary, venue.IsOpen ? UiStyle.PositiveText : UiStyle.BodyText);
                        DrawVerticalRhythm(0.25f);
                    }

                    var showsTimes = false;
                    if (details.ScheduleRows.Length > 0)
                    {
                        DrawScheduleTable("VenueScheduleTable", details.ScheduleRows);
                        showsTimes = true;
                    }
                    else if (schedulePending && HasScheduleEntries(venue.Source.Schedule))
                    {
                        DrawMutedText("Loading schedule...");
                    }
                    else if (HasScheduleEntries(venue.Source.Schedule))
                    {
                        DrawMutedText("No schedule available.");
                    }

                    if (details.ScheduleAmendments.Length > 0)
                    {
                        if (HasScheduleEntries(venue.Source.Schedule))
                        {
                            DrawVerticalRhythm(0.5f);
                        }

                        DrawSectionHeader("Schedule amendments");
                        DrawVerticalRhythm(0.25f);
                        DrawScheduleTable("VenueScheduleAmendmentsTable", details.ScheduleAmendments);
                        showsTimes = true;
                    }

                    if (showsTimes)
                    {
                        DrawVerticalRhythm(0.25f);
                        DrawMutedText("All times are in your timezone.");
                    }
                });
            }
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

        if (BuildResolutionSummary(venue.Status) == null && !HasScheduleEntries(venue.Source.Schedule))
        {
            return;
        }

        DrawSection("ScheduleLoadingCard", "Schedule", UiStyle.SectionHighlightBackground, () =>
        {
            if (BuildResolutionSummary(venue.Status) is { } resolutionSummary)
            {
                DrawText(resolutionSummary, venue.IsOpen ? UiStyle.PositiveText : UiStyle.BodyText);
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

    private static void DrawScheduleTable(string id, IReadOnlyList<PreparedScheduleRow> rows)
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
        using var scheduleTable = ImRaii.Table(id, 2, tableFlags, new Vector2(fullBleedWidth, 0f));
        if (!scheduleTable)
        {
            return;
        }

        ImGui.TableSetupColumn("Day", ImGuiTableColumnFlags.WidthStretch, 0.62f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.38f);

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            var labelColor = row.IsActiveAt(now) ? UiStyle.SectionAccentText : UiStyle.BodyText;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawLeftInsetTableText(row.Label, labelColor);
            ImGui.TableNextColumn();
            DrawRightAlignedTableText(row.TimeRange, labelColor);
        }
    }

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

        using (ImRaii.TextWrapPos(0f))
        {
            DrawText(text, color);
        }
    }

    private static void DrawLeftInsetTableText(string text, Vector4 color)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Scale(12f));
        DrawText(text, color);
    }

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
                using (ImRaii.TextWrapPos(wrapRightEdge))
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

    private void DrawVenueHeader(PreparedVenue venue)
    {
        var isFavorite = IsPreferredVenue(_favoriteVenueIds, venue.Id);
        var isVisited = IsPreferredVenue(_visitedVenueIds, venue.Id);
        var isHidden = _hiddenVenueIds.Contains(venue.Id);
        var hideIcon = isHidden ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash;
        var buttonsWidth = MeasureIconActionButtonSize(FontAwesomeIcon.Star).X +
                           MeasureIconActionButtonSize(FontAwesomeIcon.Check).X +
                           MeasureIconActionButtonSize(hideIcon).X +
                           UiStyle.InlineSpacing * 2f;
        var start = ImGui.GetCursorPos();
        var buttonsX = start.X + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttonsWidth);

        ImGui.SetCursorPos(new Vector2(buttonsX, start.Y));
        float buttonsBottom;
        using (ImRaii.PushId(venue.Id))
        {
            if (DrawToggleIconButton("Favorite", FontAwesomeIcon.Star, isFavorite, isFavorite ? "Remove from favorites" : "Add to favorites", UiStyle.FavoriteText))
            {
                SetPreferredVenue(_configuration.FavoriteVenueIds, _favoriteVenueIds, venue.Id, !isFavorite);
            }

            ImGui.SameLine(0f, UiStyle.InlineSpacing);
            if (DrawToggleIconButton("Visited", FontAwesomeIcon.Check, isVisited, isVisited ? "Unmark as visited" : "Mark as visited", UiStyle.PositiveText))
            {
                SetPreferredVenue(_configuration.VisitedVenueIds, _visitedVenueIds, venue.Id, !isVisited);
            }

            ImGui.SameLine(0f, UiStyle.InlineSpacing);
            if (DrawToggleIconButton("Hide", hideIcon, isHidden, isHidden ? "Show this venue in the list again" : "Hide this venue from the list"))
            {
                SetVenueHidden(venue, !isHidden);
            }

            buttonsBottom = ImGui.GetItemRectMax().Y;
        }

        ImGui.SetCursorPos(start);
        DrawDisplayTitleText(venue.DisplayName, buttonsX - UiStyle.InlineGroupSpacing);
        var cursor = ImGui.GetCursorScreenPos();
        var belowButtons = buttonsBottom + ImGui.GetStyle().ItemSpacing.Y;
        if (belowButtons > cursor.Y)
        {
            ImGui.SetCursorScreenPos(new Vector2(cursor.X, belowButtons));
        }
    }

    private void DrawVenueChips(PreparedVenue venue)
    {
        var rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var first = true;
        var now = DateTimeOffset.Now;
        if (venue.IsOpen && venue.Status.Opening is { } open)
        {
            DrawInfoChip("StatusChip", venue.Status.Advertised ? VenueStatus.AdvertisedLine : open.IsAroundTheClock ? "Open 24/7" : $"Open now · until {FormatClosingTime(open.End)}", UiChipTone.Positive, rightEdge, ref first);
        }
        else if (IsOpeningSoon(venue, now))
        {
            DrawInfoChip("StatusChip", FormatOpensIn(venue.Status.Opening!.Value.Start - now), UiChipTone.Caution, rightEdge, ref first);
        }
        else if (venue.Status.Opening != null)
        {
            DrawInfoChip("StatusChip", venue.StatusLine, UiChipTone.Neutral, rightEdge, ref first);
        }

        if (IsVenueHere(venue))
        {
            DrawInfoChip("HereChip", "You're here", UiChipTone.Positive, rightEdge, ref first);
        }

        if (venue.IsNsfw)
        {
            DrawInfoChip("NsfwChip", "NSFW", UiChipTone.Warning, rightEdge, ref first);
        }

        var size = venue.VenueTypeLabel switch
        {
            "A" => "Apartment",
            "S" => "Small house",
            "M" => "Medium house",
            "L" => "Large house",
            _ => null,
        };
        if (size != null)
        {
            DrawInfoChip("SizeChip", size, UiChipTone.Neutral, rightEdge, ref first);
        }

        if (venue.Region != null)
        {
            DrawInfoChip("RegionChip", venue.Region, UiChipTone.Neutral, rightEdge, ref first);
        }

        if (IsUnlistedVenue(venue))
        {
            DrawInfoChip("UnlistedChip", "Not on FFXIV Venues", UiChipTone.Accent, rightEdge, ref first);
        }
    }
}
