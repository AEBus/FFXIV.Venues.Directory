using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private void DrawVenueTable(IReadOnlyList<PreparedVenue> venues)
    {
        if (venues.Count == 0)
        {
            DrawVenueTableEmptyState();
            return;
        }

        var flags = ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingStretchProp |
                    ImGuiTableFlags.Sortable |
                    ImGuiTableFlags.NoSavedSettings;
        var size = ImGui.GetContentRegionAvail();
        float venueWrapWidth;
        float addressWrapWidth;
        using var tableColors = ImRaii.PushColor(ImGuiCol.TableHeaderBg, UiStyle.ListHeaderBackground)
            .Push(ImGuiCol.TableRowBgAlt, UiStyle.ListAlternateRowBackground);
        using var tableCellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(Scale(12f), ImGui.GetStyle().CellPadding.Y));
        using (var summaryTable = ImRaii.Table("VenueSummaryTable"u8, 4, flags, size))
        {
            if (!summaryTable)
            {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Venue", ImGuiTableColumnFlags.WidthStretch, UiStyle.ListVenueColumnWeight);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, UiStyle.ListAddressColumnWeight);
            // Wide enough for its header at any font size (glyphs do not grow exactly with the scale).
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, MathF.Max(UiStyle.ListSizeColumnWidth, ImGui.CalcTextSize("Size").X + Scale(4f)));
            ImGui.TableSetupColumn(
                "Status",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort,
                MeasureStatusColumnWidth());
            using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.ListHeaderText))
            {
                // The wrap widths of the venue and location cells, measured here while in their columns.
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                venueWrapWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f - UiStyle.ListMarkerReserve);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Scale(12f));
                ImGui.TableHeader("Venue");
                ImGui.TableNextColumn();
                addressWrapWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
                ImGui.TableHeader("Location");
                ImGui.TableNextColumn();
                ImGui.TableHeader("Size");
                ImGui.TableNextColumn();
                ImGui.TableHeader("Status");
            }

            EnsureSortedVenues(ImGui.TableGetSortSpecs());
            if (_sortedVenues.Count == 0)
            {
                return;
            }

            EnsureSortedVenueRowMetrics(venueWrapWidth, addressWrapWidth);

            var scrollY = ImGui.GetScrollY();
            var visibleHeight = ImGui.GetWindowHeight();
            var firstVisibleIndex = FindFirstVisibleSortedVenueIndex(scrollY);
            var endVisibleIndex = FindEndVisibleSortedVenueIndex(scrollY + visibleHeight);
            _ = Math.Clamp(endVisibleIndex - firstVisibleIndex, 0, _sortedVenues.Count);

            if (firstVisibleIndex > 0)
            {
                DrawVenueTableSpacerRow(_sortedVenueRowOffsets[firstVisibleIndex]);
            }

            for (var i = firstVisibleIndex; i < endVisibleIndex; i++)
            {
                DrawVenueTableRow(_sortedVenues[i], i, _sortedVenueRowHeights[i]);
            }

            var trailingHeight = _sortedVenueTotalHeight - _sortedVenueRowOffsets[endVisibleIndex];
            if (trailingHeight > 0f)
            {
                DrawVenueTableSpacerRow(trailingHeight);
            }
        }
    }

    private string GetEmptySelectionMessage()
    {
        if (_filters.HiddenOnly && _hiddenVenueIds.Count == 0)
        {
            return "You have not hidden any venues.";
        }

        if (_filters.FavoritesOnly && _favoriteVenueIds.Count == 0)
        {
            return "You have no favorite venues yet. Add some with the star in a venue's details.";
        }

        if (_filters.Visited == VenueVisitedFilter.Visited && _visitedVenueIds.Count == 0)
        {
            return "You have no visited venues yet. Mark some with the check in a venue's details.";
        }

        return _filteredVenues.Count == 0
            ? "No venues match the current filters."
            : "Select a venue from the list to see its details.";
    }

    private void EnsureSortedVenueRowMetrics(float venueWrapWidth, float addressWrapWidth)
    {
        if (!_sortedVenueRowMetricsDirty &&
            MathF.Abs(_sortedVenueColumnMetricsWrapWidth - venueWrapWidth) < 0.5f &&
            MathF.Abs(_sortedVenueRowMetricsWrapWidth - addressWrapWidth) < 0.5f &&
            _sortedVenueRowHeights.Count == _sortedVenues.Count)
        {
            return;
        }

        _sortedVenueRowMetricsDirty = false;
        _sortedVenueColumnMetricsWrapWidth = venueWrapWidth;
        _sortedVenueRowMetricsWrapWidth = addressWrapWidth;
        _sortedVenueRowHeights.Clear();
        _sortedVenueRowOffsets.Clear();
        _sortedVenueRowOffsets.Add(0f);

        var totalHeight = 0f;
        var minimumRowHeight = UiStyle.MinimumRowHeight;
        foreach (var venue in _sortedVenues)
        {
            // Measured once per venue and width: the list is laid out again on every filter or status change.
            var rowText = GetVenueRowText(venue);
            if (MathF.Abs(rowText.MeasuredWidth - venueWrapWidth) > 0.5f)
            {
                rowText.MeasuredHeight = ImGui.CalcTextSize(venue.DisplayName, false, venueWrapWidth).Y;
                rowText.MeasuredWidth = venueWrapWidth;
            }

            var venueHeight = rowText.MeasuredHeight;
            // The location is one line (cut with an ellipsis, full in the tooltip), so rows keep an even height.
            var addressHeight = ImGui.GetTextLineHeight();
            var rowHeight = MathF.Max(MathF.Max(venueHeight, addressHeight), minimumRowHeight);
            _sortedVenueRowHeights.Add(rowHeight);
            totalHeight += rowHeight;
            _sortedVenueRowOffsets.Add(totalHeight);
        }

        _sortedVenueTotalHeight = totalHeight;
    }

    private int FindFirstVisibleSortedVenueIndex(float visibleStart)
    {
        if (_sortedVenues.Count == 0)
        {
            return 0;
        }

        var index = UpperBound(_sortedVenueRowOffsets, visibleStart) - 1;
        return Math.Clamp(index, 0, _sortedVenues.Count);
    }

    private int FindEndVisibleSortedVenueIndex(float visibleEnd)
    {
        if (_sortedVenues.Count == 0)
        {
            return 0;
        }

        var index = LowerBound(_sortedVenueRowOffsets, visibleEnd);
        return Math.Clamp(index + 1, 0, _sortedVenues.Count);
    }

    private static int LowerBound(List<float> values, float target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (values[mid] < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static int UpperBound(List<float> values, float target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (values[mid] <= target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private void DrawVenueTableSpacerRow(float height)
    {
        if (height <= 0f)
        {
            return;
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.None, height);
        ImGui.TableNextColumn();
        ImGui.Dummy(new Vector2(1f, height));
    }

    private void DrawVenueTableRow(PreparedVenue venue, int rowIndex, float rowHeight)
    {
        var isSelected = string.Equals(_selectedVenueId, venue.Id, StringComparison.Ordinal);

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        ImGui.TableNextColumn();
        using (ImRaii.PushId(rowIndex))
        {
            using var rowHighlight = ImRaii.PushColor(ImGuiCol.Header, UiStyle.SelectedRowBackground)
                .Push(ImGuiCol.HeaderHovered, UiStyle.SelectedRowHoveredBackground)
                .Push(ImGuiCol.HeaderActive, UiStyle.SelectedRowActiveBackground);
            if (ImGui.Selectable("##VenueRow", isSelected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight)))
            {
                _selectedVenueId = venue.Id;
                _pinnedVenueId = null;
            }

            if (isSelected)
            {
                DrawSelectedRowAccent(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            }

            var textColor = isSelected
                ? ResolveTextColorU32(UiStyle.BodyStrongText)
                : ResolveTextColorU32();
            var cellWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
            var wrapWidth = MathF.Max(1f, cellWidth - UiStyle.ListMarkerReserve);
            var rowText = GetVenueRowText(venue);
            if (MathF.Abs(rowText.NameWidth - wrapWidth) > 0.5f)
            {
                rowText.NameLines = WrapTextToWidth(venue.DisplayName, wrapWidth);
                rowText.NameHeight = ImGui.CalcTextSize(venue.DisplayName, false, wrapWidth).Y;
                rowText.NameWidth = wrapWidth;
            }

            var textHeight = rowText.NameHeight;
            var textPos = new Vector2(
                ImGui.GetItemRectMin().X + Scale(12f),
                ImGui.GetItemRectMin().Y + MathF.Max(0f, (rowHeight - textHeight) * 0.5f));
            var lineHeight = ImGui.GetTextLineHeight();
            var drawList = ImGui.GetWindowDrawList();
            var wrappedLines = rowText.NameLines;
            for (var i = 0; i < wrappedLines.Count; i++)
            {
                drawList.AddText(
                    new Vector2(textPos.X, textPos.Y + (i * lineHeight)),
                    textColor,
                    wrappedLines[i]);
            }

            DrawVenueRowMarkers(venue, textPos.X + cellWidth, ImGui.GetItemRectMin().Y, rowHeight);

            // Right-click on the row (the selectable is still the last item here).
            DrawVenueRowContextMenu(venue);
        }

        ImGui.TableNextColumn();
        var addressWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
        var rowAddress = GetVenueRowText(venue);
        if (MathF.Abs(rowAddress.AddressWidth - addressWidth) > 0.5f)
        {
            var addressLines = venue.TableAddress.Split('\n');
            var address = addressLines.Length > 1 ? $"{addressLines[0]} (+{addressLines.Length - 1})" : venue.TableAddress;
            rowAddress.Address = FitToWidth(address, addressWidth);
            rowAddress.AddressWidth = addressWidth;
        }

        CenterCursorForRow(rowHeight, ImGui.GetTextLineHeight());
        DrawText(rowAddress.Address, UiStyle.BodyMutedText);

        // The list shows world and plot only; the full address is one hover away.
        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()) && ImGui.IsWindowHovered())
        {
            ImGui.SetTooltip(venue.DetailedAddress);
        }
        ImGui.TableNextColumn();
        var badgeWidth = UiStyle.ListSizeBadgeWidth;
        var available = MathF.Max(0f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
        var centeredOffset = MathF.Max(0f, (available - badgeWidth) * 0.5f);
        CenterCursorForRow(rowHeight, UiStyle.BadgeSide);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centeredOffset);
        DrawSizeBadge(venue.VenueTypeLabel, rowAddress.BadgeId);
        ImGui.TableNextColumn();
        CenterCursorForRow(rowHeight, ImGui.GetTextLineHeight());
        DrawVenueStatus(venue);
    }

    // Wide enough for the longest status line FormatStatusLine produces and for the line of a venue open by a Party Finder ad.
    private static float MeasureStatusColumnWidth()
    {
        var width = 0f;
        foreach (var sample in Use12HourClock ? StatusColumnSamples12Hour : StatusColumnSamples)
        {
            width = MathF.Max(width, ImGui.CalcTextSize(sample).X);
        }

        return width + UiStyle.ListStatusColumnTextReserve;
    }

    private static readonly string[] StatusColumnSamples = ["Opens Sep 30 00:00", "Opens May 30 00:00", "Opens in 2 h 59 min", "Open until Wed 00:00", "No opening set", VenueStatus.AdvertisedLine];
    private static readonly string[] StatusColumnSamples12Hour = ["Opens Sep 30 10:00 PM", "Opens May 30 10:00 PM", "Opens in 2 h 59 min", "Open until Wed 10:00 PM", "No opening set", VenueStatus.AdvertisedLine];

    private static List<string> WrapTextToWidth(string text, float wrapWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            lines.Add(string.Empty);
            return lines;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (string.IsNullOrEmpty(current) || ImGui.CalcTextSize(candidate).X <= wrapWidth)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (!string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }

        return lines;
    }

    private static void DrawSizeBadge(string text, string id)
    {
        var chipWidth = UiStyle.ListSizeBadgeWidth;
        var chipSize = new Vector2(chipWidth, UiStyle.BadgeSide);
        DrawStaticChip(id, text, UiChipTone.Accent, chipSize, centerText: true);
    }

    // What a row of the list draws, worked out once per venue and column width (the countdown once a minute) instead of every frame: wrapping the name and cutting the address measure text many times over.
    private sealed class VenueRowText
    {
        public float NameWidth { get; set; } = -1f;

        public List<string> NameLines { get; set; } = [];

        public float NameHeight { get; set; }

        public float MeasuredWidth { get; set; } = -1f;

        public float MeasuredHeight { get; set; }

        public float AddressWidth { get; set; } = -1f;

        public string Address { get; set; } = string.Empty;

        public string BadgeId { get; init; } = string.Empty;

        public long OpensInMinute { get; set; } = -1;

        public string OpensIn { get; set; } = string.Empty;
    }

    private readonly Dictionary<PreparedVenue, VenueRowText> _venueRowTexts = new(ReferenceEqualityComparer.Instance);
    private int _venueRowTextsFontVersion = -1;

    // Measurements belong to a font: another one (interface size, Dalamud's font) starts over. A refreshed list has new venues, so the old ones are let go once there are more than a few lists' worth.
    private VenueRowText GetVenueRowText(PreparedVenue venue)
    {
        if (_venueRowTextsFontVersion != PluginUiFont.Version || _venueRowTexts.Count > 6000)
        {
            _venueRowTexts.Clear();
            _venueRowTextsFontVersion = PluginUiFont.Version;
        }

        if (!_venueRowTexts.TryGetValue(venue, out var text))
        {
            _venueRowTexts[venue] = text = new VenueRowText { BadgeId = $"size_badge_{venue.Id}" };
        }

        return text;
    }

    private static void CenterCursorForRow(float rowHeight, float itemHeight)
    {
        var offset = MathF.Max(0f, (rowHeight - itemHeight) * 0.5f);
        if (offset > 0f)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
        }
    }

    private void DrawVenueStatus(PreparedVenue venue)
    {
        var text = venue.StatusLine;
        Vector4 color;
        if (venue.IsOpen)
        {
            color = UiStyle.PositiveText;
        }
        else if (IsOpeningSoon(venue, DateTimeOffset.Now))
        {
            var rowText = GetVenueRowText(venue);
            var now = DateTimeOffset.UtcNow;
            var minute = now.ToUnixTimeSeconds() / 60;
            if (rowText.OpensInMinute != minute)
            {
                rowText.OpensIn = FormatOpensIn(venue.Status.Opening!.Value.Start - now);
                rowText.OpensInMinute = minute;
            }

            text = rowText.OpensIn;
            color = UiStyle.SoonText;
        }
        else
        {
            color = venue.Status.Opening == null ? UiStyle.BodySubtleText : UiStyle.BodyText;
        }

        var defaultInset = ImGui.GetStyle().CellPadding.X;
        var insetAdjustment = MathF.Max(0f, defaultInset - UiStyle.ListStatusHorizontalInset);
        if (insetAdjustment > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - insetAdjustment);
        }

        DrawText(text, color);
    }

    // A gold star for favorites and a green check for visited venues, right-aligned in the venue cell.
    private void DrawVenueRowMarkers(PreparedVenue venue, float rightX, float rowTop, float rowHeight)
    {
        var isFavorite = IsPreferredVenue(_favoriteVenueIds, venue.Id);
        var isVisited = IsPreferredVenue(_visitedVenueIds, venue.Id);
        var hasEvents = TryGetUpcomingVenueEvents(venue.Id, out var upcoming, out var liveEvent);
        if (!isFavorite && !isVisited && !hasEvents)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        using (ImRaii.PushFont(PluginUiFont.IconFont))
        {
            var x = rightX;
            if (hasEvents)
            {
                // Partake events at this venue: green while one is on, pink otherwise; a venue that only advertises in the Party Finder gets the ads' bullhorn.
                var onlyAds = upcoming.TrueForAll(e => e.Source == EventSource.PartyFinder || e.EndsAt <= DateTimeOffset.Now);
                var icon = IconText(onlyAds ? FontAwesomeIcon.Bullhorn : FontAwesomeIcon.CalendarAlt);
                var size = ImGui.CalcTextSize(icon);
                x -= size.X;
                drawList.AddText(new Vector2(x, rowTop + (rowHeight - size.Y) * 0.5f), ImGui.GetColorU32(liveEvent ? UiStyle.PositiveText : UiStyle.EventText), icon);
                x -= UiStyle.InlineSpacing;
            }

            if (isVisited)
            {
                var icon = IconText(FontAwesomeIcon.Check);
                var size = ImGui.CalcTextSize(icon);
                x -= size.X;
                drawList.AddText(new Vector2(x, rowTop + (rowHeight - size.Y) * 0.5f), ImGui.GetColorU32(UiStyle.PositiveText), icon);
                x -= UiStyle.InlineSpacing;
            }

            if (isFavorite)
            {
                var icon = IconText(FontAwesomeIcon.Star);
                var size = ImGui.CalcTextSize(icon);
                x -= size.X;
                drawList.AddText(new Vector2(x, rowTop + (rowHeight - size.Y) * 0.5f), ImGui.GetColorU32(UiStyle.FavoriteText), icon);
            }
        }
    }

    private void DrawVenueRowContextMenu(PreparedVenue venue)
    {
        using var menu = ImRaii.ContextPopupItem("VenueRowMenu");
        if (!menu)
        {
            return;
        }

        var isFavorite = IsPreferredVenue(_favoriteVenueIds, venue.Id);
        if (ImGui.MenuItem(isFavorite ? "Remove from favorites" : "Add to favorites"))
        {
            SetPreferredVenue(_configuration.FavoriteVenueIds, _favoriteVenueIds, venue.Id, !isFavorite);
        }

        var isVisited = IsPreferredVenue(_visitedVenueIds, venue.Id);
        if (ImGui.MenuItem(isVisited ? "Unmark as visited" : "Mark as visited"))
        {
            SetPreferredVenue(_configuration.VisitedVenueIds, _visitedVenueIds, venue.Id, !isVisited);
        }

        var isHidden = _hiddenVenueIds.Contains(venue.Id);
        if (ImGui.MenuItem(isHidden ? "Show in the list again" : "Hide from the list"))
        {
            SetVenueHidden(venue, !isHidden);
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Copy address"))
        {
            ImGui.SetClipboardText(venue.DetailedAddress);
        }
    }

    private void DrawVenueTableEmptyState()
    {
        DrawSection("VenueListEmptyState", UiStyle.FilterGroupBackground, () =>
        {
            DrawText(GetEmptySelectionMessage(), UiStyle.BodyStrongText);
            DrawVerticalRhythm(0.35f);
            DrawMutedText(_filters.HiddenOnly
                ? "Hide a venue with the eye button in its details or from its right-click menu."
                : "Adjust the active filters or refresh the venue list.");
        });
    }
}
