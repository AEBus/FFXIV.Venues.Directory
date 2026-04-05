using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{


    private void DrawToolbar(int visibleCount)
    {
        var refreshButtonSize = MeasureIconActionButtonSize(FontAwesomeIcon.SyncAlt);
        using (var toolbarTable = ImRaii.Table(
                   "FilterToolbarLayout"u8,
                   2,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.NoPadInnerX))
        {
            if (toolbarTable)
            {
                ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Refresh", ImGuiTableColumnFlags.WidthFixed, refreshButtonSize.X + UiStyle.ToolbarButtonInset);

                ImGui.TableNextColumn();
                DrawText($"{visibleCount} / {_venues?.Length ?? 0} venues", UiStyle.DisplayTitleText);
                DrawVerticalRhythm(0.15f);
                DrawMutedText(_lastRefresh == default
                    ? "Updated -"
                    : $"Updated {FormatRelativeTime(_lastRefresh)}");

                ImGui.TableNextColumn();
                AlignCursorRight(refreshButtonSize.X);
                if (DrawIconActionButton("FilterToolbarRefresh", FontAwesomeIcon.SyncAlt, UiButtonTone.Secondary, "Refresh"))
                {
                    TriggerRefresh();
                }
            }
            else
            {
                DrawText($"{visibleCount} / {_venues?.Length ?? 0} venues", UiStyle.DisplayTitleText);
                DrawVerticalRhythm(0.15f);
                DrawMutedText(_lastRefresh == default
                    ? "Updated -"
                    : $"Updated {FormatRelativeTime(_lastRefresh)}");
                ImGui.SameLine(0f, Scale(8f));
                if (DrawIconActionButton("FilterToolbarRefreshFallback", FontAwesomeIcon.SyncAlt, UiButtonTone.Secondary, "Refresh"))
                {
                    TriggerRefresh();
                }
            }
        }

        if (!string.IsNullOrEmpty(_loadError))
        {
            DrawVerticalRhythm(0.2f);
            DrawText(_loadError, UiStyle.ErrorText);
        }
    }

    private void DrawFilters()
    {
        DrawFilterGroupCard(
            "SearchFilterCard",
            "Search",
            string.Empty,
            DrawSearchFilter,
            accentTitle: true);

        DrawFilterGroupCard(
            "QuickFilterCard",
            "Quick Filters",
            string.Empty,
            DrawFilterToggles);

        DrawFilterGroupCard(
            "SizeFilterCard",
            "House Size",
            string.Empty,
            DrawSizeFilter);

        DrawFilterGroupCard(
            "LocationFilterCard",
            "Location",
            string.Empty,
            DrawLocationFilters);

        DrawFilterGroupCard(
            "TagFilterCard",
            "Tags",
            string.Empty,
            DrawTagFilters);
    }

    private void DrawSearchFilter()
    {
        var fieldWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - Scale(12f));
        using (ImRaii.ItemWidth(fieldWidth))
        {
            if (ImGui.InputTextWithHint("##VenueSearch", "Search by name, description or tag...", ref _searchText, 160))
            {
                InvalidateFilteredVenues();
            }
        }
    }

    private void DrawSizeFilter()
    {
        var sizeFilterOuterInset = MathF.Max(
            0f,
            (MathF.Max(0f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X) -
             (UiStyle.QuickFilterButtonWidth * 3f + UiStyle.FilterButtonGap * 2f)) * 0.5f);
        DrawCenteredDualButtonRow(
            UiStyle.FilterSplitButtonGap,
            sizeFilterOuterInset,
            width => DrawApartmentSizeToggle(width),
            width => DrawSmallSizeToggle(width));
        DrawExactVerticalGap(UiStyle.FilterRowSpacing);
        DrawCenteredDualButtonRow(
            UiStyle.FilterSplitButtonGap,
            sizeFilterOuterInset,
            width => DrawMediumSizeToggle(width),
            width => DrawLargeSizeToggle(width));
    }

    private void DrawSizeToggle(string label, ref bool flag, float width = 0f)
    {
        if (DrawToggleChip($"SizeToggle::{label}", label, flag, width))
        {
            flag = !flag;
            if (!_sizeApartment && !_sizeSmall && !_sizeMedium && !_sizeLarge)
            {
                flag = true;
            }

            InvalidateFilteredVenues();
        }
    }

    private void DrawFilterToggles()
    {
        DrawCenteredFixedWidthRow(
            UiStyle.QuickFilterButtonWidth,
            UiStyle.FilterButtonGap,
            width => DrawOpenNowToggle(width),
            width => DrawSfwOnlyToggle(width),
            width => DrawNsfwOnlyToggle(width));
        DrawExactVerticalGap(UiStyle.FilterRowSpacing);
        DrawCenteredFixedWidthRow(
            UiStyle.QuickFilterButtonWidth,
            UiStyle.FilterButtonGap,
            width => DrawFavoriteOnlyToggle(width),
            width => DrawVisitedOnlyToggle(width));
    }

    private void DrawLocationFilters()
    {
        var fieldWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - Scale(12f));
        using (ImRaii.ItemWidth(fieldWidth))
        {
            using (var regionCombo = ImRaii.Combo("##Region"u8, (_selectedRegion ?? "Any Region")))
            {
                if (regionCombo)
                {
                    if (ImGui.Selectable("Any Region", _selectedRegion == null))
                    {
                        SetRegion(null);
                    }

                    ImGui.Separator();
                    foreach (var region in _regions)
                    {
                        if (ImGui.Selectable(region, string.Equals(region, _selectedRegion, StringComparison.OrdinalIgnoreCase)))
                        {
                            SetRegion(region);
                        }
                    }
                }
            }

            DrawVerticalRhythm(0.25f);

            using (var dataCenterCombo = ImRaii.Combo("##DataCenter"u8, (_selectedDataCenter ?? "Any Data Center")))
            {
                if (dataCenterCombo)
                {
                    if (ImGui.Selectable("Any Data Center", _selectedDataCenter == null))
                    {
                        SetDataCenter(null);
                    }

                    ImGui.Separator();
                    foreach (var dc in GetRegionDataCenters())
                    {
                        if (ImGui.Selectable(dc, string.Equals(dc, _selectedDataCenter, StringComparison.OrdinalIgnoreCase)))
                        {
                            SetDataCenter(dc);
                        }
                    }
                }
            }

            DrawVerticalRhythm(0.25f);

            using (var worldCombo = ImRaii.Combo("##World"u8, (_selectedWorld ?? "Any World")))
            {
                if (worldCombo)
                {
                    if (ImGui.Selectable("Any World", _selectedWorld == null))
                    {
                        _selectedWorld = null;
                        InvalidateFilteredVenues();
                    }

                    ImGui.Separator();
                    foreach (var world in _worlds)
                    {
                        if (ImGui.Selectable(world, string.Equals(world, _selectedWorld, StringComparison.OrdinalIgnoreCase)))
                        {
                            _selectedWorld = world;
                            InvalidateFilteredVenues();
                        }
                    }
                }
            }
        }
    }

    private void DrawTagFilters()
    {
        var fieldWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - Scale(12f));
        using (ImRaii.ItemWidth(fieldWidth))
        {
            if (ImGui.InputTextWithHint("##TagFilter", "Enter tags, separated by commas", ref _tagFilter, 128))
            {
                InvalidateFilteredVenues();
            }
        }
    }

    private void DrawFilterGroupCard(string id, string title, string description, Action content, bool accentTitle = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var contentWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var bg = ImGui.GetColorU32(UiStyle.FilterGroupBackground);
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        var padding = UiStyle.CardPadding;

        using var sectionId = ImRaii.PushId(id.GetHashCode(StringComparison.Ordinal));
        drawList.ChannelsSplit(2);
        try
        {
            drawList.ChannelsSetCurrent(1);
            using (ImRaii.Group())
            {
                DrawVerticalRhythm(0.25f);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding.X);
                var wrapRightEdge = ImGui.GetCursorPosX() + MathF.Max(0f, contentWidth - padding.X * 2f);
                ImGui.PushTextWrapPos(wrapRightEdge);
                try
                {
                    using (ImRaii.Group())
                    using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.BodyText))
                    {
                        DrawFilterGroupHeader(title, description, accentTitle);
                        DrawVerticalRhythm(0.25f);
                        content();
                    }
                }
                finally
                {
                    ImGui.PopTextWrapPos();
                }

                DrawVerticalRhythm(0.25f);
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

        DrawVerticalRhythm(0.75f);
    }

    private void DrawFilterGroupHeader(string title, string description, bool accentTitle)
    {
        DrawSectionHeader(title, accentTitle ? UiStyle.DisplayTitleText : UiStyle.SectionHeaderText);
        if (!string.IsNullOrWhiteSpace(description))
        {
            DrawVerticalRhythm(0.15f);
            DrawMutedText(description);
        }
    }

    private void DrawOpenNowToggle(float width = 0f)
    {
        if (DrawToggleChip("OpenNowFilter", FontAwesomeIcon.Clock, "Open", _onlyOpen, width))
        {
            _onlyOpen = !_onlyOpen;
            InvalidateFilteredVenues();
        }
    }

    private void DrawFavoriteOnlyToggle(float width = 0f)
    {
        if (DrawToggleChip("FavoriteFilter", FontAwesomeIcon.Star, "Favorite", _favoritesOnly, width))
        {
            _favoritesOnly = !_favoritesOnly;
            InvalidateFilteredVenues();
        }
    }

    private void DrawVisitedOnlyToggle(float width = 0f)
    {
        if (DrawToggleChip("VisitedFilter", FontAwesomeIcon.Check, "Visited", _visitedOnly, width))
        {
            _visitedOnly = !_visitedOnly;
            InvalidateFilteredVenues();
        }
    }

    private void DrawSfwOnlyToggle(float width = 0f)
    {
        if (DrawToggleChip("SfwOnlyFilter", FontAwesomeIcon.ShieldAlt, "SFW", _sfwOnly, width))
        {
            _sfwOnly = !_sfwOnly;
            if (_sfwOnly)
            {
                _nsfwOnly = false;
            }

            InvalidateFilteredVenues();
        }
    }

    private void DrawNsfwOnlyToggle(float width = 0f)
    {
        if (DrawToggleChip("NsfwOnlyFilter", FontAwesomeIcon.Ban, "NSFW", _nsfwOnly, width))
        {
            _nsfwOnly = !_nsfwOnly;
            if (_nsfwOnly)
            {
                _sfwOnly = false;
            }

            InvalidateFilteredVenues();
        }
    }

    private void DrawApartmentSizeToggle(float width = 0f) => DrawSizeToggle("Apartment", ref _sizeApartment, width);

    private void DrawSmallSizeToggle(float width = 0f) => DrawSizeToggle("Small", ref _sizeSmall, width);

    private void DrawMediumSizeToggle(float width = 0f) => DrawSizeToggle("Medium", ref _sizeMedium, width);

    private void DrawLargeSizeToggle(float width = 0f) => DrawSizeToggle("Large", ref _sizeLarge, width);
}
