using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Media;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Integrations;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using TimeZoneConverter;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed class DirectoryBrowserWindow : Window
{
    private sealed record VenueRouteOption(string DisplayText, string CopyText, string? LifestreamArguments);
    private sealed class PreparedVenue
    {
        public required DirectoryVenue Venue { get; init; }
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string SearchText { get; init; }
        public required HashSet<string> TagSet { get; init; }
        public required string? Region { get; init; }
        public required string? DataCenter { get; init; }
        public required string? World { get; init; }
        public required bool IsOpen { get; init; }
        public required bool IsNsfw { get; init; }
        public required bool HasAdultServices { get; init; }
        public required bool IsApartment { get; init; }
        public required HousingPlotSize? PlotSize { get; init; }
        public required string VenueTypeLabel { get; init; }
        public required int VenueTypeSortKey { get; init; }
        public required string StatusText { get; init; }
        public required DateTimeOffset StatusSortKey { get; init; }
        public required string TableAddress { get; init; }
        public required string LocationKey { get; init; }
        public required List<VenueRouteOption> RouteOptions { get; init; }
        public required string? SanitizedDescription { get; init; }
        public required int SanitizedDescriptionHash { get; init; }
        public required DirectorySchedule[] SortedSchedule { get; init; }
    }

    private const float BannerMaxWidth = 520f;
    private const float MinPanelWidth = 320f;
    private static readonly string[] Regions = { "North America", "Europe", "Oceania", "Japan" };
    private static readonly Vector4 DefaultSectionBackground = new(0.20f, 0.20f, 0.23f, 0.95f);
    private static readonly Vector4 HighlightSectionBackground = new(0.12f, 0.12f, 0.12f, 0.95f);
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLinkRegex = new(@"\[(.*?)\]\((.*?)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownStrongRegex = new(@"(\*\*|__|~~)(.*?)\1", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownEmRegex = new(@"(\*|_)(.*?)\1", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlRegex = new(@"https?://[^\s\)\]\}<>\""]+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex OnlyPunctuationLineRegex = new(@"^[=\-_*`~|+\\/]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HeadingEqualsRegex = new(@"^\s*=+\s*(.*?)\s*=+\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HeadingSingleEqualsRegex = new(@"^\s*=\s*(.*?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LeadingBulletRegex = new(@"^\s*[-*•▪◦·]+\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LeadingDecorationRegex = new(@"^[\p{S}\s]+(?=[\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TrailingDecorationRegex = new(@"(?<=[\p{L}\p{N}])[\p{S}\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OverrideRouteRegex = new(
        @"(?:(?<label>[A-Za-z0-9'&()\- ]{2,40}):\s*)?(?<address>[A-Za-z][A-Za-z0-9'’\- ]+(?:,\s*[A-Za-z][A-Za-z0-9'’\- ]+){1,2},\s*Ward\s*\d+(?:\s*(?:Sub|Subdivision))?\s*,\s*(?:Plot|Apartment|Apt)\s*\d+(?:,\s*(?:Sub|Subdivision))?(?:,\s*Room\s*\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Dictionary<int, char> SmallCapsMap = new()
    {
        { 0x1D00, 'A' },
        { 0x0299, 'B' },
        { 0x1D04, 'C' },
        { 0x1D05, 'D' },
        { 0x1D07, 'E' },
        { 0xA730, 'F' },
        { 0x0262, 'G' },
        { 0x029C, 'H' },
        { 0x026A, 'I' },
        { 0x1D0A, 'J' },
        { 0x1D0B, 'K' },
        { 0x029F, 'L' },
        { 0x1D0D, 'M' },
        { 0x0274, 'N' },
        { 0x1D0F, 'O' },
        { 0x1D18, 'P' },
        { 0x01EB, 'Q' },
        { 0x0280, 'R' },
        { 0x1D1B, 'T' },
        { 0x1D1C, 'U' },
        { 0x1D20, 'V' },
        { 0x1D21, 'W' },
        { 0x028F, 'Y' },
        { 0x1D22, 'Z' },
    };
    private static readonly Dictionary<int, char> SpecialMap = new()
    {
        { 0x210E, 'h' },
        { 0x2113, 'l' },
        { 0x1D70A, 'o' },
        { 0x1D70B, 'o' },
        { 0x1D710, 'u' },
    };
    private static readonly Dictionary<string, string> TimeZoneAbbreviationMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "Coordinated Universal Time", "UTC" },
            { "Greenwich Mean Time", "GMT" },
            { "Eastern Standard Time", "EST" },
            { "Eastern Daylight Time", "EDT" },
            { "Central Standard Time", "CST" },
            { "Central Daylight Time", "CDT" },
            { "Mountain Standard Time", "MST" },
            { "Mountain Daylight Time", "MDT" },
            { "Pacific Standard Time", "PST" },
            { "Pacific Daylight Time", "PDT" },
            { "Alaskan Standard Time", "AKST" },
            { "Alaskan Daylight Time", "AKDT" },
            { "Hawaiian Standard Time", "HST" },
            { "Atlantic Standard Time", "AST" },
            { "Atlantic Daylight Time", "ADT" },
            { "Greenwich Standard Time", "GMT" },
            { "GMT Standard Time", "GMT" },
            { "GMT Daylight Time", "GMT" },
            { "Central European Standard Time", "CET" },
            { "Central European Summer Time", "CEST" },
            { "W. Europe Standard Time", "CET" },
            { "W. Europe Daylight Time", "CEST" },
            { "E. Europe Standard Time", "EET" },
            { "E. Europe Daylight Time", "EEST" },
            { "Russian Standard Time", "MSK" },
            { "Japan Standard Time", "JST" },
            { "AUS Eastern Standard Time", "AEST" },
            { "AUS Eastern Daylight Time", "AEDT" },
            { "Cen. Australia Standard Time", "ACST" },
            { "Cen. Australia Daylight Time", "ACDT" },
            { "W. Australia Standard Time", "AWST" },
            { "New Zealand Standard Time", "NZST" },
            { "New Zealand Daylight Time", "NZDT" }
        };
    private static readonly Dictionary<string, TimeZoneInfo?> TimeZoneInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object TimeZoneInfoCacheGate = new();
    private static readonly Dictionary<int, string> ProblemSymbolMap = new()
    {
        { 0x2728, "*" }, // ✨
        { 0x1F48B, "•" }, // 💋
        { 0x1F3B6, "♪" }, // 🎶
        { 0x1F3A7, "♪" }, // 🎧
        { 0x1F378, "*" }, // 🍸
        { 0x1F376, "*" }, // 🍶
        { 0x1F3B2, "•" }, // 🎲
        { 0x1F3AD, "*" }, // 🎭
        { 0x1F389, "*" }, // 🎉
        { 0x1F525, "*" }, // 🔥
        { 0x1F319, "*" }, // 🌙
        { 0x1F31F, "*" }, // 🌟
        { 0x2B50, "*" }, // ⭐
        { 0x2605, "*" }, // ★
        { 0x2606, "*" }, // ☆
        { 0x1F338, "*" }, // 🌸
        { 0x1F940, "*" }, // 🥀
        { 0x2740, "*" }, // ❀
        { 0x1F49C, "♥" }, // 💜
        { 0x1F49B, "♥" }, // 💛
        { 0x1F5A4, "♥" }, // 🖤
        { 0x2764, "♥" }, // ❤
        { 0x2661, "♥" }, // ♡
        { 0x1F4CD, "•" }, // 📍
        { 0x1F517, "->" }, // 🔗
        { 0x1F4AC, "" }, // 💬
        { 0xFE0F, "" }, // VS16
        { 0x200D, "" }, // ZWJ
        { 0x2060, "" } // WORD JOINER
    };

    private readonly HttpClient _httpClient;
    private readonly VenueBannerCache _venueService;
    private readonly Configuration _configuration;
    private readonly LifestreamNavigator _lifestreamIpc;
    private readonly PlotSizeLookup _housingPlotSizeResolver;

    private Task<DirectoryVenue[]?>? _venuesTask;
    private Task<PreparedVenue[]>? _prepareVenuesTask;
    private DirectoryVenue[]? _venues;
    private string? _loadError;
    private DateTimeOffset _lastRefresh;

    private string? _selectedVenueId;
    private string _searchText = string.Empty;
    private string _tagFilter = string.Empty;
    private string? _selectedRegion;
    private string? _selectedDataCenter;
    private string? _selectedWorld;
    private bool _onlyOpen = true;
    private bool _favoritesOnly;
    private bool _visitedOnly;
    private bool _sfwOnly;
    private bool _nsfwOnly;
    private bool _sizeSmall = true;
    private bool _sizeMedium = true;
    private bool _sizeLarge = true;
    private bool _sizeApartment = true;

    private readonly List<string> _dataCenters = new();
    private readonly List<string> _regions = new();
    private readonly List<string> _worlds = new();
    private readonly List<PreparedVenue> _preparedVenues = new();
    private readonly Dictionary<string, PreparedVenue> _preparedVenueById = new(StringComparer.Ordinal);
    private readonly List<PreparedVenue> _filteredVenues = new();
    private readonly List<PreparedVenue> _sortedVenues = new();
    private readonly HashSet<string> _favoriteVenueIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visitedVenueIds = new(StringComparer.Ordinal);
    private float _splitRatio = 0.42f;
    private float _rightPaneWidth;
    private readonly Dictionary<string, int> _selectedRouteIndices = new(StringComparer.Ordinal);
    private bool _filtersDirty = true;
    private bool _sortDirty = true;
    private int _sortSignature = int.MinValue;

    public DirectoryBrowserWindow(
        HttpClient httpClient,
        VenueBannerCache venueService,
        Configuration configuration,
        LifestreamNavigator lifestreamIpc,
        PlotSizeLookup housingPlotSizeResolver)
        : base("FFXIV Venues Directory")
    {
        _httpClient = httpClient;
        _venueService = venueService;
        _configuration = configuration;
        _lifestreamIpc = lifestreamIpc;
        _housingPlotSizeResolver = housingPlotSizeResolver;
        EnsurePreferenceCollectionsInitialized();
        InitializePreferenceLookups();

        Size = ImGuiHelpers.ScaledVector2(1200f, 800f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(300f, 200f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        EnsureVenuesRequested();
        TryConsumeVenueTask();
        TryConsumePreparedVenuesTask();

        if (_venues == null || _prepareVenuesTask is { IsCompleted: false })
        {
            DrawLoadingState();
            return;
        }

        RefreshFilteredVenuesIfNeeded();
        EnsureSelection(_filteredVenues);

        DrawToolbar(_filteredVenues.Count);
        ImGui.Separator();

        var region = ImGui.GetContentRegionAvail();
        var splitterWidth = Math.Max(Scale(4f), ImGui.GetStyle().ItemSpacing.X);
        var minPanelWidth = Scale(MinPanelWidth);
        var maxBannerWidth = Scale(BannerMaxWidth);
        var usableWidth = Math.Max(region.X - splitterWidth, minPanelWidth * 2);
        var leftWidth = Math.Clamp(usableWidth * _splitRatio, minPanelWidth, usableWidth - minPanelWidth);
        var rightWidth = usableWidth - leftWidth;
        if (rightWidth > maxBannerWidth)
        {
            rightWidth = maxBannerWidth;
            leftWidth = usableWidth - rightWidth;
            _splitRatio = leftWidth / usableWidth;
        }

        _rightPaneWidth = rightWidth;

        using (var listPane = ImRaii.Child("VenueListPane"u8, new Vector2(leftWidth, 0f), true))
        {
            if (listPane)
            {
                DrawFilters();
                ImGui.Separator();
                DrawVenueTable(_filteredVenues);
            }
        }

        ImGui.SameLine(0f, 0f);
        ImGui.InvisibleButton("##VenueSplitter", new Vector2(splitterWidth, region.Y));
        var splitterMin = ImGui.GetItemRectMin();
        var splitterMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRectFilled(splitterMin, splitterMax, ImGui.GetColorU32(ImGuiCol.Border));
        if (ImGui.IsItemActive())
        {
            var newLeft = Math.Clamp(leftWidth + ImGui.GetIO().MouseDelta.X, minPanelWidth, usableWidth - minPanelWidth);
            _splitRatio = Math.Clamp(newLeft / usableWidth, 0.15f, 0.85f);
        }

        ImGui.SameLine(0f, 0f);
        using (var detailPane = ImRaii.Child("VenueDetailPane"u8, new Vector2(rightWidth, 0f), true))
        {
            if (detailPane)
            {
                PreparedVenue? selected = null;
                if (_selectedVenueId != null)
                {
                    _preparedVenueById.TryGetValue(_selectedVenueId, out selected);
                }

                if (selected == null)
                {
                    ImGui.TextColored(ImGuiColors.DalamudYellow, GetEmptySelectionMessage());
                }
                else
                {
                    DrawVenueDetails(selected);
                }
            }
        }
    }

    private void EnsureVenuesRequested()
    {
        if (_venuesTask == null && _venues == null && _loadError == null)
        {
            TriggerRefresh();
        }
    }

    private void TriggerRefresh()
    {
        _venuesTask = _httpClient.GetFromJsonAsync<DirectoryVenue[]>("venue?approved=true");
        _prepareVenuesTask = null;
        _loadError = null;
        _venues = null;
        _preparedVenues.Clear();
        _preparedVenueById.Clear();
        _filteredVenues.Clear();
        _sortedVenues.Clear();
        _selectedRouteIndices.Clear();
        _filtersDirty = true;
        _sortDirty = true;
        _sortSignature = int.MinValue;
    }

    private void TryConsumeVenueTask()
    {
        if (_venuesTask == null || !_venuesTask.IsCompleted)
        {
            return;
        }

        if (_venuesTask.IsFaulted)
        {
            _loadError = _venuesTask.Exception?.GetBaseException().Message ?? "Failed to load venues.";
            _venuesTask = null;
            return;
        }

        _venues = _venuesTask.Result ?? Array.Empty<DirectoryVenue>();
        _venuesTask = null;
        _loadError = null;
        _lastRefresh = DateTimeOffset.UtcNow;
        var venues = _venues;
        _prepareVenuesTask = Task.Run(() => BuildPreparedVenues(venues));
    }

    private void TryConsumePreparedVenuesTask()
    {
        if (_prepareVenuesTask == null || !_prepareVenuesTask.IsCompleted)
        {
            return;
        }

        if (_prepareVenuesTask.IsFaulted)
        {
            _loadError = _prepareVenuesTask.Exception?.GetBaseException().Message ?? "Failed to prepare venues.";
            _prepareVenuesTask = null;
            _venues = null;
            return;
        }

        ApplyPreparedVenues(_prepareVenuesTask.Result);
        _prepareVenuesTask = null;
    }

    private void DrawLoadingState()
    {
        if (!string.IsNullOrEmpty(_loadError))
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, _loadError);
            if (ImGui.Button("Retry"))
            {
                TriggerRefresh();
            }

            return;
        }

        if (_prepareVenuesTask is { IsCompleted: false })
        {
            ImGui.Text("Preparing venue directory...");
            return;
        }

        ImGui.Text("Fetching venues from api.ffxivvenues.com...");
    }

    private void DrawToolbar(int visibleCount)
    {
        if (ImGui.Button("Refresh"))
        {
            TriggerRefresh();
        }

        ImGui.SameLine();
        ImGui.Text($"{visibleCount} / {_venues?.Length ?? 0} venues");

        if (_lastRefresh != default)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"Updated {FormatRelativeTime(_lastRefresh)}");
        }

        if (!string.IsNullOrEmpty(_loadError))
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudRed, _loadError);
        }
    }

    private void DrawFilters()
    {
        using (ImRaii.ItemWidth(-1f))
        {
            if (ImGui.InputTextWithHint("##VenueSearch", "Search by name, description or tag...", ref _searchText, 160))
            {
                MarkFiltersDirty();
            }
        }

        using (var filterTable = ImRaii.Table("FilterLayout"u8, 2, ImGuiTableFlags.SizingStretchSame))
        {
            if (!filterTable)
            {
                return;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            using (var regionCombo = ImRaii.Combo("Region"u8, (_selectedRegion ?? "Any")))
            {
                if (regionCombo)
                {
                    if (ImGui.Selectable("Any", _selectedRegion == null))
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

            ImGui.TableNextColumn();
            using (var dataCenterCombo = ImRaii.Combo("Data Center"u8, (_selectedDataCenter ?? "Any")))
            {
                if (dataCenterCombo)
                {
                    if (ImGui.Selectable("Any", _selectedDataCenter == null))
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

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            using (var worldCombo = ImRaii.Combo("World"u8, (_selectedWorld ?? "Any")))
            {
                if (worldCombo)
                {
                    if (ImGui.Selectable("Any", _selectedWorld == null))
                    {
                        _selectedWorld = null;
                        MarkFiltersDirty();
                    }

                    ImGui.Separator();
                    foreach (var world in _worlds)
                    {
                        if (ImGui.Selectable(world, string.Equals(world, _selectedWorld, StringComparison.OrdinalIgnoreCase)))
                        {
                            _selectedWorld = world;
                            MarkFiltersDirty();
                        }
                    }
                }
            }

            ImGui.TableNextColumn();
            if (ImGui.InputTextWithHint("Tags", "Comma separated tags", ref _tagFilter, 128))
            {
                MarkFiltersDirty();
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawSizeFilter();
            ImGui.TableNextColumn();
            DrawFilterToggles();
        }
    }

    private void DrawSizeFilter()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("House size");
        ImGui.SameLine(0f, Scale(20f));

        var changed = false;
        changed |= DrawSizeToggle("Apartment", ref _sizeApartment);
        ImGui.SameLine();
        changed |= DrawSizeToggle("Small", ref _sizeSmall);
        ImGui.SameLine();
        changed |= DrawSizeToggle("Medium", ref _sizeMedium);
        ImGui.SameLine();
        changed |= DrawSizeToggle("Large", ref _sizeLarge);
        if (changed)
        {
            MarkFiltersDirty();
        }
    }

    private bool DrawSizeToggle(string label, ref bool flag)
    {
        var changed = ImGui.Checkbox(label, ref flag);
        if (changed && !_sizeApartment && !_sizeSmall && !_sizeMedium && !_sizeLarge)
        {
            flag = true;
        }

        return changed;
    }

    private void DrawFilterToggles()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Filters");
        ImGui.SameLine(0f, Scale(20f));

        var changed = ImGui.Checkbox("Open now##filter", ref _onlyOpen);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Favorite##filter", ref _favoritesOnly);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Visited##filter", ref _visitedOnly);
        ImGui.SameLine();
        var sfwChanged = ImGui.Checkbox("SFW only##filter", ref _sfwOnly);
        changed |= sfwChanged;
        if (sfwChanged && _sfwOnly)
        {
            _nsfwOnly = false;
            changed = true;
        }

        ImGui.SameLine();
        var nsfwChanged = ImGui.Checkbox("NSFW only##filter", ref _nsfwOnly);
        changed |= nsfwChanged;
        if (nsfwChanged && _nsfwOnly)
        {
            _sfwOnly = false;
            changed = true;
        }

        if (changed)
        {
            MarkFiltersDirty();
        }
    }

    private void DrawVenueTable(IReadOnlyList<PreparedVenue> venues)
    {
        if (venues.Count == 0)
        {
            ImGui.TextDisabled(GetEmptySelectionMessage());
            return;
        }

        var flags = ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Sortable;
        var size = ImGui.GetContentRegionAvail();
        using (var summaryTable = ImRaii.Table("VenueSummaryTable"u8, 4, flags, size))
        {
            if (!summaryTable)
            {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Venue", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort, 0.34f);
            ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthStretch, 0.44f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, Scale(40f));
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.15f);
            ImGui.TableHeadersRow();

            EnsureSortedVenues(ImGui.TableGetSortSpecs(), venues);
            var rowIndex = 0;
            foreach (var venue in _sortedVenues)
            {
                var open = venue.IsOpen;
                var nameColor = open ? ImGuiColors.DalamudViolet : ImGuiColors.ParsedGrey;
                var statusText = venue.StatusText;
                var isSelected = string.Equals(_selectedVenueId, venue.Id, StringComparison.Ordinal);
                var addressText = venue.TableAddress;
                var venueTypeLabel = venue.VenueTypeLabel;

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(1);
                var addressColumnWidth = MathF.Max(1f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
                var addressWrapWidth = MathF.Max(1f, addressColumnWidth);
                var addressHeight = ImGui.CalcTextSize(addressText, false, addressWrapWidth).Y;
                var rowHeight = MathF.Max(MathF.Max(ImGui.GetTextLineHeight(), addressHeight), Scale(20f));

                ImGui.TableSetColumnIndex(0);
                using (ImRaii.PushId(rowIndex++))
                {
                    using var rowHighlight = ImRaii.PushColor(ImGuiCol.Header, new Vector4(0.26f, 0.30f, 0.50f, 0.80f))
                        .Push(ImGuiCol.HeaderHovered, new Vector4(0.24f, 0.28f, 0.45f, 0.85f))
                        .Push(ImGuiCol.HeaderActive, new Vector4(0.30f, 0.35f, 0.58f, 0.90f));
                    if (ImGui.Selectable(venue.DisplayName, isSelected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight)))
                    {
                        _selectedVenueId = venue.Id;
                    }
                }
                ImGui.TableNextColumn();
                ImGui.TextWrapped(addressText);
                ImGui.TableNextColumn();
                var badgeSide = Scale(18f);
                var available = MathF.Max(0f, ImGui.GetColumnWidth() - ImGui.GetStyle().CellPadding.X * 2f);
                var centeredOffset = MathF.Max(0f, (available - badgeSide) * 0.5f);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centeredOffset);
                DrawSizeBadge(venueTypeLabel, $"size_{venue.Id}");
                ImGui.TableNextColumn();
                ImGui.TextColored(nameColor, statusText);
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

    private void DrawVenueDetails(PreparedVenue venue)
    {
        var source = venue.Venue;
        var banner = _venueService.GetVenueBanner(source.Id, source.BannerUri);
        if (banner != null)
        {
            var padding = ImGui.GetStyle().WindowPadding.X * 2f;
            var maxWidth = MathF.Max(0f, _rightPaneWidth - padding);
            var width = MathF.Min(maxWidth, Scale(BannerMaxWidth));
            var aspect = banner.Width == 0 ? 0.5f : banner.Height / (float)banner.Width;
            var size = new Vector2(width, MathF.Max(Scale(120f), width * aspect));
            ImGui.Image(banner.Handle, size);
        }

        ImGui.SetWindowFontScale(1.5f);
        var headerName = venue.DisplayName;
        var headerSize = ImGui.CalcTextSize(headerName);
        var headerPos = ImGui.GetCursorScreenPos();
        var headerDrawList = ImGui.GetWindowDrawList();
        var headerShadow = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f));
        var headerColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        headerDrawList.AddText(new Vector2(headerPos.X - Scale(1f), headerPos.Y), headerShadow, headerName);
        headerDrawList.AddText(new Vector2(headerPos.X + Scale(1f), headerPos.Y), headerShadow, headerName);
        headerDrawList.AddText(new Vector2(headerPos.X, headerPos.Y - Scale(1f)), headerShadow, headerName);
        headerDrawList.AddText(new Vector2(headerPos.X, headerPos.Y + Scale(1f)), headerShadow, headerName);
        headerDrawList.AddText(headerPos, headerColor, headerName);
        ImGui.SetCursorScreenPos(new Vector2(headerPos.X, headerPos.Y + headerSize.Y));
        ImGui.SetWindowFontScale(1f);

        var routeOptions = venue.RouteOptions;
        var selectedRouteIndex = GetSelectedRouteIndex(venue.Id, routeOptions.Count);
        var selectedRoute = routeOptions[selectedRouteIndex];
        ImGui.TextDisabled(selectedRoute.DisplayText);

        if (routeOptions.Count > 1)
        {
            using (ImRaii.ItemWidth(-1f))
            using (var routeCombo = ImRaii.Combo("##VenueRouteSelector"u8, selectedRoute.DisplayText))
            {
                if (routeCombo)
                {
                    for (var i = 0; i < routeOptions.Count; i++)
                    {
                        var isSelected = i == selectedRouteIndex;
                        if (ImGui.Selectable(routeOptions[i].DisplayText, isSelected))
                        {
                            _selectedRouteIndices[venue.Id] = i;
                            selectedRouteIndex = i;
                            selectedRoute = routeOptions[i];
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                }
            }
        }

        using (var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ImGuiHelpers.ScaledVector2(10f, 6f))
                                   .Push(ImGuiStyleVar.FrameRounding, Scale(6f)))
        {
            if (ImGui.Button("Copy address"))
            {
                ImGui.SetClipboardText(selectedRoute.CopyText);
            }

            if (_lifestreamIpc.IsAvailable)
            {
                ImGui.SameLine();
                using var lifestreamDisabled = ImRaii.Disabled(string.IsNullOrWhiteSpace(selectedRoute.LifestreamArguments));
                if (ImGui.Button("Visit (Lifestream)"))
                {
                    var arguments = selectedRoute.LifestreamArguments;
                    if (!string.IsNullOrEmpty(arguments) &&
                        !_lifestreamIpc.TryExecuteCommand(arguments, out var errorMessage))
                    {
                        DalamudServices.ChatGui.PrintError($"Failed to execute Lifestream command: {errorMessage}");
                    }
                }
            }

            if (source.Website != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Website"))
                {
                    Util.OpenLink(source.Website.ToString());
                }
            }

            if (source.Discord != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Discord"))
                {
                    Util.OpenLink(source.Discord.ToString());
                }
            }
        }

        using (ImRaii.PushId(HashCode.Combine("VenuePreferenceActions", venue.Id)))
        {
            var isFavorite = _favoriteVenueIds.Contains(venue.Id);
            if (ImGui.Checkbox("Favorite venue", ref isFavorite))
            {
                SetPreferredVenue(_configuration.FavoriteVenueIds, _favoriteVenueIds, venue.Id, isFavorite);
            }

            ImGui.SameLine();
            var isVisited = _visitedVenueIds.Contains(venue.Id);
            if (ImGui.Checkbox("Visited", ref isVisited))
            {
                SetPreferredVenue(_configuration.VisitedVenueIds, _visitedVenueIds, venue.Id, isVisited);
            }
        }

        var warningText = GetVenueWarningText(venue.IsNsfw, venue.HasAdultServices);
        if (warningText != null)
        {
            DrawSection("NsfwWarningCard", HighlightSectionBackground, () =>
            {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "Warning:");
                ImGui.SameLine();
                ImGui.TextWrapped(warningText);
            });
        }

        if (!string.IsNullOrWhiteSpace(venue.SanitizedDescription))
        {
            DrawSection("DescriptionCard", () =>
            {
                DrawDescriptionWithLinks(venue.SanitizedDescription, venue.Id, venue.SanitizedDescriptionHash);
            });
        }

        DrawSection("ScheduleCard", HighlightSectionBackground, () =>
        {
            if (source.Resolution != null)
            {
                var resolution = source.Resolution;
                var label = resolution.IsNow
                    ? $"Open now until {FormatShortTime(resolution.End)}!"
                    : $"Next open {resolution.Start.ToLocalTime().ToString("dddd", CultureInfo.InvariantCulture)} at {FormatShortTime(resolution.Start)}";
                ImGui.TextColored(ImGuiColors.DalamudViolet, label);
            }

            if (venue.SortedSchedule.Length > 0)
            {
                var tableFlags = ImGuiTableFlags.SizingStretchProp |
                                 ImGuiTableFlags.NoHostExtendX |
                                 ImGuiTableFlags.BordersOuterH |
                                 ImGuiTableFlags.BordersInnerH;
                using (var scheduleTable = ImRaii.Table("VenueScheduleTable"u8, 2, tableFlags))
                {
                    if (scheduleTable)
                    {
                        ImGui.TableSetupColumn("Day", ImGuiTableColumnFlags.WidthStretch, 0.62f);
                        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.38f);
                        var currentDay = DateTime.Now.DayOfWeek;

                        foreach (var schedule in venue.SortedSchedule)
                        {
                            var (start, end, _, localDay) = FormatScheduleTimes(schedule, currentDay);
                            var label = FormatScheduleLabel(schedule, localDay);
                            var isActive = schedule.Resolution?.IsNow == true;
                            var labelColor = isActive ? ImGuiColors.DalamudViolet : ImGuiColors.DalamudWhite;

                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.TextColored(labelColor, label);
                            ImGui.TableNextColumn();
                            ImGui.TextColored(labelColor, $"{start} - {end}");
                        }
                    }
                }

                ImGui.TextColored(ImGuiColors.DalamudGrey, "All times are in your timezone.");
            }
        });

        if (source.Tags?.Count > 0)
        {
            DrawSection("TagsCard", () => DrawTagChips(source.Tags));
        }
    }

    private void DrawSection(string id, Action content) =>
        DrawSection(id, null, content);

    private void DrawSection(string id, Vector4? backgroundOverride, Action content)
    {
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var contentWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var bg = ImGui.GetColorU32(backgroundOverride ?? DefaultSectionBackground);
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        var padding = ImGuiHelpers.ScaledVector2(12f, 10f);
        using var sectionId = ImRaii.PushId(id.GetHashCode(StringComparison.Ordinal));

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        using (ImRaii.Group())
        {
            ImGuiHelpers.ScaledDummy(0f, 10f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding.X);
            var wrapRightEdge = ImGui.GetCursorPosX() + MathF.Max(0f, contentWidth - padding.X * 2f);
            ImGui.PushTextWrapPos(wrapRightEdge);
            using (ImRaii.Group())
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudWhite))
            {
                content();
            }
            ImGui.PopTextWrapPos();

            ImGuiHelpers.ScaledDummy(0f, 10f);
        }

        drawList.ChannelsSetCurrent(0);

        var min = startPos;
        var max = ImGui.GetItemRectMax();
        max = new Vector2(min.X + contentWidth, max.Y);
        drawList.AddRectFilled(min, max, bg, Scale(6f));
        drawList.AddRect(min, max, border, Scale(6f));
        drawList.ChannelsMerge();

        ImGui.Spacing();
    }

    private static void DrawTagChips(IEnumerable<string> tags)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var framePadding = ImGuiHelpers.ScaledVector2(8f, 4f);
        var startPosX = ImGui.GetCursorPosX();
        var rightInset = Scale(10f);
        var maxWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - rightInset);
        var x = startPosX;
        var y = ImGui.GetCursorPosY();
        var rowHeight = 0f;
        var startScreen = ImGui.GetCursorScreenPos();
        var rightEdge = startScreen.X + maxWidth;
        var drawList = ImGui.GetWindowDrawList();

        using var chipStyle = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, framePadding)
            .Push(ImGuiStyleVar.FrameRounding, Scale(6f));

        var tagIndex = 0;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var size = ImGui.CalcTextSize(tag);
            var chipWidth = size.X + framePadding.X * 2f;
            var chipHeight = size.Y + framePadding.Y * 2f;

            var screenX = startScreen.X + (x - startPosX);
            if (screenX + chipWidth > rightEdge && x > startPosX)
            {
                x = startPosX;
                y += rowHeight + spacing;
                rowHeight = 0f;
            }

            ImGui.SetCursorPos(new Vector2(x, y));
            ImGui.InvisibleButton($"##tag{tagIndex++}", new Vector2(chipWidth, chipHeight));
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var bg = ImGui.GetColorU32(new Vector4(0.24f, 0.24f, 0.28f, 1f));
            var border = ImGui.GetColorU32(ImGuiCol.Border);
            drawList.AddRectFilled(min, max, bg, Scale(6f));
            drawList.AddRect(min, max, border, Scale(6f));
            drawList.AddText(new Vector2(min.X + framePadding.X, min.Y + framePadding.Y), ImGui.GetColorU32(ImGuiCol.Text), tag);
            x += chipWidth + spacing;
            rowHeight = MathF.Max(rowHeight, chipHeight);
        }

        ImGui.SetCursorPos(new Vector2(startPosX, y + rowHeight + spacing));
    }

    private static string SanitizeDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = NormalizeDisplayText(value);
        text = HtmlTagRegex.Replace(text, string.Empty);
        text = MarkdownLinkRegex.Replace(text, "$1 ($2)");
        text = MarkdownStrongRegex.Replace(text, "$2");
        text = MarkdownEmRegex.Replace(text, "$2");
        text = text.Replace("`", string.Empty);
        text = text.Replace("\r\n", "\n");

        var lines = text.Split('\n');
        var normalizedLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var normalized = MultiWhitespaceRegex.Replace(line, " ").Trim();
            if (normalized.Length == 0)
            {
                normalizedLines.Add(string.Empty);
                continue;
            }

            var headingMatch = HeadingEqualsRegex.Match(normalized);
            if (headingMatch.Success)
            {
                normalized = headingMatch.Groups[1].Value.Trim();
            }
            else
            {
                var singleHeading = HeadingSingleEqualsRegex.Match(normalized);
                if (singleHeading.Success)
                {
                    normalized = singleHeading.Groups[1].Value.Trim();
                }
            }

            normalized = LeadingDecorationRegex.Replace(normalized, string.Empty);
            normalized = TrailingDecorationRegex.Replace(normalized, string.Empty);
            var hadListMarker = LeadingBulletRegex.IsMatch(normalized);
            normalized = LeadingBulletRegex.Replace(normalized, string.Empty).Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var cleaned = hadListMarker ? $"• {normalized}" : normalized;
            if (cleaned.Length == 0)
            {
                continue;
            }

            if (cleaned.Length > 6 && OnlyPunctuationLineRegex.IsMatch(cleaned))
            {
                continue;
            }

            normalizedLines.Add(cleaned);
        }

        var merged = new List<string>(normalizedLines.Count);
        foreach (var line in normalizedLines)
        {
            if (line.Length == 0)
            {
                if (merged.Count > 0 && merged[^1].Length > 0)
                {
                    merged.Add(string.Empty);
                }

                continue;
            }

            if (merged.Count == 0 || merged[^1].Length == 0)
            {
                merged.Add(line);
                continue;
            }

            if (ShouldJoinDescriptionLines(merged[^1], line))
            {
                merged[^1] += " " + line;
            }
            else
            {
                merged.Add(line);
            }
        }

        return string.Join("\n", merged).Trim();
    }

    private static void DrawDescriptionWithLinks(string sanitized, string venueId, int paragraphHash)
    {
        var lines = sanitized.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                ImGui.Spacing();
                continue;
            }

            var matches = UrlRegex.Matches(line);
            if (matches.Count == 0)
            {
                ImGui.TextWrapped(line);
                continue;
            }

            var cursor = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (match.Index > cursor)
                {
                    var prefix = line.Substring(cursor, match.Index - cursor);
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        ImGui.TextUnformatted(prefix);
                        ImGui.SameLine(0f, 0f);
                    }
                }

                var rawUrl = match.Value;
                var url = rawUrl.TrimEnd('.', ',', ';', ':', '!', '?', ')');
                var trailing = rawUrl.Substring(url.Length);

                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    using var linkId = ImRaii.PushId(HashCode.Combine("DescLink", venueId, paragraphHash, lineIndex, i));
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedBlue))
                    {
                        if (ImGui.Selectable(url, false, ImGuiSelectableFlags.DontClosePopups, ImGui.CalcTextSize(url)))
                        {
                            Util.OpenLink(url);
                        }
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                        ImGui.SetTooltip("Open link");
                    }

                    if (!string.IsNullOrEmpty(trailing))
                    {
                        ImGui.SameLine(0f, 0f);
                        ImGui.TextUnformatted(trailing);
                    }
                }
                else
                {
                    ImGui.TextUnformatted(rawUrl);
                }

                cursor = match.Index + match.Length;
                if (cursor < line.Length)
                {
                    ImGui.SameLine(0f, 0f);
                }
            }

            if (cursor < line.Length)
            {
                ImGui.TextUnformatted(line[cursor..]);
            }
        }
    }

    private static string NormalizeFancyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var hadSmallCaps = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value == 0xA9C1 || value == 0xA9C2)
            {
                continue;
            }

            if (SmallCapsMap.TryGetValue(value, out var smallCap))
            {
                builder.Append(smallCap);
                hadSmallCaps = true;
                continue;
            }

            if (SpecialMap.TryGetValue(value, out var mapped))
            {
                builder.Append(mapped);
                continue;
            }

            if (value is >= 0x1D400 and <= 0x1D419)
            {
                builder.Append((char)('A' + (value - 0x1D400)));
                continue;
            }

            if (value is >= 0x1D41A and <= 0x1D433)
            {
                builder.Append((char)('a' + (value - 0x1D41A)));
                continue;
            }

            if (value is >= 0x1D434 and <= 0x1D44D)
            {
                builder.Append((char)('A' + (value - 0x1D434)));
                continue;
            }

            if (value is >= 0x1D44E and <= 0x1D467)
            {
                builder.Append((char)('a' + (value - 0x1D44E)));
                continue;
            }

            if (value is >= 0x1D63C and <= 0x1D655)
            {
                builder.Append((char)('A' + (value - 0x1D63C)));
                continue;
            }

            if (value is >= 0x1D656 and <= 0x1D66F)
            {
                builder.Append((char)('a' + (value - 0x1D656)));
                continue;
            }

            if (value is >= 0x1D608 and <= 0x1D621)
            {
                builder.Append((char)('A' + (value - 0x1D608)));
                continue;
            }

            if (value is >= 0x1D622 and <= 0x1D63B)
            {
                builder.Append((char)('a' + (value - 0x1D622)));
                continue;
            }

            if (value is >= 0x1D5D4 and <= 0x1D5ED)
            {
                builder.Append((char)('A' + (value - 0x1D5D4)));
                continue;
            }

            if (value is >= 0x1D5EE and <= 0x1D607)
            {
                builder.Append((char)('a' + (value - 0x1D5EE)));
                continue;
            }

            builder.Append(rune.ToString());
        }

        var normalized = builder.ToString();
        if (hadSmallCaps)
        {
            normalized = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
        }

        return normalized;
    }

    private static string NormalizeDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = NormalizeFancyText(text);
        normalized = ReplaceProblemSymbols(normalized);
        normalized = normalized.Replace("\r\n", "\n").Trim();
        return normalized;
    }

    private static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = NormalizeDisplayText(text);
        normalized = HtmlTagRegex.Replace(normalized, " ");
        normalized = MarkdownLinkRegex.Replace(normalized, "$1");
        normalized = MarkdownStrongRegex.Replace(normalized, "$2");
        normalized = MarkdownEmRegex.Replace(normalized, "$2");
        normalized = normalized.Replace("`", string.Empty);
        normalized = MultiWhitespaceRegex.Replace(normalized, " ").Trim();
        return normalized;
    }

    private static string ReplaceProblemSymbols(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (ProblemSymbolMap.TryGetValue(rune.Value, out var replacement))
            {
                builder.Append(replacement);
                continue;
            }

            if (IsLikelyEmojiRune(rune.Value))
            {
                builder.Append('•');
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static bool IsLikelyEmojiRune(int value) =>
        value is >= 0x1F000 and <= 0x1FAFF;

    private static bool ShouldJoinDescriptionLines(string previous, string current)
    {
        if (previous.EndsWith(':'))
        {
            return false;
        }

        if (current.StartsWith("• ", StringComparison.Ordinal))
        {
            return false;
        }

        var last = previous[^1];
        return last is not '.' and not '!' and not '?' and not ';' and not ':';
    }

    private void EnsureSortedVenues(ImGuiTableSortSpecsPtr sortSpecs, IReadOnlyList<PreparedVenue> venues)
    {
        var signature = BuildSortSignature(sortSpecs);
        if (!_sortDirty && !sortSpecs.SpecsDirty && signature == _sortSignature && _sortedVenues.Count == venues.Count)
        {
            return;
        }

        _sortedVenues.Clear();
        _sortedVenues.AddRange(venues);

        if (_sortedVenues.Count > 1)
        {
            if (sortSpecs.SpecsCount == 0)
            {
                _sortedVenues.Sort(static (left, right) =>
                {
                    var compare = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
                    return compare != 0 ? compare : StringComparer.Ordinal.Compare(left.Id, right.Id);
                });
            }
            else
            {
                _sortedVenues.Sort((left, right) => ComparePreparedVenues(left, right, sortSpecs));
            }
        }

        _sortDirty = false;
        _sortSignature = signature;
        sortSpecs.SpecsDirty = false;
    }

    private static int BuildSortSignature(ImGuiTableSortSpecsPtr sortSpecs)
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + sortSpecs.SpecsCount;
            for (var i = 0; i < sortSpecs.SpecsCount; i++)
            {
                var spec = sortSpecs.Specs[i];
                hash = (hash * 31) + spec.ColumnIndex;
                hash = (hash * 31) + (int)spec.SortDirection;
            }

            return hash;
        }
    }

    private static int ComparePreparedVenues(PreparedVenue left, PreparedVenue right, ImGuiTableSortSpecsPtr sortSpecs)
    {
        for (var i = 0; i < sortSpecs.SpecsCount; i++)
        {
            var spec = sortSpecs.Specs[i];
            if (spec.SortDirection == ImGuiSortDirection.None)
            {
                continue;
            }

            var comparison = spec.ColumnIndex switch
            {
                0 => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName),
                1 => StringComparer.OrdinalIgnoreCase.Compare(left.LocationKey, right.LocationKey),
                2 => left.VenueTypeSortKey.CompareTo(right.VenueTypeSortKey),
                3 => left.StatusSortKey.CompareTo(right.StatusSortKey),
                _ => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName),
            };

            if (comparison == 0)
            {
                continue;
            }

            return spec.SortDirection == ImGuiSortDirection.Descending
                ? -comparison
                : comparison;
        }

        var fallback = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        return fallback != 0 ? fallback : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    private static DateTimeOffset GetStatusSortKey(DirectoryVenue venue)
    {
        var resolution = venue.Resolution;
        if (resolution == null)
        {
            return DateTimeOffset.MaxValue;
        }

        // For "Open until ..." sort by end time; otherwise sort by next start time.
        return resolution.IsNow ? resolution.End : resolution.Start;
    }

    private void RefreshFilteredVenuesIfNeeded()
    {
        if (!_filtersDirty)
        {
            return;
        }

        _filteredVenues.Clear();

        var search = NormalizeForSearch(_searchText.Trim());
        var tags = ParseTagFilter(_tagFilter);
        foreach (var venue in _preparedVenues)
        {
            if (MatchesFilters(venue, search, tags))
            {
                _filteredVenues.Add(venue);
            }
        }

        _filtersDirty = false;
        _sortDirty = true;
    }

    private bool MatchesFilters(PreparedVenue venue, string search, string[] tags)
    {
        if (search.Length > 0 && !ContainsNormalized(venue.SearchText, search))
        {
            return false;
        }

        if (tags.Length > 0 && !tags.All(tag => venue.TagSet.Contains(tag)))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_selectedRegion) &&
            !string.Equals(venue.Region, _selectedRegion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_selectedDataCenter) &&
            !string.Equals(venue.DataCenter, _selectedDataCenter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_selectedWorld) &&
            !string.Equals(venue.World, _selectedWorld, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_onlyOpen && !venue.IsOpen)
        {
            return false;
        }

        if (_favoritesOnly && !_favoriteVenueIds.Contains(venue.Id))
        {
            return false;
        }

        if (_visitedOnly && !_visitedVenueIds.Contains(venue.Id))
        {
            return false;
        }

        if (_sfwOnly && venue.IsNsfw)
        {
            return false;
        }

        if (_nsfwOnly && !venue.IsNsfw)
        {
            return false;
        }

        if (_sizeApartment && _sizeSmall && _sizeMedium && _sizeLarge)
        {
            return true;
        }

        if (venue.IsApartment)
        {
            return _sizeApartment;
        }

        return venue.PlotSize switch
        {
            HousingPlotSize.Small => _sizeSmall,
            HousingPlotSize.Medium => _sizeMedium,
            HousingPlotSize.Large => _sizeLarge,
            _ => false,
        };
    }

    private static string[] ParseTagFilter(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool ContainsNormalized(string source, string search)
    {
        if (source.Length == 0)
        {
            return false;
        }

        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(source, search, CompareOptions.IgnoreCase) >= 0;
    }

    private PreparedVenue[] BuildPreparedVenues(IEnumerable<DirectoryVenue> venues)
    {
        var preparedVenues = new List<PreparedVenue>();

        foreach (var venue in venues)
        {
            if (venue == null)
            {
                continue;
            }

            try
            {
                var prepared = CreatePreparedVenue(venue);
                preparedVenues.Add(prepared);
            }
            catch
            {
                // Ignore malformed venue payloads so a single bad API record does not break the whole window.
            }
        }

        return preparedVenues.ToArray();
    }

    private void ApplyPreparedVenues(IEnumerable<PreparedVenue> venues)
    {
        _preparedVenues.Clear();
        _preparedVenueById.Clear();

        foreach (var venue in venues)
        {
            _preparedVenues.Add(venue);
            _preparedVenueById[venue.Id] = venue;
        }

        _dataCenters.Clear();
        _dataCenters.AddRange(_preparedVenues
            .Select(v => v.DataCenter)
            .Where(dc => !string.IsNullOrWhiteSpace(dc))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(dc => dc, StringComparer.OrdinalIgnoreCase)!);

        _regions.Clear();
        _regions.AddRange(Regions);

        UpdateWorldOptions();
        _selectedRouteIndices.Clear();
        _selectedVenueId = _preparedVenues.FirstOrDefault()?.Id;
        _filtersDirty = true;
        _sortDirty = true;
        _sortSignature = int.MinValue;
    }

    private PreparedVenue CreatePreparedVenue(DirectoryVenue venue)
    {
        var venueId = string.IsNullOrWhiteSpace(venue.Id)
            ? BuildFallbackVenueId(venue)
            : venue.Id;
        var displayName = string.IsNullOrWhiteSpace(venue.Name) ? "Unnamed venue" : NormalizeDisplayText(venue.Name);
        var isApartment = IsApartmentLocation(venue.Location);
        HousingPlotSize? plotSize = null;
        if (!isApartment && _housingPlotSizeResolver.TryGetSize(venue.Location, out var resolvedSize))
        {
            plotSize = resolvedSize;
        }

        var sanitizedDescription = PrepareSanitizedDescription(venue.Description);
        return new PreparedVenue
        {
            Venue = venue,
            Id = venueId,
            DisplayName = displayName,
            SearchText = BuildSearchText(venue, displayName),
            TagSet = CreateTagSet(venue.Tags),
            Region = ResolveRegion(venue.Location?.DataCenter),
            DataCenter = venue.Location?.DataCenter,
            World = venue.Location?.World,
            IsOpen = venue.Resolution?.IsNow == true,
            IsNsfw = IsVenueNsfw(venue),
            HasAdultServices = HasAdultServicesTag(venue),
            IsApartment = isApartment,
            PlotSize = plotSize,
            VenueTypeLabel = GetVenueTypeLabel(isApartment, plotSize),
            VenueTypeSortKey = GetVenueTypeSortKey(isApartment, plotSize),
            StatusText = FormatStatusLine(venue),
            StatusSortKey = GetStatusSortKey(venue),
            TableAddress = FormatAddressForTable(venue),
            LocationKey = GetLocationKey(venue),
            RouteOptions = BuildRouteOptions(venue),
            SanitizedDescription = sanitizedDescription,
            SanitizedDescriptionHash = sanitizedDescription?.GetHashCode(StringComparison.Ordinal) ?? 0,
            SortedSchedule = SortSchedule(venue.Schedule),
        };
    }

    private static string BuildFallbackVenueId(DirectoryVenue venue)
    {
        var key = string.Join("|",
            venue.Name ?? string.Empty,
            venue.Location?.DataCenter ?? string.Empty,
            venue.Location?.World ?? string.Empty,
            venue.Location?.District ?? string.Empty,
            venue.Location?.Ward.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            venue.Location?.Plot.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            venue.Location?.Apartment.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            venue.Location?.Room.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        return $"fallback:{key.GetHashCode(StringComparison.Ordinal)}";
    }

    private static HashSet<string> CreateTagSet(IEnumerable<string>? tags)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tags == null)
        {
            return result;
        }

        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    private static string BuildSearchText(DirectoryVenue venue, string displayName)
    {
        var builder = new StringBuilder(displayName.Length + 64);
        if (displayName.Length > 0)
        {
            builder.Append(displayName);
        }

        if (venue.Description is { Count: > 0 })
        {
            foreach (var line in venue.Description)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                builder.Append('\n');
                builder.Append(line);
            }
        }

        if (venue.Tags is { Count: > 0 })
        {
            var hasTags = false;
            foreach (var tag in venue.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                if (!hasTags)
                {
                    builder.Append('\n');
                    hasTags = true;
                }
                else
                {
                    builder.Append(' ');
                }

                builder.Append(tag);
            }
        }

        return NormalizeForSearch(builder.ToString());
    }

    private static string? PrepareSanitizedDescription(List<string>? description)
    {
        if (description is not { Count: > 0 })
        {
            return null;
        }

        var joined = string.Join("\n", description.Where(para => !string.IsNullOrWhiteSpace(para)));
        if (joined.Length == 0)
        {
            return null;
        }

        var sanitized = SanitizeDescription(joined);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static DirectorySchedule[] SortSchedule(List<DirectorySchedule>? schedule)
    {
        if (schedule is not { Count: > 0 })
        {
            return Array.Empty<DirectorySchedule>();
        }

        return schedule
            .OrderBy(s => s.Day)
            .ThenBy(s => s.Start?.Hour ?? 0)
            .ThenBy(s => s.Start?.Minute ?? 0)
            .ToArray();
    }

    private void EnsurePreferenceCollectionsInitialized()
    {
        _configuration.FavoriteVenueIds ??= new List<string>();
        _configuration.VisitedVenueIds ??= new List<string>();
    }

    private void InitializePreferenceLookups()
    {
        _favoriteVenueIds.Clear();
        foreach (var id in _configuration.FavoriteVenueIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                _favoriteVenueIds.Add(id);
            }
        }

        _visitedVenueIds.Clear();
        foreach (var id in _configuration.VisitedVenueIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                _visitedVenueIds.Add(id);
            }
        }
    }

    private void SetPreferredVenue(List<string>? venueIds, HashSet<string> lookup, string? venueId, bool enabled)
    {
        if (venueIds == null || string.IsNullOrWhiteSpace(venueId))
        {
            return;
        }

        var existingIndex = venueIds.FindIndex(id => string.Equals(id, venueId, StringComparison.Ordinal));
        if (enabled && existingIndex < 0)
        {
            venueIds.Add(venueId);
            lookup.Add(venueId);
            _configuration.Save(DalamudServices.PluginInterface);
            MarkFiltersDirty();
            return;
        }

        if (!enabled && existingIndex >= 0)
        {
            venueIds.RemoveAt(existingIndex);
            lookup.Remove(venueId);
            _configuration.Save(DalamudServices.PluginInterface);
            MarkFiltersDirty();
        }
    }

    private void EnsureSelection(IReadOnlyList<PreparedVenue> venues)
    {
        if (venues.Count == 0)
        {
            _selectedVenueId = null;
            return;
        }

        var hasSelection = false;
        if (_selectedVenueId != null)
        {
            for (var i = 0; i < venues.Count; i++)
            {
                if (string.Equals(venues[i].Id, _selectedVenueId, StringComparison.Ordinal))
                {
                    hasSelection = true;
                    break;
                }
            }
        }

        if (!hasSelection)
        {
            _selectedVenueId = venues[0].Id;
        }
    }

    private void SetRegion(string? region)
    {
        _selectedRegion = region;
        _selectedDataCenter = null;
        _selectedWorld = null;
        UpdateWorldOptions();
        MarkFiltersDirty();
    }

    private void SetDataCenter(string? dataCenter)
    {
        _selectedDataCenter = dataCenter;
        _selectedWorld = null;
        UpdateWorldOptions();
        MarkFiltersDirty();
    }

    private void UpdateWorldOptions()
    {
        _worlds.Clear();
        if (_preparedVenues.Count == 0)
        {
            return;
        }

        var query = _preparedVenues.AsEnumerable();
        if (!string.IsNullOrEmpty(_selectedRegion))
        {
            query = query.Where(v =>
                string.Equals(v.Region, _selectedRegion, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_selectedDataCenter))
        {
            query = query.Where(v =>
                string.Equals(v.DataCenter, _selectedDataCenter, StringComparison.OrdinalIgnoreCase));
        }

        _worlds.AddRange(query
            .Select(v => v.World)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)!);
    }

    private void MarkFiltersDirty()
    {
        _filtersDirty = true;
        _sortDirty = true;
    }

    private IEnumerable<string> GetRegionDataCenters()
    {
        if (_selectedRegion == null)
        {
            return _dataCenters;
        }

        return _dataCenters
            .Where(dc => string.Equals(ResolveRegion(dc), _selectedRegion, StringComparison.OrdinalIgnoreCase))
            .OrderBy(dc => dc, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveRegion(string? dataCenter)
    {
        if (string.IsNullOrWhiteSpace(dataCenter))
        {
            return null;
        }

        return dataCenter.Trim() switch
        {
            "Aether" => "North America",
            "Crystal" => "North America",
            "Dynamis" => "North America",
            "Primal" => "North America",
            "Chaos" => "Europe",
            "Light" => "Europe",
            "Materia" => "Oceania",
            "Elemental" => "Japan",
            "Gaia" => "Japan",
            "Mana" => "Japan",
            "Meteor" => "Japan",
            _ => null
        };
    }

    private string FormatStatusLine(DirectoryVenue venue)
    {
        if (venue.Resolution == null)
        {
            return "No scheduled openings";
        }

        var startLocal = venue.Resolution.Start.ToLocalTime();
        var endLocal = venue.Resolution.End.ToLocalTime();
        var start = $"{startLocal.ToString("ddd", CultureInfo.InvariantCulture)} {FormatShortTime(startLocal)}";
        var end = FormatShortTime(endLocal);
        return venue.Resolution.IsNow
            ? $"Open until {end}"
            : $"Opens {start}";
    }

    private static string FormatAddress(DirectoryLocation? location)
    {
        if (location == null)
        {
            return "Location unknown";
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            return location.Override;
        }

        if (location.Apartment > 0)
        {
            var subdivision = location.Subdivision ? ", Subdivision" : string.Empty;
            return $"{location.DataCenter}, {location.World}, {location.District}, Ward {location.Ward}{subdivision}, Apartment {location.Apartment}";
        }

        return $"{location.DataCenter}, {location.World}, {location.District}, Ward {location.Ward}, Plot {location.Plot}";
    }

    private static string FormatAddressForTable(DirectoryVenue venue)
    {
        var location = venue.Location;
        if (location == null)
        {
            return "Location unknown";
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            var routes = ParseOverrideRouteOptions(location.Override)
                .Select(r => r.DisplayText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (routes.Count > 1)
            {
                return string.Join("\n", routes);
            }
        }

        return FormatAddress(location);
    }

    private static string FormatAddressDetailed(DirectoryLocation? location)
    {
        if (location == null)
        {
            return "Location unknown";
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            return location.Override;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.DataCenter))
        {
            parts.Add(location.DataCenter);
        }

        if (!string.IsNullOrWhiteSpace(location.World))
        {
            parts.Add(location.World);
        }

        if (!string.IsNullOrWhiteSpace(location.District))
        {
            parts.Add(location.District);
        }

        parts.Add($"Ward {location.Ward}" + (location.Subdivision ? " (Subdivision)" : string.Empty));
        if (location.Apartment > 0)
        {
            parts.Add($"Apartment {location.Apartment}");
        }
        else
        {
            parts.Add($"Plot {location.Plot}");
        }

        if (location.Room > 0)
        {
            parts.Add($"Room {location.Room}");
        }

        if (!string.IsNullOrWhiteSpace(location.Shard))
        {
            parts.Add($"Shard {location.Shard}");
        }

        return string.Join(", ", parts);
    }

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

    private static List<VenueRouteOption> BuildRouteOptions(DirectoryVenue venue)
    {
        var options = new List<VenueRouteOption>();
        var location = venue.Location;
        if (location == null)
        {
            options.Add(new VenueRouteOption("Location unknown", "Location unknown", null));
            return options;
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            options.AddRange(ParseOverrideRouteOptions(location.Override));
            if (options.Count > 0)
            {
                return options;
            }
        }

        var detailed = FormatAddressDetailed(location);
        var lifestreamArgs = FormatLifestreamArguments(location);
        options.Add(new VenueRouteOption(detailed, detailed, string.IsNullOrWhiteSpace(lifestreamArgs) ? null : lifestreamArgs));
        return options;
    }

    private static IEnumerable<VenueRouteOption> ParseOverrideRouteOptions(string overrideText)
    {
        var text = Regex.Replace(overrideText, @"\s+", " ").Trim();
        if (text.Length == 0)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentLabel = null;
        foreach (Match match in OverrideRouteRegex.Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(label))
            {
                currentLabel = label;
            }

            var address = NormalizeRouteAddress(match.Groups["address"].Value);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            var display = string.IsNullOrWhiteSpace(currentLabel)
                ? address
                : $"{currentLabel}: {address}";
            if (!seen.Add(display))
            {
                continue;
            }

            var lifestreamArgs = FormatLifestreamArguments(address);
            yield return new VenueRouteOption(display, address, string.IsNullOrWhiteSpace(lifestreamArgs) ? null : lifestreamArgs);
        }
    }

    private static string NormalizeRouteAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ").Trim().TrimEnd('.');
    }

    private static string FormatLifestreamArguments(DirectoryLocation? location)
    {
        if (location == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.DataCenter))
        {
            parts.Add(location.DataCenter);
        }

        if (!string.IsNullOrWhiteSpace(location.World))
        {
            parts.Add(location.World);
        }

        if (!string.IsNullOrWhiteSpace(location.District))
        {
            var cleaned = location.District.Replace("(Subdivision)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                parts.Add(cleaned);
            }
        }

        parts.Add($"Ward {location.Ward}");
        if (location.Apartment > 0)
        {
            parts.Add($"Apartment {location.Apartment}");
            if (location.Subdivision)
            {
                parts.Add("Subdivision");
            }
        }
        else
        {
            parts.Add($"Plot {location.Plot}");

            if (location.Room > 0)
            {
                parts.Add($"Room {location.Room}");
            }
        }

        return string.Join(", ", parts);
    }

    private static string? FormatLifestreamArguments(string routeText)
    {
        if (string.IsNullOrWhiteSpace(routeText))
        {
            return null;
        }

        var tokens = routeText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().TrimEnd('.'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var wardToken = tokens.FirstOrDefault(t => t.StartsWith("Ward ", StringComparison.OrdinalIgnoreCase));
        if (wardToken == null)
        {
            return null;
        }

        var wardIndex = tokens.FindIndex(t => string.Equals(t, wardToken, StringComparison.OrdinalIgnoreCase));
        if (wardIndex < 2)
        {
            return null;
        }

        var plotToken = tokens.FirstOrDefault(t => t.StartsWith("Plot ", StringComparison.OrdinalIgnoreCase));
        var apartmentToken = tokens.FirstOrDefault(t =>
            t.StartsWith("Apartment ", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Apt ", StringComparison.OrdinalIgnoreCase));
        if (plotToken == null && apartmentToken == null)
        {
            return null;
        }

        var head = tokens.Take(wardIndex).ToList();
        var parts = new List<string>(head.Count + 4);
        parts.AddRange(head);
        parts.Add(NormalizeWardOrPlotToken(wardToken));
        if (apartmentToken != null)
        {
            parts.Add(NormalizeWardOrPlotToken(apartmentToken));
            if (wardToken.Contains("sub", StringComparison.OrdinalIgnoreCase) ||
                tokens.Any(t => t.Contains("subdivision", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t, "sub", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t, "s", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add("Subdivision");
            }
        }
        else
        {
            parts.Add(NormalizeWardOrPlotToken(plotToken!));
        }

        var room = tokens.FirstOrDefault(t => t.StartsWith("Room ", StringComparison.OrdinalIgnoreCase));
        if (room != null)
        {
            parts.Add(room);
        }

        return string.Join(", ", parts);
    }

    private static string NormalizeWardOrPlotToken(string token)
    {
        var cleaned = Regex.Replace(token, @"\s+", " ").Trim();
        if (cleaned.StartsWith("Ward", StringComparison.OrdinalIgnoreCase))
        {
            return "Ward " + cleaned.Substring(4).Trim();
        }

        if (cleaned.StartsWith("Apartment", StringComparison.OrdinalIgnoreCase))
        {
            return "Apartment " + cleaned.Substring("Apartment".Length).Trim();
        }

        if (cleaned.StartsWith("Apt", StringComparison.OrdinalIgnoreCase))
        {
            return "Apartment " + cleaned.Substring(3).Trim();
        }

        if (cleaned.StartsWith("Plot", StringComparison.OrdinalIgnoreCase))
        {
            return "Plot " + cleaned.Substring(4).Trim();
        }

        return cleaned;
    }

    private static bool IsApartmentLocation(DirectoryLocation? location) =>
        location is { Apartment: > 0 };

    private static bool IsVenueNsfw(DirectoryVenue venue)
    {
        return IsOpenlyNsfwVenue(venue);
    }

    private static bool IsOpenlyNsfwVenue(DirectoryVenue venue) => venue.Sfw == false;

    private static bool HasAdultServicesTag(DirectoryVenue? venue)
    {
        if (venue?.Tags == null)
        {
            return false;
        }

        foreach (var tag in venue.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag) &&
                tag.Contains("courtesan", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetVenueWarningText(bool openlyNsfw, bool hasAdultServices)
    {
        if (hasAdultServices && openlyNsfw)
        {
            return "This venue has indicated they are openly NSFW and offer adult services. You must not visit this venue if you are under 18 years of age or the legal age of consent in your country, and by visiting you declare you are not. Be prepared to verify your age.";
        }

        if (hasAdultServices && !openlyNsfw)
        {
            return "This venue has indicated they offer adult services. You must not partake in these services if you are under 18 years of age or the legal age of consent in your country, and by partaking in these services you declare you are not. Be prepared to verify your age.";
        }

        if (!hasAdultServices && openlyNsfw)
        {
            return "This venue has indicated they are openly NSFW. You must not visit this venue if you are under 18 years of age or the legal age of consent in your country, and by visiting you declare you are not. Be prepared to verify your age.";
        }

        return null;
    }

    private static string GetVenueTypeLabel(bool isApartment, HousingPlotSize? size)
    {
        if (isApartment)
        {
            return "A";
        }

        return size switch
        {
            HousingPlotSize.Small => "S",
            HousingPlotSize.Medium => "M",
            HousingPlotSize.Large => "L",
            _ => "?",
        };
    }

    private static int GetVenueTypeSortKey(bool isApartment, HousingPlotSize? size)
    {
        if (isApartment)
        {
            return 0;
        }

        return size switch
        {
            HousingPlotSize.Small => 1,
            HousingPlotSize.Medium => 2,
            HousingPlotSize.Large => 3,
            _ => 4,
        };
    }

    private static void DrawSizeBadge(string text, string idSuffix)
    {
        var chipSide = Scale(18f);
        var chipSize = new Vector2(chipSide, chipSide);
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton($"##size_badge_{idSuffix}", chipSize);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var bg = ImGui.GetColorU32(new Vector4(0.23f, 0.31f, 0.45f, 1f));
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        drawList.AddRectFilled(min, max, bg, Scale(6f));
        drawList.AddRect(min, max, border, Scale(6f));

        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(
            min.X + (chipSize.X - textSize.X) * 0.5f,
            min.Y + (chipSize.Y - textSize.Y) * 0.5f);
        drawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), text);
    }

    private static string GetLocationKey(DirectoryVenue venue) =>
        $"{venue.Location?.DataCenter}-{venue.Location?.World}-{venue.Location?.District}-{venue.Location?.Ward}-{venue.Location?.Plot}";

    private static string FormatScheduleLabel(DirectorySchedule schedule, DayOfWeek? localDay)
    {
        var interval = FormatInterval(schedule.Interval);
        if (string.Equals(interval, "Daily", StringComparison.OrdinalIgnoreCase))
        {
            return "Daily";
        }

        var dayLabel = localDay.HasValue ? PluralizeDay(localDay.Value) : PluralizeDay(schedule.Day);
        return $"{interval} on {dayLabel}";
    }

    private static (string Start, string End, bool IsToday, DayOfWeek? LocalDay) FormatScheduleTimes(
        DirectorySchedule schedule,
        DayOfWeek currentDay)
    {
        if (TryFormatLocalTime(schedule.Start, schedule.Day, out var start, out var startLocal) &&
            TryFormatLocalTime(schedule.End, schedule.Day, out var end, out _))
        {
            return (start, end, startLocal.DayOfWeek == currentDay, startLocal.DayOfWeek);
        }

        return (FormatTime(schedule.Start), FormatTime(schedule.End),
            schedule.Day.ToString().Equals(currentDay.ToString(), StringComparison.OrdinalIgnoreCase), null);
    }

    private static string FormatShortTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);

    private static bool TryFormatLocalTime(DirectoryTime? time, DirectoryDay day, out string formatted, out DateTime localTime)
    {
        formatted = string.Empty;
        localTime = DateTime.MinValue;
        if (time == null)
        {
            return false;
        }

        if (!TryGetTimeZoneInfo(time.TimeZone, out var sourceTimeZone))
        {
            return false;
        }

        if (!TryParseDayOfWeek(day, out var targetDay))
        {
            targetDay = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, sourceTimeZone).DayOfWeek;
        }

        var sourceNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, sourceTimeZone);
        var sourceDate = GetNextDateForDay(sourceNow, targetDay);
        var sourceTime = sourceDate.AddHours(time.Hour).AddMinutes(time.Minute);
        if (time.NextDay)
        {
            sourceTime = sourceTime.AddDays(1);
        }

        var unspecified = DateTime.SpecifyKind(sourceTime, DateTimeKind.Unspecified);
        localTime = TimeZoneInfo.ConvertTime(unspecified, sourceTimeZone, TimeZoneInfo.Local);
        formatted = localTime.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);
        return true;
    }

    private static DateTime GetNextDateForDay(DateTime reference, DayOfWeek target)
    {
        var diff = ((int)target - (int)reference.DayOfWeek + 7) % 7;
        return reference.Date.AddDays(diff);
    }

    private static bool TryParseDayOfWeek(DirectoryDay day, out DayOfWeek dayOfWeek) =>
        Enum.TryParse(day.ToString(), true, out dayOfWeek);

    private static string PluralizeDay(DirectoryDay day) => PluralizeDay(day.ToString());

    private static string PluralizeDay(DayOfWeek day) => PluralizeDay(day.ToString());

    private static string PluralizeDay(string name)
    {
        if (name.EndsWith("day", StringComparison.OrdinalIgnoreCase))
        {
            return name + "s";
        }

        return name;
    }

    private static string FormatTime(DirectoryTime? time)
    {
        if (time == null)
        {
            return "--";
        }

        var suffix = time.NextDay ? " (+1)" : string.Empty;
        var abbreviation = GetTimeZoneAbbreviation(time.TimeZone, DateTime.UtcNow);
        return $"{time.Hour:00}:{time.Minute:00} {abbreviation}{suffix}";
    }

    private static bool TryGetTimeZoneInfo(string timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        var trimmed = timeZoneId.Trim();
        lock (TimeZoneInfoCacheGate)
        {
            if (TimeZoneInfoCache.TryGetValue(trimmed, out var cached))
            {
                if (cached != null)
                {
                    timeZone = cached;
                    return true;
                }

                return false;
            }
        }

        try
        {
            var windowsId = TZConvert.IanaToWindows(trimmed);
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            lock (TimeZoneInfoCacheGate)
            {
                TimeZoneInfoCache[trimmed] = timeZone;
            }
            return true;
        }
        catch
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(trimmed);
                lock (TimeZoneInfoCacheGate)
                {
                    TimeZoneInfoCache[trimmed] = timeZone;
                }
                return true;
            }
            catch
            {
                lock (TimeZoneInfoCacheGate)
                {
                    TimeZoneInfoCache[trimmed] = null;
                }
                return false;
            }
        }
    }

    private static string GetTimeZoneAbbreviation(string timeZoneId, DateTime referenceUtc)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return "UTC";
        }

        var trimmed = timeZoneId.Trim();
        if (string.Equals(trimmed, "UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        if (string.Equals(trimmed, "GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Etc/GMT", StringComparison.OrdinalIgnoreCase))
        {
            return "GMT";
        }

        if (!TryGetTimeZoneInfo(trimmed, out var timeZone))
        {
            return trimmed;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(referenceUtc, timeZone);
        var isDst = timeZone.IsDaylightSavingTime(local);
        var name = isDst ? timeZone.DaylightName : timeZone.StandardName;
        if (TimeZoneAbbreviationMap.TryGetValue(name, out var abbreviation) &&
            !string.IsNullOrWhiteSpace(abbreviation))
        {
            return abbreviation;
        }

        return AbbreviateName(name);
    }

    private static string AbbreviateName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var letters = new List<char>(parts.Length);
        foreach (var part in parts)
        {
            if (string.Equals(part, "Standard", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Daylight", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Summer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Time", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            letters.Add(char.ToUpperInvariant(part[0]));
        }

        return letters.Count == 0 ? name : new string(letters.ToArray());
    }

    private static string FormatInterval(DirectoryInterval? interval)
    {
        if (interval == null)
        {
            return "Unknown";
        }

        var intervalType = interval.IntervalType switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var n) => n.ToString(CultureInfo.InvariantCulture),
            JsonElement { ValueKind: JsonValueKind.String } text => text.GetString(),
            _ => interval.IntervalType.ToString()
        };

        var argument = interval.IntervalArgument switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var n) => n,
            JsonElement { ValueKind: JsonValueKind.String } text when int.TryParse(text.GetString(), out var n) => n,
            _ => int.TryParse(interval.IntervalArgument.ToString(), out var parsed) ? parsed : 0
        };

        return intervalType switch
        {
            "EveryXWeeks" or "0" => argument <= 1 ? "Weekly" : $"Every {argument} weeks",
            "EveryXDays" or "1" => Math.Abs(argument) <= 1 ? "Daily" : $"Every {Math.Abs(argument)} days",
            "EveryXMonths" => argument <= 1 ? "Monthly" : $"Every {argument} months",
            "EveryXHours" => argument <= 1 ? "Hourly" : $"Every {argument} hours",
            "EveryXMinutes" => argument <= 1 ? "Every minute" : $"Every {argument} minutes",
            "Once" => "One-time",
            null => "Unknown",
            _ => argument > 0 ? $"{intervalType} ({argument})" : intervalType
        };
    }

    private static string FormatRelativeTime(DateTimeOffset value)
    {
        var span = DateTimeOffset.UtcNow - value;
        if (span.TotalSeconds < 45) return "just now";
        if (span.TotalMinutes < 1.5) return "a minute ago";
        if (span.TotalHours < 1) return $"{Math.Round(span.TotalMinutes)} minutes ago";
        if (span.TotalHours < 1.5) return "an hour ago";
        if (span.TotalHours < 24) return $"{Math.Round(span.TotalHours)} hours ago";
        if (span.TotalDays < 2) return "yesterday";
        return $"{Math.Round(span.TotalDays)} days ago";
    }

    private static float Scale(float value) => value * ImGuiHelpers.GlobalScale;
}
