using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Net;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Notifications;

// Opt-in Dalamud notifications: a favorite venue has opened, an event at a favorite venue has started, an event the user is going to starts soon. They work with the window closed: while one is turned on, this keeps the venue and event lists fresh on its own. Nothing is announced for what was already open or running when a notification was turned on, and nothing for hidden venues. An event's venue is found the same way as in the Events tab. Clicking a notification shows the venue or the event.
internal sealed partial class FavoriteAlerts : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    private readonly IFramework _framework;
    private readonly INotificationManager _notifications;
    private readonly Configuration _configuration;
    private readonly PeriodicFeed<DirectoryVenue[]> _venueFeed;
    private readonly PeriodicFeed<IReadOnlyList<CommunityEvent>> _eventFeed;
    private readonly Dictionary<string, bool> _wasOpen = new(StringComparer.Ordinal);
    private Dictionary<string, DirectoryVenue> _venuesById = new(StringComparer.Ordinal);
    private ListedVenueFinder<DirectoryVenue>? _venueFinder;
    private int _venuesByIdVersion = -1;
    private DateTimeOffset _checkedAt;
    private DateTimeOffset _eventsCheckedUntil;

    public FavoriteAlerts(
        IFramework framework,
        INotificationManager notifications,
        Configuration configuration,
        PeriodicFeed<DirectoryVenue[]> venueFeed,
        PeriodicFeed<IReadOnlyList<CommunityEvent>> eventFeed)
    {
        _framework = framework;
        _notifications = notifications;
        _configuration = configuration;
        _venueFeed = venueFeed;
        _eventFeed = eventFeed;
        _framework.Update += OnUpdate;
        OnCreated();
    }

    // Dev build: keeps the running instance for its checks.
    partial void OnCreated();

    // A notification was clicked: the venue (or the venue of the event) to show.
    public event Action<string>? VenueRequested;

    public event Action<int>? EventRequested;

    public void Dispose() => _framework.Update -= OnUpdate;

    private bool WatchVenues => _configuration.NotifyFavoriteOpens;

    private bool WatchEvents => (_configuration.NotifyFavoriteEvents || _configuration.NotifyGoingEvents) && _configuration.ShowPartakeEvents;

    // An event the user is going to is announced this long before it starts, to leave time to get there.
    private static readonly TimeSpan GoingLeadTime = TimeSpan.FromMinutes(15);

    private void OnUpdate(IFramework framework)
    {
        // Each kind starts over when turned on again: what is open or has started by then is not news.
        if (!WatchVenues)
        {
            _wasOpen.Clear();
        }

        if (!WatchEvents)
        {
            _eventsCheckedUntil = default;
        }

        if (!WatchVenues && !WatchEvents)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _checkedAt < CheckInterval)
        {
            return;
        }

        _checkedAt = now;
        _venueFeed.Tick();
        if (WatchEvents)
        {
            _eventFeed.Tick();
        }

        // Event reminders do not wait for the venue list: without it, only the venues known from events are recognized.
        if (_venueFeed.Value is { } venues)
        {
            EnsureVenueIndex(venues);
        }

        var favorites = Favorites();
        if (WatchVenues)
        {
            CheckVenues(favorites, now);
        }

        if (WatchEvents && _eventFeed.Value is { } events)
        {
            CheckEvents(favorites, events, now);
        }
    }

    private void EnsureVenueIndex(DirectoryVenue[] venues)
    {
        if (_venuesByIdVersion != _venueFeed.Version)
        {
            _venuesByIdVersion = _venueFeed.Version;
            _venuesById = venues.GroupBy(v => v.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _venueFinder = new ListedVenueFinder<DirectoryVenue>(venues, venue => venue);
        }
    }

    // Returns the favorite venues without the hidden ones.
    private List<string> Favorites() =>
        (_configuration.FavoriteVenueIds ?? [])
            .Where(id => !(_configuration.HiddenVenueIds?.Contains(id) ?? false))
            .ToList();

    private void AnnounceOpen(string id, DirectoryVenue venue, VenueOpening opening)
    {
        var until = opening.IsAroundTheClock ? "Open around the clock" : $"Open until {FormatClosingTime(opening.End)}";
        var where = venue.Location != null ? $" · {VenueAddresses.FormatCompactAddress(venue.Location)}" : string.Empty;
        Notify($"{Name(venue.Name)} is open", until + where, () => VenueRequested?.Invoke(id));
    }

    private void AnnounceStarted(CommunityEvent communityEvent, string venueName) =>
        Notify(
            $"{Name(communityEvent.Title)} has started",
            $"At {venueName} · until {FormatShortTime(communityEvent.EndsAt)}",
            () => EventRequested?.Invoke(communityEvent.Id));

    private void AnnounceGoing(CommunityEvent communityEvent) =>
        Notify(
            $"{Name(communityEvent.Title)} starts at {FormatShortTime(communityEvent.StartsAt)}",
            $"You're going · {Host(communityEvent)}",
            () => EventRequested?.Invoke(communityEvent.Id));

    private void CheckVenues(List<string> favorites, DateTimeOffset now)
    {
        foreach (var id in favorites)
        {
            if (!_venuesById.TryGetValue(id, out var venue))
            {
                continue;
            }

            var opening = VenueOpenings.Current(venue, now);
            var isOpen = opening?.IsOpenAt(now) == true;
            if (_wasOpen.TryGetValue(id, out var wasOpen) && !wasOpen && isOpen)
            {
                AnnounceOpen(id, venue, opening!.Value);
            }

            _wasOpen[id] = isOpen;
        }
    }

    private void CheckEvents(List<string> favorites, IReadOnlyList<CommunityEvent> events, DateTimeOffset now)
    {
        if (_eventsCheckedUntil == default)
        {
            _eventsCheckedUntil = now;
            return;
        }

        var since = _eventsCheckedUntil;
        _eventsCheckedUntil = now;
        foreach (var communityEvent in events)
        {
            // An event that is already over is not announced, however long the checks were held up.
            if (communityEvent.EndsAt <= now)
            {
                continue;
            }

            // Going to it: a reminder a little before it starts.
            var reminderAt = communityEvent.StartsAt - GoingLeadTime;
            if (_configuration.NotifyGoingEvents && reminderAt > since && reminderAt <= now &&
                _configuration.GoingEvents?.Exists(g => g.Id == communityEvent.Id) == true)
            {
                AnnounceGoing(communityEvent);
                continue;
            }

            // At a favorite venue: when it starts.
            if (_configuration.NotifyFavoriteEvents && communityEvent.StartsAt > since && communityEvent.StartsAt <= now &&
                FavoriteVenueOf(communityEvent, favorites) is { } venueName)
            {
                AnnounceStarted(communityEvent, venueName);
            }
        }
    }

    // Returns the name of the favorite venue an event is at, as the Events tab finds it: the listed venue at its address or with its host's name, or else the venue known only from events at its address; null when that venue is not a favorite.
    private string? FavoriteVenueOf(CommunityEvent communityEvent, List<string> favorites)
    {
        if (_venueFinder?.Find(communityEvent) is { } listed)
        {
            return favorites.Contains(listed.Id) ? Name(listed.Name) : null;
        }

        if (communityEvent.Address == null || EventVenueMatches.AddressKey(communityEvent.Address) is not { } key)
        {
            return null;
        }

        var unlistedId = EventVenueMatches.UnlistedVenueIdPrefix + key;
        return favorites.Exists(id => string.Equals(id, unlistedId, StringComparison.OrdinalIgnoreCase))
            ? Name(communityEvent.TeamName ?? communityEvent.Title)
            : null;
    }

    private static string Host(CommunityEvent communityEvent) =>
        string.IsNullOrWhiteSpace(communityEvent.TeamName) ? communityEvent.World ?? "Partake" : Name(communityEvent.TeamName);

    private static string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "A favorite venue" : NormalizeDisplayText(name);

    private void Notify(string title, string content, Action onClick)
    {
        DalamudServices.PluginLog.Debug("Notification: {Title}", title);
        var notification = _notifications.AddNotification(new Notification
        {
            Title = title,
            Content = content,
            MinimizedText = title,
            Minimized = false,
            Type = NotificationType.Info,
            InitialDuration = TimeSpan.FromSeconds(12),
        });
        notification.Click += _ =>
        {
            onClick();
            notification.DismissNow();
        };
    }
}
