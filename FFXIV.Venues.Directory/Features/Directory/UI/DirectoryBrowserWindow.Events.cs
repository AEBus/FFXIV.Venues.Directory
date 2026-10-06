using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Features.PartyFinder;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Net;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenueAddresses;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// The Events tab: FFXIV events from Partake and venue ads from the Party Finder, as a timeline next to the details of the selected one.
internal sealed partial class DirectoryBrowserWindow
{
    private const float EventBannerMaxHeight = 260f;
    private static readonly string[] EventTimeLabels = ["Live", "24 h", "7 days", "All"];
    private static readonly FontAwesomeIcon[] EventTimeIcons = [FontAwesomeIcon.Circle, FontAwesomeIcon.Clock, FontAwesomeIcon.CalendarWeek, FontAwesomeIcon.CalendarAlt];
    private static readonly string[] ShortlistLabels = ["All", "Going", "Favorites"];
    private static readonly FontAwesomeIcon[] ShortlistIcons = [FontAwesomeIcon.LayerGroup, FontAwesomeIcon.CalendarCheck, FontAwesomeIcon.Star];
    private static readonly string?[] ShortlistTooltips = [null, "Events you're going to", "Events at venues you starred"];
    private static readonly string?[] EventTimeTooltips = ["Happening right now", "Now or starting within 24 hours", "Now or starting within 7 days", "Everything in the next two weeks"];

    private enum DirectoryMode
    {
        Venues,
        Events,
    }

    // An event with the text the list shows, prepared once per load (and again for another UI font).
    private sealed record PreparedEvent(
        CommunityEvent Event,
        string Title,
        string Host,
        string Where,
        string FullAddress,
        string SearchText,
        string? LifestreamArguments,
        HousingPlotSize? PlotSize,
        bool IsApartment,
        OpenWorldPlace? Place);

    private DirectoryMode _mode;
    private EventFilterSettings _eventFilters = null!;
    private IReadOnlyList<CommunityEvent> _events = [];
    private PreparedEvent[] _preparedEvents = [];
    private readonly PeriodicFeed<IReadOnlyList<CommunityEvent>> _eventFeed;
    private int _eventFeedVersion = -1;
    private readonly PeriodicFeed<IReadOnlyList<PartyFinderAd>> _partyFinderFeed;
    private int _partyFinderFeedVersion = -1;

    // What the two sources gave; the shown events (_events) are both together, made in the background along with the venue matches.
    private IReadOnlyList<CommunityEvent> _partakeEvents = [];
    private IReadOnlyList<PartyFinderAd> _partyFinderAds = [];
    private readonly List<PreparedEvent> _filteredEvents = [];
    private bool _filteredEventsDirty = true;
    private int _eventsMinute = -1;
    private int? _selectedEventId;
    private readonly Dictionary<int, RichDocument> _eventDocuments = [];
    private readonly Dictionary<int, Task<RichDocument?>> _eventDocumentTasks = [];
    private readonly HashSet<int> _eventDocumentFailures = [];
    private int _eventDocumentsVersion = -1;
    private RichTextView? _eventDescriptionView;
    private readonly List<(string Tag, int Count)> _eventTagCounts = [];
    private IReadOnlyList<CommunityEvent>? _eventTagCountsSource;
    private bool _showAllEventTags;

    private bool PartakeEnabled => _configuration.ShowPartakeEvents;

    private bool PartyFinderEnabled => _configuration.ShowPartyFinderAds;

    private bool EventsEnabled => PartakeEnabled || PartyFinderEnabled;

    private int LiveEventCount
    {
        get
        {
            var now = DateTimeOffset.Now;
            var count = 0;
            foreach (var communityEvent in _events)
            {
                if (communityEvent.IsLive(now))
                {
                    count++;
                }
            }

            return count;
        }
    }

    private void InitializeEvents()
    {
        _eventFilters = _configuration.EventFilters ??= new EventFilterSettings();
        _eventFilters.IncludedTags ??= [];
        _eventFilters.ExcludedTags ??= [];
        _mode = _configuration.LastModeWasEvents && EventsEnabled ? DirectoryMode.Events : DirectoryMode.Venues;
        SyncGoingEvents();
    }

    private readonly HashSet<int> _goingEventIds = [];

    // Drops the going marks of events that ended over a day ago, so the list stays bounded.
    private void SyncGoingEvents()
    {
        _configuration.GoingEvents ??= [];
        var expired = _configuration.GoingEvents.RemoveAll(g => g.EndsAt < DateTimeOffset.UtcNow - TimeSpan.FromDays(1));
        if (expired > 0)
        {
            _configuration.Save(DalamudServices.PluginInterface);
        }

        _goingEventIds.Clear();
        foreach (var going in _configuration.GoingEvents)
        {
            _goingEventIds.Add(going.Id);
        }
    }

    private bool IsGoingTo(int eventId) => _goingEventIds.Contains(eventId);

    // Marks an event as going to, like Partake's "Attend" but stored only in the plugin, since attending on Partake requires signing in there.
    private void SetGoingTo(CommunityEvent communityEvent, bool going)
    {
        _configuration.GoingEvents.RemoveAll(g => g.Id == communityEvent.Id);
        if (going)
        {
            _configuration.GoingEvents.Add(new GoingEvent { Id = communityEvent.Id, EndsAt = communityEvent.EndsAt });
        }

        _configuration.Save(DalamudServices.PluginInterface);
        SyncGoingEvents();
        _filteredEventsDirty = true;
    }

    private void SetMode(DirectoryMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        _configuration.LastModeWasEvents = mode == DirectoryMode.Events;
        _filtersSaveDueAt = DateTimeOffset.UtcNow + FiltersSaveDelay;
    }

    // Every frame: keeps the event list fresh (the feed refreshes it and retries failures on its own), takes a new list in, and lets the "live" and "within" filters move with the clock. A source turned off drops what it gave, so its events and the venues known from them leave the lists. Descriptions already fetched are kept across refreshes; they rarely change, and Partake asks for as few requests as possible.
    private void UpdateEvents()
    {
        if (PartakeEnabled)
        {
            _eventFeed.Tick();
            if (_eventFeed.Version != _eventFeedVersion && _eventFeed.Value is { } events)
            {
                _eventFeedVersion = _eventFeed.Version;
                _partakeEvents = events;
                _eventDocumentFailures.Clear();
                SyncGoingEvents();
            }
        }
        else if (_partakeEvents.Count > 0)
        {
            _partakeEvents = [];
            _eventFeedVersion = -1;
        }

        if (PartyFinderEnabled)
        {
            _partyFinderFeed.Tick();
            if (_partyFinderFeed.Version != _partyFinderFeedVersion && _partyFinderFeed.Value is { } ads)
            {
                _partyFinderFeedVersion = _partyFinderFeed.Version;
                _partyFinderAds = ads;
            }
        }
        else if (_partyFinderAds.Count > 0)
        {
            _partyFinderAds = [];
            _partyFinderFeedVersion = -1;
        }

        if (!EventsEnabled)
        {
            return;
        }

        if (_eventDocumentsVersion != RichTextVersion)
        {
            _eventDocumentsVersion = RichTextVersion;
            _eventDocuments.Clear();
        }

        var minute = DateTime.UtcNow.Minute;
        if (minute != _eventsMinute)
        {
            _eventsMinute = minute;
            _filteredEventsDirty = true;
        }
    }

    private void RefreshEvents()
    {
        if (PartakeEnabled)
        {
            _eventFeed.RefreshNow();
        }

        if (PartyFinderEnabled)
        {
            _partyFinderFeed.RefreshNow();
        }
    }

    // The source the Events tab reports on while it loads or fails: Partake, or the Party Finder when Partake is off.
    private (bool Loaded, string? Error, bool IsLoading, DateTimeOffset RetryAt, string Freshness, string Name) EventSourceState() =>
        PartakeEnabled ? StateOf(_eventFeed, "Partake") : StateOf(_partyFinderFeed, "Party Finder");

    private static (bool Loaded, string? Error, bool IsLoading, DateTimeOffset RetryAt, string Freshness, string Name) StateOf<T>(PeriodicFeed<T> feed, string name)
        where T : class =>
        (feed.Value != null, feed.Error, feed.IsLoading, feed.RetryAt, DescribeFreshness(feed), name);

    // A Party Finder ad has no age rating of its own; one for a venue marked NSFW counts as adult.
    private string RatingOf(CommunityEvent communityEvent) =>
        communityEvent.Source == EventSource.PartyFinder && string.IsNullOrEmpty(communityEvent.AgeRating) && _eventVenueMatches.ListedVenueOf(communityEvent.Id) is { Source.Sfw: false }
            ? "ADULT"
            : communityEvent.AgeRating;

    // A Party Finder ad goes by the name of its venue when the venue has one, which the ad's own text gives far less reliably.
    private PreparedEvent PrepareEvent(CommunityEvent communityEvent, PreparedVenue? venue)
    {
        var rawTitle = communityEvent.Source == EventSource.PartyFinder && venue?.Source.Name is { } venueName && venueName != EventVenueMatches.UnnamedVenueName ? venueName : communityEvent.Title;
        var title = NormalizeDisplayText(rawTitle);
        // Who posted an ad says nothing of who runs the venue, so an ad has no host.
        var host = communityEvent.Source == EventSource.PartyFinder || string.IsNullOrWhiteSpace(communityEvent.TeamName) ? string.Empty
            : NormalizeDisplayText(communityEvent.TeamName);
        var address = communityEvent.Address;
        // Not a housing address: possibly a spot in a zone or city.
        var place = address == null ? _openWorldPlaces.Find(communityEvent.LocationText) : null;
        var locationText = NormalizeDisplayText(communityEvent.LocationText);
        var where = address != null ? FormatCompactAddress(address)
            : place != null ? JoinParts(" · ", communityEvent.World, place.Describe())
            : JoinParts(" · ", communityEvent.World, locationText);
        var fullAddress = address != null
            ? FormatAddressDetailed(address)
            : JoinParts(", ", communityEvent.DataCenter, communityEvent.World, locationText);
        var search = NormalizeForSearch(string.Join('\n', new[] { rawTitle, communityEvent.TeamName ?? string.Empty, communityEvent.LocationText, communityEvent.World ?? string.Empty, place?.ZoneName ?? string.Empty, communityEvent.AdText ?? string.Empty }.Concat(communityEvent.Tags)));
        var lifestream = address != null ? FormatLifestreamArguments(address)
            : place != null ? FormatPlaceLifestreamArguments(communityEvent.World, place)
            : null;
        return new PreparedEvent(
            communityEvent,
            title,
            host,
            where,
            fullAddress,
            search,
            string.IsNullOrWhiteSpace(lifestream) ? null : lifestream,
            _venuePreparer.TryGetPlotSize(address),
            address != null && IsApartmentLocation(address),
            place);
    }

    private static string JoinParts(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private void RefreshFilteredEventsIfNeeded()
    {
        if (!_filteredEventsDirty)
        {
            return;
        }

        _filteredEventsDirty = false;
        _filteredEvents.Clear();
        var now = DateTimeOffset.Now;
        var search = NormalizeForSearch(_searchText.Trim());
        foreach (var prepared in _preparedEvents)
        {
            if (MatchesEventFilters(prepared, now, search))
            {
                _filteredEvents.Add(prepared);
            }
        }

        // Live events first, then by start.
        _filteredEvents.Sort((left, right) =>
        {
            var leftLive = left.Event.IsLive(now);
            var rightLive = right.Event.IsLive(now);
            return leftLive != rightLive
                ? (leftLive ? -1 : 1)
                : left.Event.StartsAt.CompareTo(right.Event.StartsAt);
        });

        if ((_selectedEventId == null || _filteredEvents.All(e => e.Event.Id != _selectedEventId)) &&
            (_selectedEventId == null || _selectedEventId != _pinnedEventId))
        {
            _selectedEventId = _filteredEvents.FirstOrDefault()?.Event.Id;
        }
    }

    private bool MatchesEventFilters(PreparedEvent prepared, DateTimeOffset now, string search)
    {
        var communityEvent = prepared.Event;
        if (communityEvent.EndsAt <= now)
        {
            return false;
        }

        // A venue the user hid takes its events with it, unless the user is going to this one; an ad for an event the venue has on Partake at the same time is left to that event.
        var going = IsGoingTo(communityEvent.Id);
        if (!going && (IsAtHiddenVenue(communityEvent.Id) || _eventVenueMatches.RepeatsPartakeEvent(communityEvent.Id)))
        {
            return false;
        }

        var shortlisted = _eventFilters.Shortlist switch
        {
            EventShortlist.Going => going,
            EventShortlist.FavoriteVenues => IsAtFavoriteVenue(communityEvent.Id),
            _ => true,
        };
        if (!shortlisted)
        {
            return false;
        }

        var withinTime = _eventFilters.Time switch
        {
            EventTimeFilter.LiveNow => communityEvent.IsLive(now),
            EventTimeFilter.Next24Hours => communityEvent.StartsAt - now <= TimeSpan.FromHours(24),
            EventTimeFilter.Next7Days => communityEvent.StartsAt - now <= TimeSpan.FromDays(7),
            _ => true,
        };
        if (!withinTime)
        {
            return false;
        }

        var ratingShown = RatingOf(communityEvent) switch
        {
            "ADULT" => _eventFilters.ShowAdult,
            "MATURE" => _eventFilters.ShowMature,
            "TEEN" => _eventFilters.ShowTeen,
            _ => _eventFilters.ShowEveryone,
        };
        if (!ratingShown)
        {
            return false;
        }

        // House size, shared with the venue filters: an event outside housing, or whose address could not be read, counts as "Elsewhere".
        if (!_filters.ShowsEveryPlace() && !_filters.ShowsPlace(prepared.IsApartment, prepared.PlotSize))
        {
            return false;
        }

        if ((_filters.Region != null && !string.Equals(communityEvent.Region, _filters.Region, StringComparison.OrdinalIgnoreCase)) ||
            (_filters.DataCenter != null && !string.Equals(communityEvent.DataCenter, _filters.DataCenter, StringComparison.OrdinalIgnoreCase)) ||
            (_filters.World != null && !string.Equals(communityEvent.World, _filters.World, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (var tag in _eventFilters.IncludedTags)
        {
            if (!communityEvent.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var tag in _eventFilters.ExcludedTags)
        {
            if (communityEvent.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return search.Length == 0 ||
               CultureInfo.CurrentCulture.CompareInfo.IndexOf(prepared.SearchText, search, CompareOptions.IgnoreCase) >= 0;
    }

    private void OnEventFiltersChanged()
    {
        _filteredEventsDirty = true;
        _filtersSaveDueAt = DateTimeOffset.UtcNow + FiltersSaveDelay;
    }

    private void DrawEventFilters()
    {
        DrawFilterGroupCard("EventShowCard", "Show", string.Empty, DrawEventTimeFilter);
        DrawFilterGroupCard("EventRatingCard", "Age rating", string.Empty, DrawEventRatingFilter);
        DrawFilterGroupCard("LocationFilterCard", "Location", string.Empty, DrawLocationFilters);
        DrawFilterGroupCard("SizeFilterCard", "House Size", string.Empty, DrawSizeFilter);
        DrawFilterGroupCard("EventSavedCard", "Saved", string.Empty, DrawEventSavedFilters);
        DrawFilterGroupCard("EventTagCard", "Partake tags", string.Empty, DrawEventTagFilters);
    }

    private void DrawEventSavedFilters()
    {
        var shortlist = (int)_eventFilters.Shortlist;
        if (DrawSegmented("EventShortlist", ShortlistLabels, ShortlistIcons, ref shortlist, ShortlistTooltips))
        {
            _eventFilters.Shortlist = (EventShortlist)shortlist;
            OnEventFiltersChanged();
        }

        var now = DateTimeOffset.Now;
        var atHiddenVenues = _preparedEvents.Count(e => e.Event.EndsAt > now && IsAtHiddenVenue(e.Event.Id) && !IsGoingTo(e.Event.Id));
        if (atHiddenVenues > 0)
        {
            DrawExactVerticalGap(UiStyle.FilterRowSpacing);
            DrawTextWrapped(
                atHiddenVenues == 1 ? "1 event at a venue you hid is not shown." : $"{atHiddenVenues} events at venues you hid are not shown.",
                UiStyle.BodyMutedText);
        }
    }

    private void DrawEventTimeFilter()
    {
        var time = (int)_eventFilters.Time;
        if (DrawSegmented("EventTimeFilter", EventTimeLabels, EventTimeIcons, ref time, EventTimeTooltips))
        {
            _eventFilters.Time = (EventTimeFilter)time;
            OnEventFiltersChanged();
        }
    }

    private void DrawEventRatingFilter()
    {
        DrawCenteredDualButtonRow(
            UiStyle.FilterSplitButtonGap,
            0f,
            width => _eventFilters.ShowEveryone = DrawRatingToggle("Everyone", FontAwesomeIcon.ShieldAlt, _eventFilters.ShowEveryone, width),
            width => _eventFilters.ShowTeen = DrawRatingToggle("Teen", FontAwesomeIcon.UserGraduate, _eventFilters.ShowTeen, width));
        DrawExactVerticalGap(UiStyle.FilterRowSpacing);
        DrawCenteredDualButtonRow(
            UiStyle.FilterSplitButtonGap,
            0f,
            width => _eventFilters.ShowMature = DrawRatingToggle("Mature", FontAwesomeIcon.GlassMartiniAlt, _eventFilters.ShowMature, width),
            width => _eventFilters.ShowAdult = DrawRatingToggle("Adult", FontAwesomeIcon.Ban, _eventFilters.ShowAdult, width));
    }

    // Uses the venue content filter's icons where the meaning is the same.
    private bool DrawRatingToggle(string label, FontAwesomeIcon icon, bool value, float width)
    {
        if (!DrawToggleChip($"RatingToggle::{label}", icon, label, value, width))
        {
            return value;
        }

        OnEventFiltersChanged();
        return !value;
    }

    private void DrawEventTagFilters()
    {
        EnsureEventTagCounts();
        if (DrawTagCloud(_eventTagCounts, _eventFilters.IncludedTags, _eventFilters.ExcludedTags, ref _showAllEventTags, out var toggled))
        {
            CycleTag(_eventFilters.IncludedTags, _eventFilters.ExcludedTags, toggled!);
            OnEventFiltersChanged();
        }
    }

    private void EnsureEventTagCounts()
    {
        if (ReferenceEquals(_eventTagCountsSource, _events))
        {
            return;
        }

        _eventTagCountsSource = _events;
        _eventTagCounts.Clear();
        _eventTagCounts.AddRange(_events
            .SelectMany(e => e.Tags)
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, group.Count()))
            .OrderByDescending(pair => pair.Item2)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));
    }

    private void DrawEventList()
    {
        if (!EventsEnabled)
        {
            DrawMutedText("Partake events and Party Finder ads are turned off in the settings.");
            return;
        }

        RefreshFilteredEventsIfNeeded();
        if (_preparedEvents.Length == 0)
        {
            var source = EventSourceState();
            if (source.Loaded)
            {
                DrawMutedText(PartakeEnabled ? "No FFXIV events on Partake in the next two weeks." : "No venue ads in the Party Finder right now.");
            }
            else if (source.Error != null)
            {
                DrawTextWrapped(source.Error, UiStyle.ErrorText);
                DrawMutedText(source.IsLoading ? "Trying again..." : $"Trying again {FormatRetryIn(source.RetryAt)}.");
            }
            else
            {
                DrawMutedText(PartakeEnabled ? "Loading events from Partake..." : "Loading Party Finder ads...");
            }

            return;
        }

        if (_filteredEvents.Count == 0)
        {
            DrawSection("EventListEmptyState", UiStyle.FilterGroupBackground, () =>
            {
                DrawText("No events match the current filters.", UiStyle.BodyStrongText);
                DrawVerticalRhythm(0.35f);
                DrawMutedText("Adjust the filters, or look again later: hosts add events all the time.");
            });
            return;
        }

        using var scroll = ImRaii.Child("EventListScroll"u8, Vector2.Zero, false);
        if (!scroll)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        string? group = null;
        foreach (var prepared in _filteredEvents)
        {
            var isLive = prepared.Event.IsLive(now);
            var eventGroup = isLive ? "Live now" : DescribeEventDay(prepared.Event.StartsAt);
            if (eventGroup != group)
            {
                group = eventGroup;
                DrawEventGroupHeader(eventGroup, isLive);
            }

            DrawEventRow(prepared, isLive, now);
        }
    }

    private static void DrawEventGroupHeader(string label, bool isLive)
    {
        DrawVerticalRhythm(0.2f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Scale(8f));
        DrawSectionHeader(label, isLive ? UiStyle.PositiveText : UiStyle.SectionHeaderText);
    }

    private void DrawEventRow(PreparedEvent prepared, bool isLive, DateTimeOffset now)
    {
        var communityEvent = prepared.Event;
        var lineHeight = ImGui.GetTextLineHeight();
        var rowHeight = lineHeight * 2f + Scale(10f);
        var isSelected = _selectedEventId == communityEvent.Id;
        using var rowId = ImRaii.PushId(communityEvent.Id);
        using (ImRaii.PushColor(ImGuiCol.Header, UiStyle.SelectedRowBackground)
                   .Push(ImGuiCol.HeaderHovered, UiStyle.SelectedRowHoveredBackground)
                   .Push(ImGuiCol.HeaderActive, UiStyle.SelectedRowActiveBackground))
        {
            if (ImGui.Selectable("##EventRow", isSelected, ImGuiSelectableFlags.None, new Vector2(0f, rowHeight)))
            {
                _selectedEventId = communityEvent.Id;
                _pinnedEventId = null;
            }
        }

        DrawEventRowContextMenu(prepared);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (isSelected)
        {
            DrawSelectedRowAccent(min, max);
        }

        var drawList = ImGui.GetWindowDrawList();
        var inset = Scale(10f);
        var top = min.Y + Scale(5f);
        // Wide enough for the times and for the countdown under them.
        var timeWidth = MathF.Max(
            ImGui.CalcTextSize(Use12HourClock ? "10:00 PM - 10:00 PM" : "00:00 - 00:00").X,
            ImGui.CalcTextSize("ends in 47h 59m").X) + Scale(16f);
        var timeColor = isLive ? UiStyle.PositiveText : UiStyle.BodyText;
        if (communityEvent.Source == EventSource.PartyFinder)
        {
            // An ad's expiry is no closing time: only when it went up is shown.
            drawList.AddText(new Vector2(min.X + inset, top), ImGui.GetColorU32(timeColor), "Advertising");
            drawList.AddText(new Vector2(min.X + inset, top + lineHeight), ImGui.GetColorU32(UiStyle.BodyMutedText), $"since {FormatShortTime(communityEvent.StartsAt)}");
        }
        else
        {
            drawList.AddText(new Vector2(min.X + inset, top), ImGui.GetColorU32(timeColor), $"{FormatShortTime(communityEvent.StartsAt)} - {FormatShortTime(communityEvent.EndsAt)}");
            if (isLive)
            {
                drawList.AddText(new Vector2(min.X + inset, top + lineHeight), ImGui.GetColorU32(UiStyle.BodyMutedText), $"ends in {FormatShortDuration(communityEvent.EndsAt - now)}");
            }
            else if (communityEvent.StartsAt - now <= SoonWindow)
            {
                drawList.AddText(new Vector2(min.X + inset, top + lineHeight), ImGui.GetColorU32(UiStyle.SoonText), $"in {FormatShortDuration(communityEvent.StartsAt - now)}");
            }
        }

        var isAd = communityEvent.Source == EventSource.PartyFinder;
        var rating = isAd && string.IsNullOrEmpty(RatingOf(communityEvent)) ? "Party Finder" : RatingLabel(RatingOf(communityEvent));
        var going = communityEvent.AttendeeCount > 0 ? $"{communityEvent.AttendeeCount} going" : string.Empty;
        var rightWidth = MathF.Max(ImGui.CalcTextSize(rating).X, ImGui.CalcTextSize(going).X) + inset;
        var textX = min.X + inset + timeWidth;
        var textRight = max.X - rightWidth - inset;
        drawList.PushClipRect(new Vector2(textX, min.Y), new Vector2(MathF.Max(textX, textRight), max.Y), true);
        var titleX = textX;
        if (IsGoingTo(communityEvent.Id))
        {
            // A calendar check before the title of an event the user is going to.
            var markFont = PluginUiFont.IconFont;
            var markSize = ImGui.GetFontSize() * 0.8f;
            var markText = IconText(FontAwesomeIcon.CalendarCheck);
            drawList.AddText(markFont, markSize, new Vector2(titleX, top + (lineHeight - markSize) * 0.5f), ImGui.GetColorU32(UiStyle.PositiveText), markText);
            titleX += ImGui.CalcTextSizeA(markFont, markSize, float.MaxValue, 0f, markText, out _).X + Scale(4f);
        }

        if (isAd)
        {
            // A bullhorn before the title of a Party Finder ad.
            var adFont = PluginUiFont.IconFont;
            var adSize = ImGui.GetFontSize() * 0.8f;
            var adText = IconText(FontAwesomeIcon.Bullhorn);
            drawList.AddText(adFont, adSize, new Vector2(titleX, top + (lineHeight - adSize) * 0.5f), ImGui.GetColorU32(UiStyle.SoonText), adText);
            titleX += ImGui.CalcTextSizeA(adFont, adSize, float.MaxValue, 0f, adText, out _).X + Scale(4f);
        }

        drawList.AddText(new Vector2(titleX, top), ImGui.GetColorU32(isSelected ? UiStyle.BodyStrongText : UiStyle.BodyText), prepared.Title);
        var subtitle = string.IsNullOrEmpty(prepared.Host) ? prepared.Where : $"{prepared.Host} · {prepared.Where}";
        var subtitleX = textX;
        if (IsAtFavoriteVenue(communityEvent.Id))
        {
            // A star, as in the venue list, before the host of an event at a favorite venue.
            var iconFont = PluginUiFont.IconFont;
            var iconSize = ImGui.GetFontSize() * 0.8f;
            var star = IconText(FontAwesomeIcon.Star);
            drawList.AddText(iconFont, iconSize, new Vector2(subtitleX, top + lineHeight + (lineHeight - iconSize) * 0.5f), ImGui.GetColorU32(UiStyle.FavoriteText), star);
            subtitleX += ImGui.CalcTextSizeA(iconFont, iconSize, float.MaxValue, 0f, star, out _).X + Scale(4f);
        }

        drawList.AddText(new Vector2(subtitleX, top + lineHeight), ImGui.GetColorU32(UiStyle.BodyMutedText), subtitle);
        drawList.PopClipRect();

        var ratingX = max.X - inset - ImGui.CalcTextSize(rating).X;
        drawList.AddText(new Vector2(ratingX, top), ImGui.GetColorU32(RatingColor(RatingOf(communityEvent))), rating);
        if (going.Length > 0)
        {
            drawList.AddText(new Vector2(max.X - inset - ImGui.CalcTextSize(going).X, top + lineHeight), ImGui.GetColorU32(UiStyle.BodyMutedText), going);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{prepared.Title}\n{prepared.FullAddress}");
        }
    }

    private void DrawEventHeader(PreparedEvent prepared)
    {
        var communityEvent = prepared.Event;
        var isGoing = IsGoingTo(communityEvent.Id);
        var buttonWidth = MeasureIconActionButtonSize(FontAwesomeIcon.CalendarCheck).X;
        var start = ImGui.GetCursorPos();
        var buttonX = start.X + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttonWidth);
        ImGui.SetCursorPos(new Vector2(buttonX, start.Y));
        float buttonBottom;
        using (ImRaii.PushId(communityEvent.Id))
        {
            if (DrawToggleIconButton("GoingToEvent", FontAwesomeIcon.CalendarCheck, isGoing, isGoing ? "You're going · click to unmark" : "I'm going", UiStyle.PositiveText))
            {
                SetGoingTo(communityEvent, !isGoing);
            }

            buttonBottom = ImGui.GetItemRectMax().Y;
        }

        ImGui.SetCursorPos(start);
        DrawDisplayTitleText(prepared.Title, buttonX - UiStyle.InlineGroupSpacing);
        var cursor = ImGui.GetCursorScreenPos();
        var belowButton = buttonBottom + ImGui.GetStyle().ItemSpacing.Y;
        if (belowButton > cursor.Y)
        {
            ImGui.SetCursorScreenPos(new Vector2(cursor.X, belowButton));
        }
    }

    private void DrawEventRowContextMenu(PreparedEvent prepared)
    {
        using var menu = ImRaii.ContextPopupItem("EventRowMenu");
        if (!menu)
        {
            return;
        }

        var isGoing = IsGoingTo(prepared.Event.Id);
        if (ImGui.MenuItem(isGoing ? "Not going" : "I'm going"))
        {
            SetGoingTo(prepared.Event, !isGoing);
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Copy address"))
        {
            ImGui.SetClipboardText(prepared.FullAddress);
        }

        if (prepared.Event.Url is { } url && ImGui.MenuItem("Open on Partake"))
        {
            WebLink.Open(url);
        }
    }

    private static string RatingLabel(string rating) => rating switch
    {
        "ADULT" => "Adult",
        "MATURE" => "Mature",
        "TEEN" => "Teen",
        _ => "Everyone",
    };

    private static Vector4 RatingColor(string rating) => rating switch
    {
        "ADULT" => new Vector4(1f, 0.55f, 0.60f, 1f),
        "MATURE" => UiStyle.SoonText,
        _ => UiStyle.BodyMutedText,
    };

    private static UiChipTone RatingTone(string rating) => rating switch
    {
        "ADULT" => UiChipTone.Warning,
        "MATURE" => UiChipTone.Caution,
        _ => UiChipTone.Neutral,
    };

    private PreparedEvent? GetSelectedEvent() =>
        _filteredEvents.FirstOrDefault(e => e.Event.Id == _selectedEventId) ??
        (_selectedEventId != null && _selectedEventId == _pinnedEventId
            ? _preparedEvents.FirstOrDefault(e => e.Event.Id == _selectedEventId)
            : null);

    private void DrawEventDetails(PreparedEvent prepared)
    {
        var communityEvent = prepared.Event;
        var now = DateTimeOffset.Now;
        if (communityEvent.BannerUrl != null)
        {
            var banner = _remoteImages.Get(communityEvent.BannerUrl, out var failed);
            if (banner != null)
            {
                var available = ImGui.GetContentRegionAvail().X;
                var width = MathF.Min(available, Scale(BannerMaxWidth));
                var aspect = banner.Width == 0 ? 0.5f : banner.Height / (float)banner.Width;
                var height = width * aspect;
                if (height > Scale(EventBannerMaxHeight))
                {
                    height = Scale(EventBannerMaxHeight);
                    width = height / aspect;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (available - width) * 0.5f);
                }

                DrawRoundedImage(banner.Handle, new Vector2(width, height));
                ImagePreview.HandleHover(banner, communityEvent.BannerUrl, _configuration.PreviewImagesOnHover);
                DrawVerticalRhythm(0.5f);
            }
            else if (!failed)
            {
                DrawMutedText("Loading banner...");
                DrawVerticalRhythm(0.5f);
            }
        }

        DrawEventHeader(prepared);
        if (!string.IsNullOrEmpty(prepared.Host))
        {
            DrawMutedText($"by {prepared.Host}");
        }

        if (prepared.Place != null)
        {
            DrawTextWrapped(JoinParts(" · ", communityEvent.World, prepared.Place.Describe()), UiStyle.BodyText);
        }

        DrawTextWrapped(prepared.FullAddress, UiStyle.BodyMutedText);
        DrawVerticalRhythm(0.25f);

        var rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var first = true;
        if (communityEvent.Source == EventSource.PartyFinder)
        {
            DrawInfoChip("EventTimeChip", $"In the Party Finder since {FormatShortTime(communityEvent.StartsAt)}", UiChipTone.Positive, rightEdge, ref first);
        }
        else if (communityEvent.IsLive(now))
        {
            DrawInfoChip("EventTimeChip", $"Live now · until {FormatShortTime(communityEvent.EndsAt)}", UiChipTone.Positive, rightEdge, ref first);
        }
        else
        {
            var soon = communityEvent.StartsAt - now <= SoonWindow;
            DrawInfoChip(
                "EventTimeChip",
                $"{DescribeEventDay(communityEvent.StartsAt)} · {FormatShortTime(communityEvent.StartsAt)} - {FormatShortTime(communityEvent.EndsAt)}",
                soon ? UiChipTone.Caution : UiChipTone.Neutral,
                rightEdge,
                ref first);
            DrawInfoChip("EventStartsInChip", $"in {FormatDuration(communityEvent.StartsAt - now)}", UiChipTone.Neutral, rightEdge, ref first);
        }

        if (communityEvent.Source == EventSource.Partake || !string.IsNullOrEmpty(RatingOf(communityEvent)))
        {
            DrawInfoChip("EventRatingChip", RatingLabel(RatingOf(communityEvent)), RatingTone(RatingOf(communityEvent)), rightEdge, ref first);
        }

        if (communityEvent.AttendeeCount > 0)
        {
            DrawInfoChip("EventGoingChip", $"{communityEvent.AttendeeCount} going", UiChipTone.Neutral, rightEdge, ref first);
        }

        if (communityEvent.Region != null)
        {
            DrawInfoChip("EventRegionChip", communityEvent.Region, UiChipTone.Neutral, rightEdge, ref first);
        }

        DrawVerticalRhythm(0.35f);
        var hasPreviousAction = false;
        if (_lifestreamIpc.IsAvailable && prepared.LifestreamArguments != null)
        {
            if (DrawWrappedActionButton(FontAwesomeIcon.LocationArrow, "Visit", UiButtonTone.Primary, ref hasPreviousAction) &&
                !_lifestreamIpc.TryExecuteCommand(prepared.LifestreamArguments, out var errorMessage))
            {
                DalamudServices.ChatGui.PrintError($"Could not travel with Lifestream: {errorMessage}");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(prepared.Place?.TeleportName is { } teleport
                    ? $"Travel to {communityEvent.World} and teleport to {teleport} with Lifestream"
                    : "Travel there with Lifestream");
            }
        }

        DrawShowOnMapButton(prepared.Place, ref hasPreviousAction);
        if (DrawWrappedActionButton(FontAwesomeIcon.Copy, "Copy address", UiButtonTone.Secondary, ref hasPreviousAction))
        {
            ImGui.SetClipboardText(prepared.FullAddress);
        }

        if (communityEvent.Url is { } url && DrawWrappedActionButton(FontAwesomeIcon.ExternalLinkAlt, "Open on Partake", UiButtonTone.Secondary, ref hasPreviousAction))
        {
            WebLink.Open(url);
        }

        DrawVerticalRhythm(0.5f);
        DrawEventVenueCard(prepared);
        if (communityEvent.Address == null)
        {
            DrawSection("EventAddressNote", UiStyle.SectionHighlightBackground, () =>
            {
                DrawSectionHeader(FontAwesomeIcon.InfoCircle, "Address");
                DrawVerticalRhythm(0.25f);
                DrawBodyTextWrapped("The host wrote the location as free text that is not a housing address, so there is no Visit button. It is shown as written.");
            });
        }

        DrawSection("EventDescriptionCard", communityEvent.Source == EventSource.PartyFinder ? "Ad" : "Description", () =>
        {
            var document = GetEventDocument(communityEvent, out var loading, out var failed);
            if (document != null && !document.IsEmpty)
            {
                _eventDescriptionView ??= new RichTextView(_remoteImages, () => _configuration.LoadDescriptionImages, () => _configuration.PreviewImagesOnHover);
                _eventDescriptionView.Draw(document, RichTextVersion, FormatRichTime, DescribeRichTime, RichTextColors);
            }
            else if (loading)
            {
                DrawMutedText("Loading description...");
            }
            else if (failed)
            {
                DrawMutedText("Could not load the description from Partake.");
                if (ImGui.SmallButton("Try again"))
                {
                    _eventDocumentFailures.Remove(communityEvent.Id);
                }
            }
            else
            {
                DrawMutedText("No description.");
            }

            if (communityEvent.Source == EventSource.PartyFinder)
            {
                DrawVerticalRhythm(0.25f);
                DrawMutedText("Party Finder ads via xivpf.com");
            }
        });

        if (communityEvent.Tags.Count > 0)
        {
            DrawSection("EventTagsCard", "Tags", () => DrawTagChips(communityEvent.Tags));
        }
    }

    // Returns the description of an event: a Party Finder ad's own text, or a Partake description, fetched the first time it is shown and kept until the event list reloads.
    private RichDocument? GetEventDocument(CommunityEvent communityEvent, out bool loading, out bool failed)
    {
        var eventId = communityEvent.Id;
        if (communityEvent.Source == EventSource.PartyFinder)
        {
            loading = failed = false;
            if (!_eventDocuments.TryGetValue(eventId, out var adDocument))
            {
                adDocument = PreparePlainTextDocument(communityEvent.AdText);
                _eventDocuments[eventId] = adDocument;
            }

            return adDocument;
        }

        loading = false;
        failed = _eventDocumentFailures.Contains(eventId);
        if (_eventDocuments.TryGetValue(eventId, out var document))
        {
            return document;
        }

        if (failed)
        {
            return null;
        }

        if (!_eventDocumentTasks.TryGetValue(eventId, out var task))
        {
            task = LoadEventDocumentAsync(eventId);
            _eventDocumentTasks[eventId] = task;
        }

        if (!task.IsCompleted)
        {
            loading = true;
            return null;
        }

        _eventDocumentTasks.Remove(eventId);
        if (task.IsCompletedSuccessfully && task.Result != null)
        {
            _eventDocuments[eventId] = task.Result;
            return task.Result;
        }

        var error = task.Exception?.GetBaseException();
        if (error == null)
        {
            DalamudServices.PluginLog.Debug("Partake has no description for event {EventId}.", eventId);
        }
        else if (NetworkFailure.IsExpected(error))
        {
            DalamudServices.PluginLog.Warning("Could not load the description of event {EventId}: {Reason}", eventId, error.Message);
        }
        else
        {
            DalamudServices.PluginLog.Warning(error, "Could not load the description of event {EventId}.", eventId);
        }

        _eventDocumentFailures.Add(eventId);
        failed = true;
        return null;
    }

    private async Task<RichDocument?> LoadEventDocumentAsync(int eventId)
    {
        var description = await _partake.GetEventDescriptionAsync(eventId, _disposeCts.Token).ConfigureAwait(false);
        return description == null ? null : PreparePartakeDocument(description.RawDescription);
    }
}
