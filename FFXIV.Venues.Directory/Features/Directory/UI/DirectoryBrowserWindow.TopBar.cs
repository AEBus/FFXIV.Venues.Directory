using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Net;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// The top bar: the Venues and Events tabs, the Filters button (or popover), search, how fresh the list is, refresh and settings, and under it the undo chip after hiding a venue and the active filter chips.
internal sealed partial class DirectoryBrowserWindow
{
    private const string FilterPopoverId = "VenueFiltersPopover";

    private static readonly TimeSpan HideUndoWindow = TimeSpan.FromSeconds(10);

    private string? _lastHiddenVenueId;

    private string _lastHiddenVenueName = string.Empty;

    private DateTimeOffset _lastHiddenAt;

    // One row above the panes: the filters button, search, the venue count and refresh. Below it, for a few seconds, an undo for the last hidden venue and, without the sidebar, the active filters as removable chips, so a short list is never left unexplained.
    private void DrawTopBar(PostPreparationActivationStage activationStage)
    {
        if (!EventsEnabled && _mode == DirectoryMode.Events)
        {
            _mode = DirectoryMode.Venues;
        }

        if (EventsEnabled)
        {
            var tabHeight = MeasureActionButtonSize(FontAwesomeIcon.Filter, "Filters").Y;
            var venueTotal = (_venues?.Length ?? 0) + (UnlistedVenuesShown ? _eventVenueMatches.UnlistedVenues.Length : 0);
            if (DrawTab("VenuesTab", FontAwesomeIcon.Store, "Venues", venueTotal > 0 ? venueTotal.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, false, _mode == DirectoryMode.Venues, tabHeight))
            {
                SetMode(DirectoryMode.Venues);
            }

            ImGui.SameLine(0f, UiStyle.InlineSpacing);
            var live = LiveEventCount;
            var eventBadge = live > 0 ? $"{live} live" : _preparedEvents.Length > 0 ? _preparedEvents.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            if (DrawTab("EventsTab", FontAwesomeIcon.CalendarAlt, "Events", eventBadge, live > 0, _mode == DirectoryMode.Events, tabHeight))
            {
                SetMode(DirectoryMode.Events);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(PartakeEnabled && PartyFinderEnabled ? "Events from Partake and venue ads from the Party Finder"
                    : PartakeEnabled ? "Events from Partake"
                    : "Venue ads from the Party Finder");
            }

            ImGui.SameLine(0f, UiStyle.InlineGroupSpacing);
        }

        CollectActiveFilters();
        var filterCount = _activeFilters.Count;
        var windowBottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y;

        var filtersLabel = !_filterSidebarShown && filterCount > 0 ? $"Filters ({filterCount})" : "Filters";
        var filtersTone = !_filterSidebarShown && filterCount > 0 ? UiButtonTone.Primary : UiButtonTone.Secondary;
        if (DrawActionButton(FontAwesomeIcon.Filter, filtersLabel, filtersTone))
        {
            if (_filterSidebarFits)
            {
                _configuration.FilterSidebarHidden = _filterSidebarShown;
                _configuration.Save(DalamudServices.PluginInterface);
            }
            else
            {
                ImGui.OpenPopup(FilterPopoverId);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_filterSidebarShown
                ? "Hide filters"
                : _filterSidebarFits
                    ? "Show filters"
                    : "Show filters (a wider window keeps them open at the side)");
        }

        var popoverPos = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y + UiStyle.InlineSpacing);
        DrawFilterPopover(popoverPos, windowBottom - popoverPos.Y, activationStage);

        string status;
        string? statusProblem;
        if (_mode == DirectoryMode.Events)
        {
            var source = EventSourceState();
            status = !source.Loaded
                ? source.Error != null ? $"{source.Name} unavailable" : "Loading events..."
                : $"{_filteredEvents.Count} / {_preparedEvents.Length} events · {source.Freshness}";
            statusProblem = source.Loaded ? source.Error : null;
            if (PartakeEnabled && PartyFinderEnabled && _partyFinderFeed.Error is { } partyFinderError)
            {
                statusProblem = statusProblem == null ? partyFinderError : $"{statusProblem}\n{partyFinderError}";
            }
        }
        else
        {
            var venueCount = $"{_filteredVenues.Count} / {(_venues?.Length ?? 0) + (UnlistedVenuesShown ? _eventVenueMatches.UnlistedVenues.Length : 0)} venues";
            status = _venueFeed.Value == null ? venueCount : $"{venueCount} · {DescribeFreshness(_venueFeed)}";
            statusProblem = _venueFeed.Value != null ? _venueFeed.Error : null;
        }
        var refreshWidth = MeasureIconActionButtonSize(FontAwesomeIcon.SyncAlt).X;
        var settingsWidth = MeasureIconActionButtonSize(FontAwesomeIcon.Cog).X;
        var rightBlockWidth = ImGui.CalcTextSize(status).X + UiStyle.InlineGroupSpacing + refreshWidth +
                              UiStyle.InlineSpacing + settingsWidth;

        ImGui.SameLine(0f, UiStyle.InlineGroupSpacing);
        var searchWidth = MathF.Min(
            Scale(420f),
            ImGui.GetContentRegionAvail().X - rightBlockWidth - UiStyle.InlineGroupSpacing);

        // Frame padding as tall as the action buttons, so the search field and the count line up with them.
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, UiStyle.ActionButtonPadding.Y))
                   .Push(ImGuiStyleVar.FrameRounding, UiStyle.CardRounding))
        {
            ImGui.SetNextItemWidth(MathF.Max(Scale(160f), searchWidth));
            var hint = _mode == DirectoryMode.Events ? "Search events, hosts, places or tags..." : "Search by name, description or tag...";
            if (ImGui.InputTextWithHint("##VenueSearch", hint, ref _searchText, 160))
            {
                InvalidateFilteredVenues();
            }

            ImGui.SameLine(0f, UiStyle.InlineGroupSpacing);
            AlignCursorRight(rightBlockWidth);
            ImGui.AlignTextToFramePadding();
            DrawText(status, statusProblem != null ? UiStyle.SoonText : UiStyle.BodyMutedText);
            if (statusProblem != null && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{statusProblem}\nShowing the list from the last update.");
            }
        }

        ImGui.SameLine(0f, UiStyle.InlineGroupSpacing);
        if (DrawIconActionButton("TopBarRefresh", FontAwesomeIcon.SyncAlt, UiButtonTone.Secondary, "Refresh"))
        {
            if (_mode == DirectoryMode.Events)
            {
                RefreshEvents();
            }
            else
            {
                TriggerRefresh();
            }
        }

        ImGui.SameLine(0f, UiStyle.InlineSpacing);
        if (DrawIconActionButton("TopBarSettings", FontAwesomeIcon.Cog, UiButtonTone.Secondary, "Settings"))
        {
            OpenSettings();
        }

        if (!string.IsNullOrEmpty(_loadError))
        {
            DrawText(_loadError, UiStyle.ErrorText);
        }

        var showUndo = _lastHiddenVenueId != null && DateTimeOffset.UtcNow - _lastHiddenAt < HideUndoWindow;
        var showFilterChips = !_filterSidebarShown && filterCount > 0;
        if (showUndo || showFilterChips || _venueHere != null)
        {
            DrawTopBarChips(showUndo, showFilterChips);
        }
    }

    // Describes how fresh the list is: when it was updated, that a refresh is running, or when a failed one is retried.
    private static string DescribeFreshness<T>(PeriodicFeed<T> feed)
        where T : class =>
        feed.IsLoading ? "Updating..."
        : feed.Error != null ? $"Update failed, retrying {FormatRetryIn(feed.RetryAt)}"
        : $"Updated {FormatRelativeTime(feed.LoadedAt)}";

    // The same filter cards as the sidebar, under the Filters button, when the window is too narrow for the sidebar.
    private void DrawFilterPopover(Vector2 position, float maxHeight, PostPreparationActivationStage activationStage)
    {
        if (!ImGui.IsPopupOpen(FilterPopoverId))
        {
            return;
        }

        ImGui.SetNextWindowPos(position);
        using var popup = ImRaii.Popup(FilterPopoverId);
        if (!popup)
        {
            return;
        }

        // The window was widened while the popover was open: the sidebar takes over.
        if (_filterSidebarShown)
        {
            ImGui.CloseCurrentPopup();
            return;
        }

        var available = MathF.Max(Scale(160f), maxHeight - ImGui.GetStyle().WindowPadding.Y * 2f - UiStyle.InlineSpacing);
        var height = _filterPopoverContentHeight > 0f
            ? MathF.Min(_filterPopoverContentHeight, available)
            : available;
        using (var body = ImRaii.Child("VenueFiltersPopoverBody"u8, new Vector2(Scale(FilterSidebarFixedWidth), height), false))
        {
            if (body)
            {
                DrawFilterPaneContent(activationStage);
                _filterPopoverContentHeight = ImGui.GetCursorPosY();
            }
        }
    }

    private void DrawTopBarChips(bool showUndo, bool showFilterChips)
    {
        var rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var first = true;
        DrawHereChips(rightEdge, ref first);
        if (showUndo)
        {
            var label = $"Hid {_lastHiddenVenueName} · Undo";
            var size = MeasureChipButton(label, FontAwesomeIcon.Undo);
            PlaceWrapped(size.X, rightEdge, ref first);
            if (DrawChipButton(-1, label, FontAwesomeIcon.Undo, size, UiChipTone.Neutral, "Show this venue in the list again"))
            {
                UndoLastHide();
            }
        }

        if (!showFilterChips)
        {
            return;
        }

        var chipCount = _activeFilters.Count > 1 ? _activeFilters.Count + 1 : _activeFilters.Count;
        ActiveFilterKind? removed = null;
        var clearAll = false;
        for (var i = 0; i < chipCount; i++)
        {
            var isClearAll = i == _activeFilters.Count;
            var label = isClearAll ? "Clear all" : _activeFilters[i].Label;
            var size = MeasureChipButton(label, FontAwesomeIcon.Times);
            PlaceWrapped(size.X, rightEdge, ref first);
            var tone = isClearAll ? UiChipTone.Neutral : UiChipTone.Accent;
            var tooltip = isClearAll ? "Clear all filters" : "Remove this filter";
            if (DrawChipButton(i, label, FontAwesomeIcon.Times, size, tone, tooltip))
            {
                if (isClearAll)
                {
                    clearAll = true;
                }
                else
                {
                    removed = _activeFilters[i].Kind;
                }
            }
        }

        if (clearAll)
        {
            ClearAllFilters();
        }
        else if (removed is { } kind)
        {
            ClearFilter(kind);
        }
    }

}
