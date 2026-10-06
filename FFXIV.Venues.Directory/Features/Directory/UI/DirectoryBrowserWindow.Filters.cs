using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenueAddresses;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private const int CollapsedTagCount = 12;
    private static readonly TimeSpan SoonWindow = TimeSpan.FromHours(3);
    private static readonly TimeSpan FiltersSaveDelay = TimeSpan.FromSeconds(1);
    private static readonly string[] TimeFilterLabels = ["Open now", "Soon", "Any time"];
    private static readonly FontAwesomeIcon[] TimeFilterIcons = [FontAwesomeIcon.Clock, FontAwesomeIcon.HourglassHalf, FontAwesomeIcon.CalendarAlt];
    private static readonly string?[] TimeFilterTooltips = [null, "Open now or opening within 3 hours", null];
    private static readonly string[] ContentFilterLabels = ["All", "SFW", "NSFW"];
    private static readonly FontAwesomeIcon[] ContentFilterIcons = [FontAwesomeIcon.LayerGroup, FontAwesomeIcon.ShieldAlt, FontAwesomeIcon.Ban];
    private static readonly string[] VisitedFilterLabels = ["All", "Visited", "Not visited"];
    private static readonly FontAwesomeIcon[] VisitedFilterIcons = [FontAwesomeIcon.LayerGroup, FontAwesomeIcon.Check, FontAwesomeIcon.Compass];

    private enum ActiveFilterKind
    {
        EventTime,
        EventRating,
        EventTags,
        EventFavorites,
        Time,
        Content,
        Favorites,
        Visited,
        Hidden,
        Size,
        Region,
        DataCenter,
        World,
        Tags,
    }

    private enum TagFilterState
    {
        Off,
        Included,
        Excluded,
    }

    private readonly List<(ActiveFilterKind Kind, string Label)> _activeFilters = [];
    private readonly List<(string Tag, int Count)> _tagCounts = [];
    private PreparedVenue[]? _tagCountsSource;
    private bool _showAllTags;

    private void DrawFilters()
    {
        DrawFilterGroupCard("ShowFilterCard", "Show", string.Empty, DrawShowFilters);
        DrawFilterGroupCard("LocationFilterCard", "Location", string.Empty, DrawLocationFilters);
        DrawFilterGroupCard("SizeFilterCard", "House Size", string.Empty, DrawSizeFilter);
        DrawFilterGroupCard("SavedFilterCard", "Saved", string.Empty, DrawSavedFilters);
        DrawFilterGroupCard("TagFilterCard", "Tags", string.Empty, DrawTagFilters);
    }

    private void DrawShowFilters()
    {
        var time = (int)_filters.Time;
        if (DrawSegmented("TimeFilter", TimeFilterLabels, TimeFilterIcons, ref time, TimeFilterTooltips))
        {
            _filters.Time = (VenueTimeFilter)time;
            OnFiltersChanged();
        }

        DrawExactVerticalGap(UiStyle.FilterRowSpacing);
        var content = (int)_filters.Content;
        if (DrawSegmented("ContentFilter", ContentFilterLabels, ContentFilterIcons, ref content))
        {
            _filters.Content = (VenueContentFilter)content;
            OnFiltersChanged();
        }
    }

    private void DrawSavedFilters()
    {
        var hiddenLabel = _hiddenVenueIds.Count > 0 ? $"Hidden ({_hiddenVenueIds.Count})" : "Hidden";
        DrawCenteredDualButtonRow(
            UiStyle.FilterSplitButtonGap,
            0f,
            width =>
            {
                if (DrawToggleChip("FavoritesFilter", FontAwesomeIcon.Star, "Favorites", _filters.FavoritesOnly, width))
                {
                    _filters.FavoritesOnly = !_filters.FavoritesOnly;
                    OnFiltersChanged();
                }
            },
            width =>
            {
                if (DrawToggleChip("HiddenFilter", FontAwesomeIcon.EyeSlash, hiddenLabel, _filters.HiddenOnly, width))
                {
                    _filters.HiddenOnly = !_filters.HiddenOnly;
                    OnFiltersChanged();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Show only the venues you hid, to bring them back");
                }
            });

        DrawExactVerticalGap(UiStyle.FilterRowSpacing);
        var visited = (int)_filters.Visited;
        if (DrawSegmented("VisitedFilter", VisitedFilterLabels, VisitedFilterIcons, ref visited))
        {
            _filters.Visited = (VenueVisitedFilter)visited;
            OnFiltersChanged();
        }

        if (EventsEnabled)
        {
            var includePartake = _filters.IncludeUnlistedVenues;
            if (ImGui.Checkbox($"Venues not on FFXIV Venues ({_eventVenueMatches.UnlistedVenues.Length})", ref includePartake))
            {
                _filters.IncludeUnlistedVenues = includePartake;
                OnFiltersChanged();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Venues known only from their Partake events or Party Finder ads");
            }
        }
    }

    private void DrawLocationFilters()
    {
        var fieldWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - Scale(12f));
        var myLabel = _currentDataCenter != null ? $"My data center · {_currentDataCenter}" : "My data center";
        if (DrawToggleChip("MyDataCenterFilter", FontAwesomeIcon.LocationArrow, myLabel, _filters.MyDataCenter, fieldWidth))
        {
            SetMyDataCenter(!_filters.MyDataCenter);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_currentDataCenter != null
                ? "Venues and events on the data center your character is on, following it when you travel"
                : "Venues and events on the data center your character is on (once logged in)");
        }

        DrawVerticalRhythm(0.25f);
        using (ImRaii.ItemWidth(fieldWidth))
        {
            using (ImRaii.Disabled(_filters.MyDataCenter))
            using (var regionCombo = ImRaii.Combo("##Region"u8, _filters.Region ?? "Any Region"))
            {
                if (regionCombo)
                {
                    if (ImGui.Selectable("Any Region", _filters.Region == null))
                    {
                        SetRegion(null);
                    }

                    ImGui.Separator();
                    foreach (var region in _regions)
                    {
                        if (ImGui.Selectable(region, string.Equals(region, _filters.Region, StringComparison.OrdinalIgnoreCase)))
                        {
                            SetRegion(region);
                        }
                    }
                }
            }

            DrawVerticalRhythm(0.25f);

            using (ImRaii.Disabled(_filters.MyDataCenter))
            using (var dataCenterCombo = ImRaii.Combo("##DataCenter"u8, _filters.DataCenter ?? "Any Data Center"))
            {
                if (dataCenterCombo)
                {
                    if (ImGui.Selectable("Any Data Center", _filters.DataCenter == null))
                    {
                        SetDataCenter(null);
                    }

                    ImGui.Separator();
                    foreach (var dc in GetRegionDataCenters())
                    {
                        if (ImGui.Selectable(dc, string.Equals(dc, _filters.DataCenter, StringComparison.OrdinalIgnoreCase)))
                        {
                            SetDataCenter(dc);
                        }
                    }
                }
            }

            DrawVerticalRhythm(0.25f);

            using (var worldCombo = ImRaii.Combo("##World"u8, _filters.World ?? "Any World"))
            {
                if (worldCombo)
                {
                    if (ImGui.Selectable("Any World", _filters.World == null))
                    {
                        _filters.World = null;
                        OnFiltersChanged();
                    }

                    ImGui.Separator();
                    foreach (var world in _worlds)
                    {
                        if (ImGui.Selectable(world, string.Equals(world, _filters.World, StringComparison.OrdinalIgnoreCase)))
                        {
                            _filters.World = world;
                            OnFiltersChanged();
                        }
                    }
                }
            }
        }
    }

    // The most common tags as chips with their venue counts. A click includes a tag, a second click excludes it, a third clears it; chosen tags stay visible when the list is collapsed.
    private void DrawTagFilters()
    {
        EnsureTagCounts();
        if (DrawTagCloud(_tagCounts, _filters.IncludedTags, _filters.ExcludedTags, ref _showAllTags, out var toggled))
        {
            CycleTag(_filters.IncludedTags, _filters.ExcludedTags, toggled!);
            OnFiltersChanged();
        }
    }

    // Returns true when a tag was clicked (toggled names it); the caller moves it to its next state.
    private static bool DrawTagCloud(
        List<(string Tag, int Count)> counts,
        List<string> included,
        List<string> excluded,
        ref bool showAll,
        out string? toggled)
    {
        toggled = null;
        if (counts.Count == 0)
        {
            DrawMutedText("No tags yet.");
            return false;
        }

        var rightEdge = ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X);
        var first = true;
        for (var i = 0; i < counts.Count; i++)
        {
            var (tag, count) = counts[i];
            var state = GetTagFilterState(included, excluded, tag);
            if (!showAll && i >= CollapsedTagCount && state == TagFilterState.Off)
            {
                continue;
            }

            if (DrawTagFilterChip(i, tag, count, state, rightEdge, ref first))
            {
                toggled = tag;
            }
        }

        if (counts.Count > CollapsedTagCount)
        {
            var label = showAll ? "Fewer tags" : AllTagsLabel(counts.Count);
            var icon = showAll ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown;
            var size = MeasureChipButton(label, icon);
            PlaceWrapped(size.X, rightEdge, ref first);
            if (DrawChipButton(-2, label, icon, size, UiChipTone.Neutral, null))
            {
                showAll = !showAll;
            }
        }

        return toggled != null;
    }

    private static bool DrawTagFilterChip(int index, string tag, int count, TagFilterState state, float rightEdge, ref bool first)
    {
        var countText = CountText(count);
        FontAwesomeIcon? icon = state switch
        {
            TagFilterState.Included => FontAwesomeIcon.Check,
            TagFilterState.Excluded => FontAwesomeIcon.Ban,
            _ => null,
        };
        var padding = UiStyle.RemovableChipPadding;
        var gap = UiStyle.InlineSpacing;
        var iconWidth = 0f;
        if (icon is { } shownIcon)
        {
            using (ImRaii.PushFont(PluginUiFont.IconFont))
            {
                iconWidth = ImGui.CalcTextSize(IconText(shownIcon)).X + gap;
            }
        }

        var tagSize = ImGui.CalcTextSize(tag);
        var countWidth = ImGui.CalcTextSize(countText).X;
        var size = new Vector2(iconWidth + tagSize.X + gap + countWidth + padding.X * 2f, tagSize.Y + padding.Y * 2f);
        PlaceWrapped(size.X, rightEdge, ref first);

        var tone = state switch
        {
            TagFilterState.Included => UiChipTone.Accent,
            TagFilterState.Excluded => UiChipTone.Warning,
            _ => UiChipTone.Neutral,
        };
        using var chipId = ImRaii.PushId(index);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, size.Y * 0.5f);
        using var colors = PushChipButtonColors(tone);
        var pressed = ImGui.Button("##TagFilterChip", size);
        var min = ImGui.GetItemRectMin();
        var drawList = ImGui.GetWindowDrawList();
        var x = min.X + padding.X;
        var textY = min.Y + (size.Y - tagSize.Y) * 0.5f;
        if (icon is { } drawnIcon)
        {
            using (ImRaii.PushFont(PluginUiFont.IconFont))
            {
                var iconText = IconText(drawnIcon);
                var iconSize = ImGui.CalcTextSize(iconText);
                drawList.AddText(new Vector2(x, min.Y + (size.Y - iconSize.Y) * 0.5f), ResolveTextColorU32(), iconText);
            }

            x += iconWidth;
        }

        drawList.AddText(new Vector2(x, textY), ResolveTextColorU32(), tag);
        drawList.AddText(new Vector2(x + tagSize.X + gap, textY), ImGui.GetColorU32(UiStyle.BodyMutedText), countText);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(state switch
            {
                TagFilterState.Included => "Only venues with this tag. Click again to hide venues with it.",
                TagFilterState.Excluded => "Venues with this tag are hidden. Click again to clear.",
                _ => "Click to show only venues with this tag.",
            });
        }

        return pressed;
    }

    private static TagFilterState GetTagFilterState(List<string> included, List<string> excluded, string tag)
    {
        if (ContainsTag(included, tag))
        {
            return TagFilterState.Included;
        }

        return ContainsTag(excluded, tag) ? TagFilterState.Excluded : TagFilterState.Off;
    }

    // Off, then included, then excluded, then off again.
    private static void CycleTag(List<string> included, List<string> excluded, string tag)
    {
        switch (GetTagFilterState(included, excluded, tag))
        {
            case TagFilterState.Off:
                included.Add(tag);
                break;
            case TagFilterState.Included:
                RemoveTag(included, tag);
                excluded.Add(tag);
                break;
            default:
                RemoveTag(excluded, tag);
                break;
        }
    }

    // A loop rather than Exists with a lambda: this runs for every tag in every frame.
    private static bool ContainsTag(List<string> tags, string tag)
    {
        foreach (var t in tags)
        {
            if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveTag(List<string> tags, string tag) =>
        tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

    // Tag counts over all venues, most common first; recounted when a new venue list is prepared.
    private void EnsureTagCounts()
    {
        if (ReferenceEquals(_tagCountsSource, _preparedVenues))
        {
            return;
        }

        _tagCountsSource = _preparedVenues;
        _tagCounts.Clear();
        if (_preparedVenues == null)
        {
            return;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var venue in _preparedVenues)
        {
            foreach (var tag in venue.Tags)
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        _tagCounts.AddRange(counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value)));
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
            width => _filters.SizeApartment = DrawSizeToggle("Apartment", FontAwesomeIcon.Building, 1f, _filters.SizeApartment, width),
            width => _filters.SizeSmall = DrawSizeToggle("Small", FontAwesomeIcon.Home, 0.75f, _filters.SizeSmall, width));
        DrawExactVerticalGap(UiStyle.FilterRowSpacing);
        DrawCenteredDualButtonRow(
            UiStyle.FilterSplitButtonGap,
            sizeFilterOuterInset,
            width => _filters.SizeMedium = DrawSizeToggle("Medium", FontAwesomeIcon.Home, 0.95f, _filters.SizeMedium, width),
            width => _filters.SizeLarge = DrawSizeToggle("Large", FontAwesomeIcon.Home, 1.2f, _filters.SizeLarge, width));
        DrawExactVerticalGap(UiStyle.FilterRowSpacing);

        // Across both columns, under the house sizes.
        var available = MathF.Max(0f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X);
        var rowWidth = MathF.Max(0f, available - sizeFilterOuterInset * 2f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (available - rowWidth) * 0.5f));
        _filters.SizeElsewhere = DrawSizeToggle("Elsewhere", FontAwesomeIcon.MapMarkedAlt, 1f, _filters.SizeElsewhere, rowWidth);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_mode == DirectoryMode.Events
                ? "Events outside the housing districts, or at an address whose house size isn't known"
                : "Venues outside the housing districts, or whose house size isn't known");
        }
    }

    // Returns the new value; the last size left on cannot be turned off, since that would hide every venue. Small, medium and large use the same house icon at growing sizes.
    private bool DrawSizeToggle(string label, FontAwesomeIcon icon, float iconScale, bool value, float width)
    {
        if (!DrawToggleChip($"SizeToggle::{label}", icon, label, value, width, iconScale))
        {
            return value;
        }

        var enabledSizes = (_filters.SizeApartment ? 1 : 0) + (_filters.SizeSmall ? 1 : 0) +
                           (_filters.SizeMedium ? 1 : 0) + (_filters.SizeLarge ? 1 : 0) + (_filters.SizeElsewhere ? 1 : 0);
        if (value && enabledSizes == 1)
        {
            return value;
        }

        OnFiltersChanged();
        return !value;
    }

    // Draws equal-width buttons across the card, with icons when given; the selected one is highlighted.
    private static bool DrawSegmented(string id, string[] labels, FontAwesomeIcon[]? icons, ref int selected, string?[]? tooltips = null)
    {
        var available = MathF.Max(0f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X);
        var gap = UiStyle.SegmentGap;
        var width = MathF.Max(1f, (available - gap * (labels.Length - 1)) / labels.Length);
        var changed = false;
        using var segmentId = ImRaii.PushId(id);
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0f, gap);
            }

            var pressed = icons == null
                ? DrawToggleChip($"Segment{i}", labels[i], i == selected, width)
                : DrawToggleChip($"Segment{i}", icons[i], labels[i], i == selected, width);
            if (pressed && i != selected)
            {
                selected = i;
                changed = true;
            }

            if (tooltips?[i] is { } tooltip && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        return changed;
    }

    private void CollectActiveFilters()
    {
        _activeFilters.Clear();
        if (_mode == DirectoryMode.Events)
        {
            CollectActiveEventFilters();
            return;
        }

        // The Hidden view ignores the other filters, so they are not listed as active there.
        if (_filters.HiddenOnly)
        {
            _activeFilters.Add((ActiveFilterKind.Hidden, "Hidden venues"));
            return;
        }

        if (_filters.Time != VenueTimeFilter.AnyTime)
        {
            _activeFilters.Add((ActiveFilterKind.Time, TimeFilterLabels[(int)_filters.Time]));
        }

        if (_filters.Content != VenueContentFilter.All)
        {
            _activeFilters.Add((ActiveFilterKind.Content, _filters.Content == VenueContentFilter.SfwOnly ? "SFW only" : "NSFW only"));
        }

        if (_filters.FavoritesOnly)
        {
            _activeFilters.Add((ActiveFilterKind.Favorites, "Favorites"));
        }

        if (_filters.Visited != VenueVisitedFilter.Any)
        {
            _activeFilters.Add((ActiveFilterKind.Visited, _filters.Visited == VenueVisitedFilter.Visited ? "Visited" : "Not visited"));
        }

        if (!_filters.ShowsEveryPlace())
        {
            _activeFilters.Add((ActiveFilterKind.Size, FormatSizeFilterLabel()));
        }

        AddLocationActiveFilters();

        if (_filters.IncludedTags.Count > 0 || _filters.ExcludedTags.Count > 0)
        {
            _activeFilters.Add((ActiveFilterKind.Tags, FormatTagFilterLabel()));
        }
    }

    private void CollectActiveEventFilters()
    {
        if (_eventFilters.Time != EventTimeFilter.All)
        {
            _activeFilters.Add((ActiveFilterKind.EventTime, EventTimeLabels[(int)_eventFilters.Time]));
        }

        var hidden = new List<string>(4);
        if (!_eventFilters.ShowEveryone)
        {
            hidden.Add("Everyone");
        }

        if (!_eventFilters.ShowTeen)
        {
            hidden.Add("Teen");
        }

        if (!_eventFilters.ShowMature)
        {
            hidden.Add("Mature");
        }

        if (!_eventFilters.ShowAdult)
        {
            hidden.Add("Adult");
        }

        if (hidden.Count > 0)
        {
            _activeFilters.Add((ActiveFilterKind.EventRating, $"Hiding {string.Join(", ", hidden)}"));
        }

        AddLocationActiveFilters();

        if (!_filters.ShowsEveryPlace())
        {
            _activeFilters.Add((ActiveFilterKind.Size, FormatSizeFilterLabel()));
        }

        if (_eventFilters.Shortlist != EventShortlist.All)
        {
            _activeFilters.Add((ActiveFilterKind.EventFavorites, _eventFilters.Shortlist == EventShortlist.Going ? "Going" : "Favorite venues"));
        }

        if (_eventFilters.IncludedTags.Count > 0 || _eventFilters.ExcludedTags.Count > 0)
        {
            _activeFilters.Add((ActiveFilterKind.EventTags,
                "Tags: " + string.Join(", ", _eventFilters.IncludedTags.Concat(_eventFilters.ExcludedTags.Select(tag => $"not {tag}")))));
        }
    }

    private string FormatSizeFilterLabel()
    {
        var sizes = new List<string>(4);
        if (_filters.SizeApartment)
        {
            sizes.Add("Apartment");
        }

        if (_filters.SizeSmall)
        {
            sizes.Add("Small");
        }

        if (_filters.SizeMedium)
        {
            sizes.Add("Medium");
        }

        if (_filters.SizeLarge)
        {
            sizes.Add("Large");
        }

        if (_filters.SizeElsewhere)
        {
            sizes.Add("Elsewhere");
        }

        return $"Size: {string.Join(", ", sizes)}";
    }

    private string FormatTagFilterLabel() =>
        "Tags: " + string.Join(", ", _filters.IncludedTags.Concat(_filters.ExcludedTags.Select(tag => $"not {tag}")));

    private void ClearFilter(ActiveFilterKind kind)
    {
        switch (kind)
        {
            case ActiveFilterKind.EventTime:
                _eventFilters.Time = EventTimeFilter.All;
                break;
            case ActiveFilterKind.EventRating:
                _eventFilters.ShowEveryone = _eventFilters.ShowTeen = _eventFilters.ShowMature = _eventFilters.ShowAdult = true;
                break;
            case ActiveFilterKind.EventTags:
                _eventFilters.IncludedTags.Clear();
                _eventFilters.ExcludedTags.Clear();
                break;
            case ActiveFilterKind.EventFavorites:
                _eventFilters.Shortlist = EventShortlist.All;
                break;
            case ActiveFilterKind.Time:
                _filters.Time = VenueTimeFilter.AnyTime;
                break;
            case ActiveFilterKind.Content:
                _filters.Content = VenueContentFilter.All;
                break;
            case ActiveFilterKind.Favorites:
                _filters.FavoritesOnly = false;
                break;
            case ActiveFilterKind.Visited:
                _filters.Visited = VenueVisitedFilter.Any;
                break;
            case ActiveFilterKind.Hidden:
                _filters.HiddenOnly = false;
                break;
            case ActiveFilterKind.Size:
                _filters.ShowEveryPlace();
                break;
            case ActiveFilterKind.Region:
                _filters.Region = null;
                UpdateWorldOptions();
                break;
            case ActiveFilterKind.DataCenter:
                _filters.MyDataCenter = false;
                _filters.DataCenter = null;
                UpdateWorldOptions();
                break;
            case ActiveFilterKind.World:
                _filters.World = null;
                break;
            case ActiveFilterKind.Tags:
                _filters.IncludedTags.Clear();
                _filters.ExcludedTags.Clear();
                break;
        }

        OnFiltersChanged();
    }

    // Resets everything the filter cards set. The search text stays, since it is always visible in the top bar.
    private void ClearAllFilters()
    {
        if (_mode == DirectoryMode.Events)
        {
            _eventFilters.Time = EventTimeFilter.All;
            _eventFilters.ShowEveryone = _eventFilters.ShowTeen = _eventFilters.ShowMature = _eventFilters.ShowAdult = true;
            _eventFilters.IncludedTags.Clear();
            _eventFilters.ExcludedTags.Clear();
            _eventFilters.Shortlist = EventShortlist.All;
            _filters.ShowEveryPlace();
            _filters.Region = _filters.DataCenter = _filters.World = null;
            _filters.MyDataCenter = false;
            UpdateWorldOptions();
            OnFiltersChanged();
            return;
        }

        _filters.Time = VenueTimeFilter.AnyTime;
        _filters.Content = VenueContentFilter.All;
        _filters.Visited = VenueVisitedFilter.Any;
        _filters.FavoritesOnly = _filters.HiddenOnly = false;
        _filters.ShowEveryPlace();
        _filters.Region = _filters.DataCenter = _filters.World = null;
        _filters.MyDataCenter = false;
        _filters.IncludedTags.Clear();
        _filters.ExcludedTags.Clear();
        UpdateWorldOptions();
        OnFiltersChanged();
    }

    private void OnFiltersChanged()
    {
        InvalidateFilteredVenues();
        _filteredEventsDirty = true;
        RememberCharacterLocation();
        _filtersSaveDueAt = DateTimeOffset.UtcNow + FiltersSaveDelay;
    }

    // Every frame before the list is filtered: the location of the character that just logged in, the "Soon" window moving with the clock, and a delayed save, so clicking through options does not write the configuration on every click.
    private void UpdateFilterState()
    {
        var player = DalamudServices.PlayerState;
        var characterId = player.IsLoaded ? player.ContentId : 0UL;
        var worldId = player.IsLoaded ? player.CurrentWorld.RowId : 0u;
        var follow = false;
        if (worldId != 0 && worldId != _currentWorldId)
        {
            _currentWorldId = worldId;
            _currentDataCenter = DataCenterOf(worldId);
            follow = true;
        }

        if (characterId != 0 && characterId != _locationCharacterId)
        {
            _locationCharacterId = characterId;
            ApplyCharacterLocation(characterId);
            follow = true;
        }

        if (follow && _filters.MyDataCenter)
        {
            FollowCurrentDataCenter();
        }

        if (_filters.Time == VenueTimeFilter.Soon)
        {
            var minute = DateTime.UtcNow.Minute;
            if (minute != _soonFilterMinute)
            {
                _soonFilterMinute = minute;
                InvalidateFilteredVenues();
            }
        }

        if (_filtersSaveDueAt != default && DateTimeOffset.UtcNow >= _filtersSaveDueAt)
        {
            SaveFiltersNow();
        }
    }

    private void SaveFiltersNow()
    {
        _filtersSaveDueAt = default;
        _configuration.Save(DalamudServices.PluginInterface);
    }

    // Each character keeps its own region, data center and world. A character seen for the first time starts from the region of its home world.
    private void ApplyCharacterLocation(ulong characterId)
    {
        if (_configuration.CharacterLocations.TryGetValue(characterId, out var saved))
        {
            _filters.Region = saved.Region;
            _filters.DataCenter = saved.DataCenter;
            _filters.World = saved.World;
            _filters.MyDataCenter = saved.MyDataCenter;
        }
        else
        {
            var homeDataCenter = DalamudServices.PlayerState.HomeWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText();
            _filters.Region = ResolveRegion(homeDataCenter);
            _filters.DataCenter = null;
            _filters.World = null;
            _filters.MyDataCenter = false;
        }

        UpdateWorldOptions();
        OnFiltersChanged();
    }

    private void RememberCharacterLocation()
    {
        if (_locationCharacterId == 0)
        {
            return;
        }

        if (!_configuration.CharacterLocations.TryGetValue(_locationCharacterId, out var location))
        {
            location = new VenueLocationFilter();
            _configuration.CharacterLocations[_locationCharacterId] = location;
        }

        location.Region = _filters.Region;
        location.DataCenter = _filters.DataCenter;
        location.World = _filters.World;
        location.MyDataCenter = _filters.MyDataCenter;
    }

    // "My data center" on: the region and data center the character is on now (a world picked there stays); off: the data center stays as it was, for the combos to change.
    private void SetMyDataCenter(bool on)
    {
        _filters.MyDataCenter = on;
        if (!on || !FollowCurrentDataCenter())
        {
            OnFiltersChanged();
        }
    }

    // Returns true when the filters moved to another data center.
    private bool FollowCurrentDataCenter()
    {
        if (_currentDataCenter == null || string.Equals(_filters.DataCenter, _currentDataCenter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _filters.Region = ResolveRegion(_currentDataCenter);
        _filters.DataCenter = _currentDataCenter;
        _filters.World = null;
        UpdateWorldOptions();
        OnFiltersChanged();
        return true;
    }

    // Returns the data center of a world under its English name, as FFXIV Venues and Partake use it.
    private string? DataCenterOf(uint worldId)
    {
        var world = DalamudServices.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>(ClientLanguage.English).GetRowOrDefault(worldId);
        var name = world == null
            ? null
            : DalamudServices.DataManager.GetExcelSheet<Lumina.Excel.Sheets.WorldDCGroupType>(ClientLanguage.English).GetRowOrDefault(world.Value.DataCenter.RowId)?.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Spelled the way the venue data has it, when it has that data center.
        return _dataCenters.FirstOrDefault(dc => string.Equals(dc, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }

    // Adds the region, data center and world as active filter chips; "My data center" is one chip for the region and data center it sets.
    private void AddLocationActiveFilters()
    {
        if (_filters.MyDataCenter && _filters.DataCenter != null)
        {
            _activeFilters.Add((ActiveFilterKind.DataCenter, $"My data center · {_filters.DataCenter}"));
        }
        else
        {
            if (_filters.Region != null)
            {
                _activeFilters.Add((ActiveFilterKind.Region, _filters.Region));
            }

            if (_filters.DataCenter != null)
            {
                _activeFilters.Add((ActiveFilterKind.DataCenter, _filters.DataCenter));
            }
        }

        if (_filters.World != null)
        {
            _activeFilters.Add((ActiveFilterKind.World, _filters.World));
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
                using (ImRaii.TextWrapPos(wrapRightEdge))
                using (ImRaii.Group())
                using (ImRaii.PushColor(ImGuiCol.Text, UiStyle.BodyText))
                {
                    DrawFilterGroupHeader(title, description, accentTitle);
                    DrawVerticalRhythm(0.25f);
                    content();
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

    private void SetRegion(string? region)
    {
        _filters.MyDataCenter = false;
        _filters.Region = region;
        _filters.DataCenter = null;
        _filters.World = null;
        UpdateWorldOptions();
        OnFiltersChanged();
    }

    private void SetDataCenter(string? dataCenter)
    {
        _filters.MyDataCenter = false;
        _filters.DataCenter = dataCenter;
        _filters.World = null;
        UpdateWorldOptions();
        OnFiltersChanged();
    }

    private void UpdateWorldOptions()
    {
        _worlds.Clear();
        if (_venues == null)
        {
            return;
        }

        var query = _venues.AsEnumerable();
        if (!string.IsNullOrEmpty(_filters.Region))
        {
            query = query.Where(v =>
                string.Equals(ResolveRegion(v.Location?.DataCenter), _filters.Region, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_filters.DataCenter))
        {
            query = query.Where(v =>
                string.Equals(v.Location?.DataCenter, _filters.DataCenter, StringComparison.OrdinalIgnoreCase));
        }

        _worlds.AddRange(query
            .Select(v => v.Location?.World)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)!);
    }

    private IEnumerable<string> GetRegionDataCenters()
    {
        if (_filters.Region == null)
        {
            return _dataCenters;
        }

        return _dataCenters
            .Where(dc => string.Equals(ResolveRegion(dc), _filters.Region, StringComparison.OrdinalIgnoreCase))
            .OrderBy(dc => dc, StringComparer.OrdinalIgnoreCase);
    }
}
