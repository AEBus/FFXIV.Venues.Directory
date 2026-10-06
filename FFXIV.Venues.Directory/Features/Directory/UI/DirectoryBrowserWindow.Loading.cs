using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Text;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenueAddresses;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenuePreparer;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// Getting the venue list on screen and keeping it fresh: a new list from the feed is prepared in the background and swapped in without losing the selection, statuses move with the clock, and a venue's details are built the first time it is selected (the previous ones stay shown while a refreshed list rebuilds them).
internal sealed partial class DirectoryBrowserWindow
{
    // Reloads the list for the refresh button; what is shown stays until the new list is prepared.
    private void TriggerRefresh() => _venueFeed.RefreshNow();

    // A new list from the feed (the first one, a periodic refresh or the refresh button) is prepared in the background; the current one stays on screen, with its selection, filters and scroll, until it is ready.
    private void ConsumeVenueFeed()
    {
        if (_venueFeed.Version == _venueFeedVersion || _venueFeed.Value is not { } venues)
        {
            return;
        }

        _venueFeedVersion = _venueFeed.Version;
        _venueRefreshVersion++;
        _venues = venues;
        _loadError = null;

        _dataCenters.Clear();
        _dataCenters.AddRange(_venues
            .Select(v => v.Location?.DataCenter)
            .Where(dc => !string.IsNullOrWhiteSpace(dc))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(dc => dc, StringComparer.OrdinalIgnoreCase)!);

        _regions.Clear();
        _regions.AddRange(Regions);

        UpdateWorldOptions();
        _housingPlotSizeResolver.WarmUp();
        StartVenuePreparation(_venues);
    }

    // Venue text is prepared for the glyphs of the UI font. The font is built asynchronously and rebuilt when the user changes Dalamud's font, so text prepared before (or for another font) is prepared again; the list keeps showing the previous text meanwhile.
    private void RefreshPreparedTextForFont()
    {
        if (_venues == null || _preparedVenuesTask != null ||
            (UiGlyphs.Current?.Generation ?? 0) == _preparedGlyphGeneration)
        {
            return;
        }

        PrepareVenueTextAgain();
    }

    private static bool Resolve12HourClock(ClockFormat format) => format switch
    {
        ClockFormat.TwelveHour => true,
        ClockFormat.TwentyFourHour => false,
        _ => SystemClock.Uses12Hour(),
    };

    private void SetClockFormat(ClockFormat format)
    {
        _configuration.Clock = format;
        _configuration.Use12HourClock = format == ClockFormat.TwelveHour;
        _configuration.Save(DalamudServices.PluginInterface);
        ApplyClockFormat();
    }

    // Times are baked into the prepared text (status lines, schedules, Discord timestamps), so a clock format change prepares it again, the same way a font change does. "Auto" is checked again whenever the settings open, in case Windows' format changed meanwhile.
    private void ApplyClockFormat()
    {
        var use12Hour = Resolve12HourClock(_configuration.Clock ?? ClockFormat.System);
        if (Use12HourClock == use12Hour)
        {
            return;
        }

        Use12HourClock = use12Hour;
        if (_venues != null && _preparedVenuesTask == null)
        {
            PrepareVenueTextAgain();
        }
    }

    private void PrepareVenueTextAgain() => StartVenuePreparation(_venues!);

    private void StartVenuePreparation(DirectoryVenue[] venues)
    {
        var refreshVersion = _venueRefreshVersion;
        _preparedGlyphGeneration = UiGlyphs.Current?.Generation ?? 0;
        _preparedVenueTaskVersion = refreshVersion;
        _preparedVenueTaskStartedAtUtc = DateTimeOffset.UtcNow;
        var cancellationToken = _disposeCts.Token;
        _preparedVenuesTask = Task.Factory.StartNew(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prepared = _venuePreparer.BuildPreparedVenues(venues);
                cancellationToken.ThrowIfCancellationRequested();
                return prepared;
            },
            cancellationToken,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private bool TryConsumePreparedVenueTask()
    {
        if (_preparedVenuesTask == null || !_preparedVenuesTask.IsCompleted)
        {
            if (_preparedVenuesTask != null &&
                _venues != null &&
                _preparedVenueTaskStartedAtUtc != default &&
                DateTimeOffset.UtcNow - _preparedVenueTaskStartedAtUtc >= PreparedVenueBuildTimeout)
            {
                DalamudServices.PluginLog.Warning("Preparing the venues took longer than {Timeout}s; showing them without house sizes.", PreparedVenueBuildTimeout.TotalSeconds);

                ActivatePreparedVenues(_venuePreparer.BuildPreparedVenuesLightweight(_venues));
                return true;
            }

            return false;
        }

        if (_preparedVenuesTask.IsCanceled)
        {
            _preparedVenuesTask = null;
            _preparedVenueTaskStartedAtUtc = default;
            return false;
        }

        if (_preparedVenuesTask.IsFaulted)
        {
            _loadError = _preparedVenuesTask.Exception?.GetBaseException().Message ?? "Failed to prepare venues.";
            _preparedVenuesTask = null;
            _preparedVenueTaskStartedAtUtc = default;
            return false;
        }

        if (_preparedVenueTaskVersion != _venueRefreshVersion)
        {
            _preparedVenuesTask = null;
            _preparedVenueTaskStartedAtUtc = default;
            return false;
        }

        ActivatePreparedVenues(_preparedVenuesTask.Result ?? []);
        return true;
    }

    // The first list warms up over a few frames; a refreshed one replaces the shown one in place, and the details built for the old one stay on screen until the new ones are built.
    private void ActivatePreparedVenues(PreparedVenue[] prepared)
    {
        var replacing = _preparedVenues != null;
        if (replacing)
        {
            foreach (var (id, details) in _preparedVenueDetailsCache)
            {
                _staleVenueDetails[id] = details;
            }
        }

        _preparedVenueDetailsCache.Clear();
        _preparedVenueDetailTasks.Clear();
        _preparedVenueScheduleTasks.Clear();
        _preparedVenues = prepared;
        _preparedVenuesTask = null;
        _preparedVenueTaskStartedAtUtc = default;
        _selectedVenueId = KeepOrFirstVenueId(_preparedVenues);
        _postPreparationActivationStage = replacing ? PostPreparationActivationStage.None : PostPreparationActivationStage.FinalizeState;
        _venueStatusMinute = -1;
        InvalidateFilteredVenues();
    }

    // Every minute the venues' statuses are worked out again for the current time, so venues open and close in the list as the clock passes their times; the order and the filters follow when one of them changed.
    private void UpdateVenueStatuses()
    {
        var now = DateTimeOffset.UtcNow;
        var minute = now.ToUnixTimeSeconds() / 60;
        if (minute == _venueStatusMinute)
        {
            return;
        }

        _venueStatusMinute = minute;
        var changed = false;
        foreach (var venue in _preparedVenues!)
        {
            changed |= venue.Status.Update(venue.Source, now);
        }

        foreach (var venue in _eventVenueMatches.UnlistedVenues)
        {
            changed |= venue.Status.Update(venue.Source, now);
        }

        if (changed)
        {
            InvalidateFilteredVenues();
        }
    }

    // A refreshed list keeps the selected venue while it is still listed.
    private string? KeepOrFirstVenueId(PreparedVenue[] venues) =>
        venues.Any(v => string.Equals(v.Id, _selectedVenueId, StringComparison.Ordinal))
            ? _selectedVenueId
            : venues.FirstOrDefault()?.Id;

    private void DrawLoadingState()
    {
        var error = _loadError ?? (_venueFeed.Value == null ? _venueFeed.Error : null);
        if (!string.IsNullOrEmpty(error))
        {
            DrawText(error, UiStyle.ErrorText);
            if (_venueFeed.IsLoading)
            {
                DrawMutedText("Trying again...");
            }
            else
            {
                DrawMutedText($"Trying again {FormatRetryIn(_venueFeed.RetryAt)}.");
                using (ImRaii.Disabled(!_venueFeed.CanRefreshNow))
                {
                    if (ImGui.Button("Retry now"))
                    {
                        TriggerRefresh();
                    }
                }
            }

            return;
        }

        if (_preparedVenuesTask != null)
        {
            ImGui.Text("Preparing venues...");
            return;
        }

        ImGui.Text("Loading venues...");
    }

    private static void DrawActivationPlaceholder() => ImGui.Text("Activating venue list...");

    private void AdvancePostPreparationActivationStage()
    {
        _postPreparationActivationStage = _postPreparationActivationStage switch
        {
            PostPreparationActivationStage.FinalizeState => PostPreparationActivationStage.ListWarmup,
            PostPreparationActivationStage.ListWarmup => PostPreparationActivationStage.DetailWarmup,
            PostPreparationActivationStage.DetailWarmup => PostPreparationActivationStage.None,
            _ => PostPreparationActivationStage.None
        };
    }

    private bool TryGetPreparedVenueDetails(
        PreparedVenue venue,
        out PreparedVenueDetails details,
        out bool cacheHit,
        out bool buildPending)
    {
        if (_preparedVenueDetailsCache.TryGetValue(venue.Id, out var cachedDetails))
        {
            if (TryConsumePreparedVenueScheduleTask(venue.Id, cachedDetails, out var completedDetails))
            {
                cachedDetails = completedDetails;
                _preparedVenueDetailsCache[venue.Id] = cachedDetails;
            }
            else if (cachedDetails.SchedulePending && !_preparedVenueScheduleTasks.ContainsKey(venue.Id))
            {
                EnsurePreparedVenueScheduleBuildStarted(venue);
            }

            details = cachedDetails;
            cacheHit = true;
            buildPending = false;
            return true;
        }

        if (_preparedVenueDetailTasks.TryGetValue(venue.Id, out var task))
        {
            if (!task.IsCompleted)
            {
                cacheHit = false;
                return UseStaleDetails(venue, out details, out buildPending);
            }

            _preparedVenueDetailTasks.Remove(venue.Id);
            _staleVenueDetails.Remove(venue.Id);
            if (task.IsFaulted || task.IsCanceled)
            {
                DalamudServices.PluginLog.Warning(task.Exception?.GetBaseException(), "Could not prepare the details of venue {VenueId}; showing a fallback.", venue.Id);

                details = CreateFallbackPreparedVenueDetails(venue);
                _preparedVenueDetailsCache[venue.Id] = details;
                cacheHit = false;
                buildPending = false;
                return true;
            }

            details = task.Result;
            _preparedVenueDetailsCache[venue.Id] = details;
            cacheHit = false;
            buildPending = false;
            return true;
        }

        EnsurePreparedVenueDetailBuildStarted(venue);
        cacheHit = false;
        return UseStaleDetails(venue, out details, out buildPending);
    }

    private bool UseStaleDetails(PreparedVenue venue, out PreparedVenueDetails details, out bool buildPending)
    {
        if (_staleVenueDetails.TryGetValue(venue.Id, out var stale))
        {
            details = stale;
            buildPending = false;
            return true;
        }

        details = default!;
        buildPending = true;
        return false;
    }

    private bool TryConsumePreparedVenueScheduleTask(
        string venueId,
        PreparedVenueDetails details,
        out PreparedVenueDetails updatedDetails)
    {
        updatedDetails = details;
        if (!_preparedVenueScheduleTasks.TryGetValue(venueId, out var task) || !task.IsCompleted)
        {
            return false;
        }

        _preparedVenueScheduleTasks.Remove(venueId);
        if (task.IsFaulted || task.IsCanceled)
        {
            DalamudServices.PluginLog.Warning(task.Exception?.GetBaseException(), "Could not prepare the schedule of venue {VenueId}.", venueId);

            updatedDetails = details with
            {
                ScheduleRows = [],
                SchedulePending = false
            };
            return true;
        }

        updatedDetails = details with
        {
            ScheduleRows = task.Result,
            SchedulePending = false
        };
        return true;
    }

    private void EnsurePreparedVenueDetailBuildStarted(PreparedVenue venue)
    {
        if (_preparedVenueDetailsCache.ContainsKey(venue.Id) || _preparedVenueDetailTasks.ContainsKey(venue.Id))
        {
            return;
        }

        var cancellationToken = _disposeCts.Token;
        _preparedVenueDetailTasks[venue.Id] = Task.Factory.StartNew(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                }
                catch
                {
                    // Best effort only; some hosts may reject thread priority changes.
                }

                var details = _venuePreparer.BuildPreparedVenueDetails(venue);
                cancellationToken.ThrowIfCancellationRequested();
                return details;
            },
            cancellationToken,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void EnsurePreparedVenueScheduleBuildStarted(PreparedVenue venue)
    {
        if (_preparedVenueScheduleTasks.ContainsKey(venue.Id))
        {
            return;
        }

        var cancellationToken = _disposeCts.Token;
        _preparedVenueScheduleTasks[venue.Id] = Task.Factory.StartNew(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                }
                catch
                {
                    // Best effort only; some hosts may reject thread priority changes.
                }

                var rows = BuildPreparedScheduleRows(venue.Source.Schedule);
                cancellationToken.ThrowIfCancellationRequested();
                return rows;
            },
            cancellationToken,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }
}
