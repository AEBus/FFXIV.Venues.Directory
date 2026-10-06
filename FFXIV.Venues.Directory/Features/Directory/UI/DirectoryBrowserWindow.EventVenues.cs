using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Features.PartyFinder;
using FFXIV.Venues.Directory.Infrastructure;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// Which venue each community event takes place at (see EventVenueMatches), prepared in the background: venues show their upcoming events; events link to their venue.
internal sealed partial class DirectoryBrowserWindow
{
    private const int VenueEventsShown = 3;

    private EventVenueMatches _eventVenueMatches = EventVenueMatches.Empty;

    // What the shown events and matches were prepared from, and what the one being prepared is from.
    private EventPreparationInputs _preparedEventInputs;
    private EventPreparationInputs _preparingEventInputs;
    private Task<(IReadOnlyList<CommunityEvent> Events, PreparedEvent[] Prepared, EventVenueMatches Matches)>? _eventPreparationTask;

    private readonly record struct EventPreparationInputs(
        IReadOnlyList<CommunityEvent>? PartakeEvents,
        IReadOnlyList<PartyFinderAd>? PartyFinderAds,
        PreparedVenue[]? Venues,
        int TextVersion,
        bool PlacesReady);

    // Opened from the other tab: shown even while the current filters would leave it out of the list.
    private string? _pinnedVenueId;
    private int? _pinnedEventId;

    // The events (Partake's together with the Party Finder ads), their list text and their venues are prepared again in the background when either source, the venues, the text treatment (font, clock) or the game's place names change; what is shown stays until the new ones are in.
    private void UpdateEventVenueMatches()
    {
        var inputs = new EventPreparationInputs(_partakeEvents, _partyFinderAds, _preparedVenues, RichTextVersion, _openWorldPlaces.IsReady);
        if (_eventPreparationTask is { IsCompleted: true } task)
        {
            _eventPreparationTask = null;
            if (task.IsCompletedSuccessfully)
            {
                if (_preparingEventInputs == inputs)
                {
                    (_events, _preparedEvents, _eventVenueMatches) = task.Result;
                    _preparedEventInputs = inputs;
                    _filteredEventsDirty = true;
                    InvalidateFilteredVenues();
                }
            }
            else
            {
                // Only the inputs that failed are not tried again; newer ones are prepared right away.
                DalamudServices.PluginLog.Warning(task.Exception?.GetBaseException(), "Could not prepare the events.");
                _preparedEventInputs = _preparingEventInputs;
            }
        }

        if (_eventPreparationTask != null || _preparedEventInputs == inputs)
        {
            return;
        }

        _preparingEventInputs = inputs;
        var partakeEvents = _partakeEvents;
        var ads = _partyFinderAds;
        var venues = _preparedVenues;
        var worlds = _openWorldPlaces.WorldDataCenters;
        _eventPreparationTask = Task.Run(() =>
        {
            var events = ads.Count == 0 ? partakeEvents : partakeEvents.Concat(PartyFinderEvents.FromAds(ads, partakeEvents, worlds)).ToList();
            var matches = venues == null
                ? EventVenueMatches.Empty
                : EventVenueMatches.Build(events, venues, source => _venuePreparer.CreatePreparedVenue(source), DateTimeOffset.UtcNow);
            var prepared = events.Select(e => PrepareEvent(e, matches.VenueOf(e.Id))).ToArray();
            return (events, prepared, matches);
        });
    }

    private string? EventVenueId(int eventId) => _eventVenueMatches.VenueIdOf(eventId);

    private static bool IsUnlistedVenue(PreparedVenue venue) => EventVenueMatches.IsUnlistedVenue(venue);

    private bool IsAtFavoriteVenue(int eventId) =>
        EventVenueId(eventId) is { } venueId && _favoriteVenueIds.Contains(venueId);

    private bool IsAtHiddenVenue(int eventId) =>
        EventVenueId(eventId) is { } venueId && _hiddenVenueIds.Contains(venueId);

    private bool UnlistedVenuesShown => EventsEnabled && _filters.IncludeUnlistedVenues;

    private bool TryGetUpcomingVenueEvents(string venueId, out List<CommunityEvent> events, out bool anyLive)
    {
        anyLive = false;
        if (!EventsEnabled || !_eventVenueMatches.TryGetEvents(venueId, out events))
        {
            events = null!;
            return false;
        }

        var now = DateTimeOffset.Now;
        var upcoming = false;
        foreach (var communityEvent in events)
        {
            if (communityEvent.EndsAt <= now)
            {
                continue;
            }

            upcoming = true;
            anyLive |= communityEvent.IsLive(now);
        }

        return upcoming;
    }

    private void DrawVenueEventsCard(PreparedVenue venue)
    {
        if (!TryGetUpcomingVenueEvents(venue.Id, out var events, out _))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        DrawVenueEventList("VenueEventsCard", "Events on Partake", events.Where(e => e.Source == EventSource.Partake && e.EndsAt > now).ToList());
        DrawVenueEventList("VenueAdsCard", "In the Party Finder now", events.Where(e => e.Source == EventSource.PartyFinder && e.EndsAt > now).ToList());
    }

    private void DrawVenueEventList(string id, string title, List<CommunityEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        DrawSection(id, title, () =>
        {
            var now = DateTimeOffset.Now;
            foreach (var communityEvent in events.Take(VenueEventsShown))
            {
                // An ad shows the first of its texts, which says more than its name, mostly the venue's own.
                var eventTitle = communityEvent.Source == EventSource.PartyFinder && communityEvent.AdText != null
                    ? NormalizeDisplayText(communityEvent.AdText.Split("\n\n")[0])
                    : _preparedEvents.FirstOrDefault(p => p.Event.Id == communityEvent.Id)?.Title ?? communityEvent.Title;
                var when = communityEvent.Source == EventSource.PartyFinder ? $"Since {FormatShortTime(communityEvent.StartsAt)}"
                    : communityEvent.IsLive(now) ? $"Live now · until {FormatShortTime(communityEvent.EndsAt)}"
                    : $"{DescribeEventDay(communityEvent.StartsAt)} · {FormatShortTime(communityEvent.StartsAt)}";
                using var rowId = ImRaii.PushId(communityEvent.Id);
                using (ImRaii.PushColor(ImGuiCol.Text, communityEvent.IsLive(now) ? UiStyle.PositiveText : UiStyle.BodyText))
                {
                    if (ImGui.Selectable($"{when}##when", false, ImGuiSelectableFlags.None))
                    {
                        OpenEvent(communityEvent.Id);
                    }
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Show this event in the Events tab");
                }

                DrawTextWrapped(eventTitle, UiStyle.BodyMutedText);
                DrawVerticalRhythm(0.15f);
            }
        });
    }

    private void DrawEventVenueCard(PreparedEvent prepared)
    {
        if (_eventVenueMatches.ListedVenueOf(prepared.Event.Id) is { } venue)
        {
            DrawSection("EventVenueCard", UiStyle.SectionHighlightBackground, () =>
            {
                DrawSectionHeader(FontAwesomeIcon.Store, "Listed on FFXIV Venues");
                DrawVerticalRhythm(0.25f);
                DrawBodyTextWrapped(venue.DisplayName);
                DrawVerticalRhythm(0.25f);
                if (DrawActionButton(FontAwesomeIcon.ArrowRight, "Open venue", UiButtonTone.Secondary))
                {
                    OpenVenue(venue.Id);
                }
            });
        }
    }

    // Shows a venue or event from outside the window, such as a notification, even if the filters would hide it.
    public void ShowVenue(string venueId) => OpenVenue(venueId);

    public void ShowEvent(int eventId) => OpenEvent(eventId);

    private void OpenEvent(int eventId)
    {
        SetMode(DirectoryMode.Events);
        _selectedEventId = eventId;
        _pinnedEventId = eventId;
        _filteredEventsDirty = true;
    }

    private void OpenVenue(string venueId)
    {
        SetMode(DirectoryMode.Venues);
        _selectedVenueId = venueId;
        _pinnedVenueId = venueId;
    }
}
