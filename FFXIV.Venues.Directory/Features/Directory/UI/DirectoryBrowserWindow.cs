using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Features.Directory.Text;
using FFXIV.Venues.Directory.Features.Events;
using FFXIV.Venues.Directory.Features.PartyFinder;
using FFXIV.Venues.Directory.Infrastructure.Media;
using FFXIV.Venues.Directory.Infrastructure.Net;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using FFXIV.Venues.Directory.Integrations;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow : Window, IDisposable
{
    private enum PostPreparationActivationStage
    {
        None,
        FinalizeState,
        ListWarmup,
        DetailWarmup
    }

    private readonly record struct SortSpecSnapshot(int ColumnIndex, ImGuiSortDirection Direction);

    private const float BannerMaxWidth = 520f;
    // The list and the details always sit side by side; the filter sidebar is shown only when both still get these widths next to it, and otherwise folds into the Filters popover.
    private const float MinListWidth = 640f;
    private const float MinDetailWidth = 360f;
    private const float FilterSidebarFixedWidth = 352f;
    private static readonly Vector2 DefaultWindowSize = new(1280f, 720f);
    private static readonly Vector2 MinWindowSize = new(1020f, 600f);
    private static readonly TimeSpan PreparedVenueBuildTimeout = TimeSpan.FromSeconds(5);
    private static readonly Vector4 DefaultSectionBackground = PluginTheme.Surface;
    private static readonly Vector4 HighlightSectionBackground = new(0x14 / 255f, 0x18 / 255f, 0x28 / 255f, 1f);

    private readonly PeriodicFeed<DirectoryVenue[]> _venueFeed;
    private readonly Configuration _configuration;
    private readonly LifestreamNavigator _lifestreamIpc;
    private readonly PlotSizeLookup _housingPlotSizeResolver;
    private readonly PartakeClient _partake;
    private readonly RemoteImageCache _remoteImages;
    private readonly CancellationTokenSource _disposeCts = new();

    private Task<PreparedVenue[]>? _preparedVenuesTask;
    private DirectoryVenue[]? _venues;
    private PreparedVenue[]? _preparedVenues;
    // Preparing the venues failed (a bug, not the network).
    private string? _loadError;
    private DateTimeOffset _preparedVenueTaskStartedAtUtc;
    private int _venueFeedVersion = -1;
    private int _venueRefreshVersion;
    private int _preparedVenueTaskVersion;
    private int _preparedGlyphGeneration;
    private long _venueStatusMinute = -1;
    // Details of the previous preparation, shown while the new ones are built so a refresh does not blank them.
    private readonly Dictionary<string, PreparedVenueDetails> _staleVenueDetails = new(StringComparer.Ordinal);

    private string? _selectedVenueId;
    private string _searchText = string.Empty;
    private readonly VenueFilterSettings _filters;
    // Read by the static route builders on the preparation thread.
    private readonly OpenWorldPlaces _openWorldPlaces;
    private readonly VenuePreparer _venuePreparer;
    private ulong _locationCharacterId;

    // The world the character is on and its data center (the last one known while logged out), for "My data center".
    private uint _currentWorldId;
    private string? _currentDataCenter;
    private DateTimeOffset _filtersSaveDueAt;
    private int _soonFilterMinute = -1;

    private readonly List<string> _dataCenters = [];
    private readonly List<string> _regions = [];
    private readonly List<string> _worlds = [];
    private readonly HashSet<string> _favoriteVenueIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visitedVenueIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenVenueIds = new(StringComparer.Ordinal);
    private readonly List<PreparedVenue> _filteredVenues = [];
    private readonly List<PreparedVenue> _sortedVenues = [];
    private readonly Dictionary<string, PreparedVenueDetails> _preparedVenueDetailsCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<PreparedVenueDetails>> _preparedVenueDetailTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<PreparedScheduleRow[]>> _preparedVenueScheduleTasks = new(StringComparer.Ordinal);
    private readonly List<SortSpecSnapshot> _sortSpecSnapshots = [];
    private readonly List<float> _sortedVenueRowHeights = [];
    private readonly List<float> _sortedVenueRowOffsets = [];
    private float _splitRatio = 0.42f;
    private float _rightPaneWidth;
    private float _sortedVenueColumnMetricsWrapWidth = -1f;
    private float _sortedVenueRowMetricsWrapWidth = -1f;
    private float _sortedVenueTotalHeight;
    private bool _filterSidebarShown;
    private bool _filterSidebarFits;
    private float _filterPopoverContentHeight;
    private bool _filteredVenuesDirty = true;
    private bool _sortedVenuesDirty = true;
    private bool _sortedVenueRowMetricsDirty = true;
    private readonly Dictionary<string, int> _selectedRouteIndices = new(StringComparer.Ordinal);
    private PostPreparationActivationStage _postPreparationActivationStage;
    private bool _disposed;

    public DirectoryBrowserWindow(
        PeriodicFeed<DirectoryVenue[]> venueFeed,
        PeriodicFeed<IReadOnlyList<CommunityEvent>> eventFeed,
        PeriodicFeed<IReadOnlyList<PartyFinderAd>> partyFinderFeed,
        Configuration configuration,
        LifestreamNavigator lifestreamIpc,
        PlotSizeLookup housingPlotSizeResolver,
        PartakeClient partake,
        RemoteImageCache remoteImages,
        OpenWorldPlaces openWorldPlaces)
        : base("FFXIV Venues Directory")
    {
        _openWorldPlaces = openWorldPlaces;
        _venuePreparer = new VenuePreparer(housingPlotSizeResolver, openWorldPlaces);
        openWorldPlaces.WarmUp();
        _partake = partake;
        _remoteImages = remoteImages;
        _venueFeed = venueFeed;
        _eventFeed = eventFeed;
        _partyFinderFeed = partyFinderFeed;
        _configuration = configuration;
        _lifestreamIpc = lifestreamIpc;
        _housingPlotSizeResolver = housingPlotSizeResolver;
        _filters = _configuration.Filters ??= new VenueFilterSettings();
        _configuration.Clock ??= _configuration.Use12HourClock ? ClockFormat.TwelveHour : ClockFormat.System;
        Use12HourClock = Resolve12HourClock(_configuration.Clock.Value);
        UiScale.SetFactor(_configuration.InterfaceScale);
        Settings = new SettingsWindow(this);
        InitializeEvents();
        EnsurePreferenceCollectionsInitialized();
        SyncPreferredVenueLookups();

        Size = DefaultWindowSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = MinWindowSize,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        StartDevChecks();
    }

    private IDisposable? _theme;
    private Vector2 _appliedMinimumSize;
    private int _fontVersion = -1;
    private (DirectoryMode Mode, string? VenueId, int? EventId) _detailScrollKey;

    // Hooks for the dev build, a separate project that compiles these sources with its own checks. Here they have no body, so the compiler drops them and every call to them.
    partial void StartDevChecks();

    partial void StopDevChecks();

    // Dev build: scrolls the details where a check asked.
    partial void ApplyDevDetailScroll();

    // Dev build: times each step of a frame.
    partial void ProfileFrameStart();

    partial void ProfileStep(string step);

    partial void ProfileFrameEnd();

    // The plugin's colors for this window and everything it opens (popups, tooltips), unless the user prefers their Dalamud style.
    public override void PreDraw()
    {
        // The minimum size grows with the interface size, so the list and the details fit side by side, but never beyond the game window; the first size stays within it too.
        var minimumSize = UiScale.FitToScreen(MinWindowSize * UiScale.Factor);
        if (_appliedMinimumSize != minimumSize)
        {
            _appliedMinimumSize = minimumSize;
            Size = UiScale.FitToScreen(DefaultWindowSize);
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = minimumSize,
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
            };
        }

        _theme = _configuration.UseDalamudTheme ? null : PluginTheme.Push();
        base.PreDraw();
    }

    public override void PostDraw()
    {
        base.PostDraw();
        DrawDevWindows();
        _theme?.Dispose();
        _theme = null;
    }

    partial void DrawDevWindows();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_filtersSaveDueAt != default)
        {
            SaveFiltersNow();
        }

        StopDevChecks();
        _disposeCts.Cancel();
        _preparedVenuesTask = null;
        _preparedVenueDetailTasks.Clear();
        _preparedVenueScheduleTasks.Clear();
        _disposeCts.Dispose();
    }

    public override void Draw()
    {
        if (_disposed)
        {
            return;
        }

        var activationStage = _postPreparationActivationStage;
        ProfileFrameStart();
        _remoteImages.Trim();
        try
        {
            _venueFeed.Tick();
            ConsumeVenueFeed();
            ProfileStep("feed");
            RefreshPreparedTextForFont();
            var consumedPreparedTask = TryConsumePreparedVenueTask();
            ProfileStep("prepared");
            if (_venues == null || _preparedVenues == null)
            {
                DrawLoadingState();
                return;
            }

            UpdateVenueStatuses();
            ProfileStep("statuses");

            if (consumedPreparedTask)
            {
                DrawActivationPlaceholder();
                return;
            }

            // Another font (interface size, Dalamud font settings): measured row heights and popover sizes are redone.
            if (_fontVersion != PluginUiFont.Version)
            {
                _fontVersion = PluginUiFont.Version;
                InvalidateFilteredVenues();
                _filterPopoverContentHeight = 0f;
            }

            UpdateFilterState();
            UpdateVenueHere();
            UpdateEvents();
            ProfileStep("events");
            UpdateEventVenueMatches();
            ProfileStep("matches");
            RefreshFilteredEventsIfNeeded();
            RefreshFilteredVenuesIfNeeded();
            EnsureSelection(_filteredVenues);
            var selectedVenue = GetSelectedPreparedVenue();
            ProfileStep("filter");

            if (activationStage == PostPreparationActivationStage.FinalizeState)
            {
                DrawActivationPlaceholder();
                return;
            }

            var region = ImGui.GetContentRegionAvail();
            var splitterWidth = Math.Max(Scale(4f), ImGui.GetStyle().ItemSpacing.X);
            var sidebarWidth = Scale(FilterSidebarFixedWidth);
            _filterSidebarFits = region.X >= sidebarWidth + splitterWidth + Scale(MinListWidth) + Scale(MinDetailWidth);
            _filterSidebarShown = _filterSidebarFits && !_configuration.FilterSidebarHidden;
            if (!_filterSidebarShown)
            {
                sidebarWidth = 0f;
            }

            DrawTopBar(activationStage);
            ProfileStep("top-bar");

            var paneHeight = MathF.Max(1f, ImGui.GetContentRegionAvail().Y);
            var contentWidth = MathF.Max(1f, region.X - sidebarWidth - splitterWidth);
            var (leftWidth, rightWidth) = SplitListAndDetails(contentWidth);
            _rightPaneWidth = rightWidth;

            if (_filterSidebarShown)
            {
                using (var filterPane = ImRaii.Child("VenueFilterPane"u8, new Vector2(sidebarWidth, paneHeight), true))
                {
                    if (filterPane)
                    {
                        DrawFilterPaneContent(activationStage);
                    }
                }

                ImGui.SameLine(0f, 0f);
            }

            ProfileStep("sidebar");

            // Filters changed in the sidebar or the top bar apply to the list in the same frame.
            RefreshFilteredVenuesIfNeeded();
            using (var listPane = ImRaii.Child(
                       "VenueListPane"u8,
                       new Vector2(leftWidth, paneHeight),
                       true,
                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (listPane)
                {
                    if (_mode == DirectoryMode.Events)
                    {
                        DrawEventList();
                    }
                    else
                    {
                        DrawVenueTable(_filteredVenues);
                    }
                }
            }

            ProfileStep("list");
            selectedVenue = GetSelectedPreparedVenue();
            ImGui.SameLine(0f, 0f);
            ImGui.InvisibleButton("##VenueListSplitter", new Vector2(splitterWidth, paneHeight));
            var listSplitterMin = ImGui.GetItemRectMin();
            var listSplitterMax = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRectFilled(listSplitterMin, listSplitterMax, ImGui.GetColorU32(ImGuiCol.Border));
            if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
            }

            if (ImGui.IsItemActive())
            {
                var newLeft = leftWidth + ImGui.GetIO().MouseDelta.X;
                _splitRatio = Math.Clamp(newLeft / contentWidth, 0.15f, 0.85f);
            }

            ImGui.SameLine(0f, 0f);
            using (var detailPane = ImRaii.Child("VenueDetailPane"u8, new Vector2(rightWidth, paneHeight), true))
            {
                if (detailPane)
                {
                    // Details of another venue or event start at the top.
                    var detailKey = (_mode, _mode == DirectoryMode.Events ? null : _selectedVenueId, _mode == DirectoryMode.Events ? _selectedEventId : null);
                    if (detailKey != _detailScrollKey)
                    {
                        _detailScrollKey = detailKey;
                        ImGui.SetScrollY(0f);
                    }

                    ApplyDevDetailScroll();

                    if (_mode == DirectoryMode.Events)
                    {
                        if (GetSelectedEvent() is { } selectedEvent)
                        {
                            DrawEventDetails(selectedEvent);
                        }
                        else
                        {
                            DrawMutedText(_filteredEvents.Count == 0 ? string.Empty : "Select an event to see its details.");
                        }
                    }
                    else
                    {
                        if (selectedVenue == null)
                        {
                            DrawText(GetEmptySelectionMessage(), UiStyle.WarningText);
                        }
                        else if (activationStage != PostPreparationActivationStage.None)
                        {
                            ImGui.TextDisabled("Loading venue details...");
                        }
                        else
                        {
                            DrawVenueDetails(selectedVenue, activationStage == PostPreparationActivationStage.None);
                        }
                    }
                }
            }
        }
        finally
        {
            ProfileStep("details");
            ProfileFrameEnd();
            if (activationStage != PostPreparationActivationStage.None)
            {
                AdvancePostPreparationActivationStage();
            }
        }
    }

    // The list gets at least MinListWidth and the details at least MinDetailWidth and at most the banner width; in between, the splitter position is kept as a ratio of the width.
    private (float List, float Detail) SplitListAndDetails(float contentWidth)
    {
        var minList = Scale(MinListWidth);
        var minDetail = Scale(MinDetailWidth);
        if (contentWidth <= minList + minDetail)
        {
            // Narrower than the list and the details need: briefly after a UI scale change, or on a screen too small for the minimum window size.
            var list = contentWidth * minList / (minList + minDetail);
            return (list, contentWidth - list);
        }

        var left = MathF.Min(MathF.Max(contentWidth * _splitRatio, minList), contentWidth - minDetail);
        var right = contentWidth - left;
        var maxDetail = Scale(BannerMaxWidth);
        if (right > maxDetail)
        {
            right = maxDetail;
            left = contentWidth - right;
        }

        return (left, right);
    }

    private void DrawFilterPaneContent(PostPreparationActivationStage activationStage)
    {
        if (activationStage != PostPreparationActivationStage.None)
        {
            DrawActivationPlaceholder();
            return;
        }

        if (_mode == DirectoryMode.Events)
        {
            DrawEventFilters();
        }
        else
        {
            DrawFilters();
        }
    }

    // A venue opened from elsewhere stays selected even when the filters leave the list empty.
    private void EnsureSelection(IReadOnlyList<PreparedVenue> venues)
    {
        if (_selectedVenueId != null && _selectedVenueId == _pinnedVenueId)
        {
            return;
        }

        if (venues.Count == 0)
        {
            _selectedVenueId = null;
            return;
        }

        if (_selectedVenueId == null || venues.All(v => !string.Equals(v.Id, _selectedVenueId, StringComparison.Ordinal)))
        {
            _selectedVenueId = venues[0].Id;
        }
    }

    private PreparedVenue? GetSelectedPreparedVenue() =>
        _filteredVenues.FirstOrDefault(v => string.Equals(v.Id, _selectedVenueId, StringComparison.Ordinal)) ??
        (_selectedVenueId != null && _selectedVenueId == _pinnedVenueId
            ? _preparedVenues?.FirstOrDefault(v => string.Equals(v.Id, _selectedVenueId, StringComparison.Ordinal)) ??
              _eventVenueMatches.UnlistedVenues.FirstOrDefault(v => string.Equals(v.Id, _selectedVenueId, StringComparison.Ordinal))
            : null);

    private int GetSelectedRouteIndex(string venueId, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        if (!_selectedRouteIndices.TryGetValue(venueId, out var index))
        {
            _selectedRouteIndices[venueId] = 0;
            return 0;
        }

        if (index >= 0 && index < count)
        {
            return index;
        }

        _selectedRouteIndices[venueId] = 0;
        return 0;
    }

    private static float Scale(float value) => value * UiScale.Total;
}
