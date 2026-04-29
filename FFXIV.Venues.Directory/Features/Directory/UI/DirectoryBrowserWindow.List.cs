using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;

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

        var isNarrowLayout = _wasNarrowLayoutLastDraw;
        var wideStatusColumnWidth = MathF.Max(
            UiStyle.ListStatusColumnWidth,
            ImGui.CalcTextSize("Open until 00:00").X + UiStyle.ListStatusColumnTextReserve);
        var flags = ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                    (isNarrowLayout ? ImGuiTableFlags.SizingFixedFit : ImGuiTableFlags.SizingStretchProp) |
                    ImGuiTableFlags.Sortable |
                    ImGuiTableFlags.NoSavedSettings;
        var size = ImGui.GetContentRegionAvail();
        var venueWrapWidth = 0f;
        var addressWrapWidth = 0f;
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
            ImGui.TableSetupColumn(
                "Venue",
                (isNarrowLayout ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch) | ImGuiTableColumnFlags.DefaultSort,
                isNarrowLayout ? UiStyle.ListNarrowVenueColumnWidth : UiStyle.ListVenueColumnWeight);
            ImGui.TableSetupColumn(
                "Address",
                ImGuiTableColumnFlags.WidthFixed,
                isNarrowLayout ? UiStyle.ListNarrowAddressColumnWidth : UiStyle.ListAddressColumnWidth);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, UiStyle.ListSizeColumnWidth);
            ImGui.TableSetupColumn(
                "Status",
                ImGuiTableColumnFlags.WidthFixed,
                isNarrowLayout ? UiStyle.ListNarrowStatusColumnWidth : wideStatusColumnWidth);
            using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.ListHeaderText))
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableSetColumnIndex(0);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Scale(12f));
                ImGui.TableHeader("Venue");
                ImGui.TableNextColumn();
                ImGui.TableHeader("Address");
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

            ImGui.TableSetColumnIndex(0);
            venueWrapWidth = isNarrowLayout
                ? MathF.Max(1f, UiStyle.ListNarrowVenueColumnWidth - ImGui.GetStyle().CellPadding.X * 2f)
                : MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
            ImGui.TableNextColumn();
            var addressColumnWidth = isNarrowLayout
                ? MathF.Max(1f, UiStyle.ListNarrowAddressColumnWidth - ImGui.GetStyle().CellPadding.X * 2f)
                : MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
            addressWrapWidth = MathF.Max(1f, addressColumnWidth);
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
        if (_favoritesOnly && _visitedOnly)
        {
            return "You have no favorite or visited venues yet. Add or mark some first.";
        }

        if (_favoritesOnly)
        {
            return "You have no favorite venues yet. Add some first.";
        }

        if (_visitedOnly)
        {
            return "You have no visited venues yet. Mark some first.";
        }

        return "Select a venue from the list to see its details.";
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
            var venueHeight = ImGui.CalcTextSize(venue.DisplayName, false, venueWrapWidth).Y;
            var addressHeight = ImGui.CalcTextSize(venue.TableAddress, false, addressWrapWidth).Y;
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
        ImGui.TableSetColumnIndex(0);
        ImGui.Dummy(new Vector2(1f, height));
    }

    private void DrawVenueTableRow(PreparedVenue venue, int rowIndex, float rowHeight)
    {
        var isSelected = string.Equals(_selectedVenueId, venue.Id, StringComparison.Ordinal);

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        ImGui.TableSetColumnIndex(0);
        using (ImRaii.PushId(rowIndex))
        {
            using var rowHighlight = ImRaii.PushColor(ImGuiCol.Header, UiStyle.SelectedRowBackground)
                .Push(ImGuiCol.HeaderHovered, UiStyle.SelectedRowHoveredBackground)
                .Push(ImGuiCol.HeaderActive, UiStyle.SelectedRowActiveBackground);
            if (ImGui.Selectable("##VenueRow", isSelected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight)))
            {
                _selectedVenueId = venue.Id;
            }

            var textColor = isSelected
                ? ResolveTextColorU32(UiStyle.BodyStrongText)
                : ResolveTextColorU32();
            var wrapWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
            var textHeight = ImGui.CalcTextSize(venue.DisplayName, false, wrapWidth).Y;
            var textPos = new Vector2(
                ImGui.GetItemRectMin().X + Scale(12f),
                ImGui.GetItemRectMin().Y + MathF.Max(0f, (rowHeight - textHeight) * 0.5f));
            var lineHeight = ImGui.GetTextLineHeight();
            var drawList = ImGui.GetWindowDrawList();
            var wrappedLines = WrapTextToWidth(venue.DisplayName, wrapWidth);
            for (var i = 0; i < wrappedLines.Count; i++)
            {
                drawList.AddText(
                    new Vector2(textPos.X, textPos.Y + (i * lineHeight)),
                    textColor,
                    wrappedLines[i]);
            }
        }

        ImGui.TableNextColumn();
        using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.BodyMutedText))
        {
            var addressWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f - Scale(18f));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + addressWidth);
            try
            {
                ImGui.TextWrapped(venue.TableAddress);
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }
        }
        ImGui.TableNextColumn();
        var badgeWidth = GetSizeBadgeWidth(venue.VenueTypeLabel);
        var available = MathF.Max(0f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
        var centeredOffset = MathF.Max(0f, (available - badgeWidth) * 0.5f);
        CenterCursorForRow(rowHeight, UiStyle.BadgeSide);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centeredOffset);
        DrawSizeBadge(venue.VenueTypeLabel, $"size_{venue.Id}");
        ImGui.TableNextColumn();
        CenterCursorForRow(rowHeight, ImGui.GetTextLineHeight());
        DrawVenueStatus(venue);
    }

    private static float GetSizeBadgeWidth(string text) =>
        UiStyle.ListSizeBadgeWidth;

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

    private static void DrawSizeBadge(string text, string idSuffix)
    {
        var chipWidth = GetSizeBadgeWidth(text);
        var chipSize = new Vector2(chipWidth, UiStyle.BadgeSide);
        DrawStaticChip($"size_badge_{idSuffix}", text, UiChipTone.Accent, chipSize, centerText: true);
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
        var color = venue.IsOpen
            ? UiStyle.PositiveText
            : string.Equals(venue.StatusLine, "No opening set", StringComparison.OrdinalIgnoreCase)
                ? UiStyle.BodySubtleText
                : UiStyle.BodyText;

        var defaultInset = ImGui.GetStyle().CellPadding.X;
        var insetAdjustment = MathF.Max(0f, defaultInset - UiStyle.ListStatusHorizontalInset);
        if (insetAdjustment > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - insetAdjustment);
        }

        DrawText(venue.StatusLine, color);
    }

    private void DrawVenueTableEmptyState()
    {
        DrawSection("VenueListEmptyState", UiStyle.FilterGroupBackground, () =>
        {
            DrawText(GetEmptySelectionMessage(), UiStyle.BodyStrongText);
            DrawVerticalRhythm(0.35f);
            DrawMutedText("Adjust the active filters or refresh the venue list.");
        });
    }
}
