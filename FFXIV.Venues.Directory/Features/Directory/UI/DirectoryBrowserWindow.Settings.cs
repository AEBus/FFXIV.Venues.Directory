using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIV.Venues.Directory.Features.Directory.Text;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.Net;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// The settings, in a window of their own that opens without the directory: a page per topic, picked on the left.
internal sealed partial class DirectoryBrowserWindow
{
    private enum SettingsPage
    {
        Appearance,
        Images,
        Events,
        Notifications,
        About,
    }

    private const string ProjectUrl = "https://github.com/AEBus/FFXIV.Venues.Directory";
    private const float SettingsNavWidth = 180f;

    private static readonly Vector2 SettingsDefaultSize = new(700f, 600f);
    private static readonly Vector2 SettingsMinSize = new(560f, 420f);

    private static readonly string[] SettingsPageLabels = ["Appearance", "Images", "Events", "Notifications", "About"];
    private static readonly FontAwesomeIcon[] SettingsPageIcons = [FontAwesomeIcon.Palette, FontAwesomeIcon.Image, FontAwesomeIcon.CalendarAlt, FontAwesomeIcon.Bell, FontAwesomeIcon.InfoCircle];

    private static readonly string[] SettingsPageSubtitles =
    [
        "Colors, size and how times are written.",
        "Banners and pictures in descriptions.",
        "Where the Events tab gets its events from.",
        "Dalamud notifications, also while the directory is closed. Click one to show it.",
        "The plugin and where its data comes from.",
    ];

    private static readonly string[] ClockLabels = ["Auto", "24-hour", "12-hour"];
    private static readonly FontAwesomeIcon[] ClockIcons = [FontAwesomeIcon.Desktop, FontAwesomeIcon.Clock, FontAwesomeIcon.Clock];
    private static readonly string?[] ClockTooltips = ["As Windows shows the time", null, null];

    private static readonly string[] LookLabels = ["Directory theme", "Dalamud theme"];
    private static readonly FontAwesomeIcon[] LookIcons = [FontAwesomeIcon.Palette, FontAwesomeIcon.Cog];

    private static readonly string[] SizeLabels = ["90%", "100%", "115%", "130%"];

    private SettingsPage _settingsPage;

    public SettingsWindow Settings { get; }

    // Opens the settings, or brings them to the front when they are open already.
    public void OpenSettings()
    {
        Settings.IsOpen = true;
        Settings.BringToFront();
    }

    // The fonts are rebuilt at the new size at the start of the next frame (PluginUiFont), and everything measured with the old ones is measured again when they arrive.
    private void SetInterfaceScale(float factor)
    {
        UiScale.SetFactor(factor);
        _configuration.InterfaceScale = UiScale.Factor;
        _configuration.Save(DalamudServices.PluginInterface);
    }

    private void SaveSettings() => _configuration.Save(DalamudServices.PluginInterface);

    private void DrawSettings()
    {
        using (var nav = ImRaii.Child("SettingsNav"u8, new Vector2(Scale(SettingsNavWidth), 0f), true))
        {
            if (nav)
            {
                for (var i = 0; i < SettingsPageLabels.Length; i++)
                {
                    if (DrawSettingsNavItem(i, (int)_settingsPage == i))
                    {
                        _settingsPage = (SettingsPage)i;
                    }
                }
            }
        }

        ImGui.SameLine(0f, UiStyle.InlineSpacing);
        using var page = ImRaii.Child("SettingsPage"u8, Vector2.Zero, true);
        if (!page)
        {
            return;
        }

        DrawVerticalRhythm(0.25f);
        DrawDisplayTitleText(SettingsPageLabels[(int)_settingsPage]);
        DrawTextWrapped(SettingsPageSubtitles[(int)_settingsPage], UiStyle.BodyMutedText);
        DrawVerticalRhythm();
        switch (_settingsPage)
        {
            case SettingsPage.Appearance:
                DrawAppearanceSettings();
                break;
            case SettingsPage.Images:
                DrawImageSettings();
                break;
            case SettingsPage.Events:
                DrawEventSettings();
                break;
            case SettingsPage.Notifications:
                DrawNotificationSettings();
                break;
            case SettingsPage.About:
                DrawAboutPage();
                break;
        }
    }

    private void DrawAppearanceSettings()
    {
        DrawSection("ColorsCard", "Colors", () =>
        {
            var look = _configuration.UseDalamudTheme ? 1 : 0;
            if (DrawSegmented("ColorsSetting", LookLabels, LookIcons, ref look))
            {
                _configuration.UseDalamudTheme = look == 1;
                SaveSettings();
            }

            DrawVerticalRhythm(0.25f);
            DrawTextWrapped(
                _configuration.UseDalamudTheme
                    ? "Colors and spacing follow your Dalamud theme."
                    : "The directory's own colors and spacing, whatever theme Dalamud uses.",
                UiStyle.BodyMutedText);
        });

        DrawSection("SizeCard", "Interface size", () =>
        {
            var sizeIndex = Array.IndexOf(UiScale.Presets, UiScale.Factor);
            if (DrawSegmented("InterfaceSize", SizeLabels, null, ref sizeIndex))
            {
                SetInterfaceScale(UiScale.Presets[sizeIndex]);
            }

            DrawVerticalRhythm(0.25f);
            DrawTextWrapped("On top of Dalamud's own UI scale: 100% is the size Dalamud draws at.", UiStyle.BodyMutedText);
        });

        DrawSection("TimeCard", "Time format", () =>
        {
            var clock = (int)(_configuration.Clock ?? ClockFormat.System);
            if (DrawSegmented("ClockFormat", ClockLabels, ClockIcons, ref clock, ClockTooltips))
            {
                SetClockFormat((ClockFormat)clock);
            }

            DrawVerticalRhythm(0.25f);
            var sample = Use12HourClock ? "8:30 PM" : "20:30";
            DrawTextWrapped(_configuration.Clock == ClockFormat.System ? $"Like Windows: times look like {sample}." : $"Times look like {sample}.", UiStyle.BodyMutedText);
        });
    }

    private void DrawImageSettings()
    {
        DrawSection("ImagesCard", null, null, () =>
        {
            var loadImages = _configuration.LoadDescriptionImages;
            if (DrawSettingSwitch("DescriptionImages", "Images in descriptions", "Pictures in descriptions load from the hosts' own sites. Turned off, each one is a link to open in the browser.", ref loadImages))
            {
                _configuration.LoadDescriptionImages = loadImages;
                SaveSettings();
            }

            DrawVerticalRhythm(0.75f);
            var preview = _configuration.PreviewImagesOnHover;
            if (DrawSettingSwitch("PreviewImages", "Full image on hover", "Hovering a banner or a picture shows it whole. A click opens it in the browser either way.", ref preview))
            {
                _configuration.PreviewImagesOnHover = preview;
                SaveSettings();
            }
        });
    }

    private void DrawEventSettings()
    {
        DrawSection("EventSourcesCard", "Sources", () =>
        {
            var showEvents = _configuration.ShowPartakeEvents;
            if (DrawSettingSwitch("PartakeEvents", "Events from Partake", "The events hosts post on partake.gg. Turned off, the plugin sends nothing to Partake.", ref showEvents))
            {
                _configuration.ShowPartakeEvents = showEvents;
                SaveSettings();
                _filteredEventsDirty = true;
            }

            DrawVerticalRhythm(0.75f);
            var showAds = _configuration.ShowPartyFinderAds;
            if (DrawSettingSwitch("PartyFinderAds", "Venue ads from the Party Finder", "The ads venues post in the in-game Party Finder, as xivpf.com collects them, for as long as they are up. Turned off, the plugin sends nothing to xivpf.com.", ref showAds))
            {
                _configuration.ShowPartyFinderAds = showAds;
                SaveSettings();
                _filteredEventsDirty = true;
            }
        });

        if (!EventsEnabled)
        {
            DrawTextWrapped("With both turned off, the Events tab is hidden.", UiStyle.BodyMutedText);
        }
    }

    private void DrawNotificationSettings()
    {
        DrawSection("VenueNotificationsCard", "Venues", () =>
        {
            var notifyOpens = _configuration.NotifyFavoriteOpens;
            if (DrawSettingSwitch("FavoriteOpens", "When a favorite venue opens", null, ref notifyOpens))
            {
                _configuration.NotifyFavoriteOpens = notifyOpens;
                SaveSettings();
            }
        });

        DrawSection("EventNotificationsCard", "Events", () =>
        {
            var partake = _configuration.ShowPartakeEvents;
            var notifyEvents = _configuration.NotifyFavoriteEvents;
            if (DrawSettingSwitch("FavoriteEvents", "When an event at a favorite venue starts", null, ref notifyEvents, partake))
            {
                _configuration.NotifyFavoriteEvents = notifyEvents;
                SaveSettings();
            }

            DrawVerticalRhythm(0.5f);
            var notifyGoing = _configuration.NotifyGoingEvents;
            if (DrawSettingSwitch("GoingEvents", "15 minutes before an event I'm going to", null, ref notifyGoing, partake))
            {
                _configuration.NotifyGoingEvents = notifyGoing;
                SaveSettings();
            }

            if (!partake)
            {
                DrawVerticalRhythm(0.5f);
                DrawTextWrapped("These follow the events on Partake, which are turned off on the Events page.", UiStyle.BodyMutedText);
            }
        });
    }

    private void DrawAboutPage()
    {
        DrawSection("AboutCard", "FFXIV Venues Directory", () =>
        {
            var version = typeof(DirectoryBrowserWindow).Assembly.GetName().Version?.ToString() ?? "?";
            DrawTextWrapped($"Version {version}", UiStyle.BodyMutedText);
            DrawVerticalRhythm(0.5f);
            if (DrawActionButton(FontAwesomeIcon.Code, "Source code", UiButtonTone.Secondary))
            {
                WebLink.Open(ProjectUrl);
            }

            ImGui.SameLine(0f, UiStyle.InlineSpacing);
            if (DrawActionButton(FontAwesomeIcon.Bug, "Report a problem", UiButtonTone.Secondary))
            {
                WebLink.Open(ProjectUrl + "/issues");
            }
        });

        DrawSection("DataSourcesCard", "Where the data comes from", () =>
        {
            DrawDataSource("FFXIV Venues", "https://ffxivvenues.com", "The venues: addresses, schedules, descriptions and banners.");
            DrawDataSource("Partake", "https://www.partake.gg", "Events, while they are turned on.");
            DrawDataSource("xivpf.com", "https://xivpf.com", "Party Finder ads, while they are turned on.");
            DrawVerticalRhythm(0.25f);
            DrawTextWrapped("Pictures in descriptions load from the sites they are on. What the plugin loads stays in memory; only your settings, favorites and marks are saved.", UiStyle.BodyMutedText);
        });

        DrawSection("ChangelogCard", "Changelog", () =>
        {
            _changelogView ??= new RichTextView(_remoteImages, () => false, () => false);
            _changelogView.Draw(GetChangelog(), RichTextVersion, FormatRichTime, DescribeRichTime, RichTextColors);
        });
    }

    private RichTextView? _changelogView;
    private RichDocument? _changelog;
    private int _changelogVersion = -1;

    // Returns the changelog embedded from the repository's CHANGELOG.md, without its title, prepared like a venue description; empty when the resource is missing.
    private RichDocument GetChangelog()
    {
        if (_changelog != null && _changelogVersion == RichTextVersion)
        {
            return _changelog;
        }

        _changelogVersion = RichTextVersion;
        using var stream = typeof(DirectoryBrowserWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream == null)
        {
            return _changelog = RichDocument.Empty;
        }

        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n').Where(line => !line.StartsWith("# ", StringComparison.Ordinal));
        return _changelog = VenueText.PrepareVenueDescription(lines);
    }

    // A source of the directory's data: its name as a link to its site, and what comes from it.
    private static void DrawDataSource(string name, string url, string what)
    {
        DrawLinkText(name, url);
        DrawTextWrapped(what, UiStyle.BodyMutedText);
        DrawVerticalRhythm(0.5f);
    }

    private static void DrawLinkText(string text, string url)
    {
        DrawText(text, UiStyle.LinkText);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(url);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(UiStyle.LinkText));
        }

        if (ImGui.IsItemClicked())
        {
            WebLink.Open(url);
        }
    }

    // A page of the settings in the list on the left: its icon and name, highlighted like a selected list row.
    private static bool DrawSettingsNavItem(int index, bool selected)
    {
        using var itemId = ImRaii.PushId(index);
        var pressed = ImGui.InvisibleButton("##SettingsNavItem", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight() + Scale(10f)));
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        if (selected || hovered)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(selected ? UiStyle.SelectedRowBackground : UiStyle.SecondaryButtonHoveredBackground));
        }

        if (selected)
        {
            DrawSelectedRowAccent(min, max);
        }

        var iconFont = PluginUiFont.IconFont;
        var iconSize = ImGui.GetFontSize();
        var iconText = IconText(SettingsPageIcons[index]);
        var iconWidth = ImGui.CalcTextSizeA(iconFont, iconSize, float.MaxValue, 0f, iconText, out _).X;
        var iconSlot = Scale(22f);
        var x = min.X + Scale(12f);
        var middle = (min.Y + max.Y) * 0.5f;
        var textColor = ImGui.GetColorU32(selected || hovered ? UiStyle.BodyStrongText : UiStyle.BodyMutedText);
        drawList.AddText(iconFont, iconSize, new Vector2(x + (iconSlot - iconWidth) * 0.5f, middle - iconSize * 0.5f), selected ? ImGui.GetColorU32(UiStyle.SectionAccentText) : textColor, iconText);
        drawList.AddText(new Vector2(x + iconSlot + Scale(8f), middle - ImGui.GetTextLineHeight() * 0.5f), textColor, SettingsPageLabels[index]);
        return pressed;
    }

    // A setting turned on and off: its name and what it does on the left, a switch on the right; the whole row toggles it.
    private static bool DrawSettingSwitch(string id, string label, string? description, ref bool value, bool enabled = true)
    {
        using var rowId = ImRaii.PushId(id);
        using var disabled = ImRaii.Disabled(!enabled);
        var start = ImGui.GetCursorPos();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - UiStyle.CardPadding.X);
        var switchSize = new Vector2(ImGui.GetTextLineHeight() * 1.9f, ImGui.GetTextLineHeight());
        var textWidth = MathF.Max(1f, width - switchSize.X - UiStyle.InlineGroupSpacing);
        using (ImRaii.Group())
        using (ImRaii.TextWrapPos(start.X + textWidth))
        {
            DrawTextWrapped(label, UiStyle.BodyText);
            if (description != null)
            {
                DrawTextWrapped(description, UiStyle.BodyMutedText);
            }
        }

        var height = ImGui.GetItemRectSize().Y;
        ImGui.SetCursorPos(start);
        var pressed = ImGui.InvisibleButton("##SettingSwitch", new Vector2(width, height));
        if (pressed)
        {
            value = !value;
        }

        DrawSwitch(new Vector2(ImGui.GetItemRectMin().X + width - switchSize.X, ImGui.GetItemRectMin().Y), switchSize, value, ImGui.IsItemHovered());
        return pressed;
    }

    private static void DrawSwitch(Vector2 min, Vector2 size, bool on, bool hovered)
    {
        var track = on
            ? hovered ? UiStyle.PrimaryButtonHoveredBackground : UiStyle.PrimaryButtonBackground
            : hovered ? UiStyle.SecondaryButtonHoveredBackground : UiStyle.SecondaryButtonBackground;
        var radius = size.Y * 0.5f;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(track), radius);
        drawList.AddRect(min, min + size, ImGui.GetColorU32(ImGuiCol.Border), radius);
        var knobX = on ? min.X + size.X - radius : min.X + radius;
        drawList.AddCircleFilled(new Vector2(knobX, min.Y + radius), radius - Scale(3f), ImGui.GetColorU32(on ? UiStyle.DisplayTitleText : UiStyle.BodyMutedText));
    }

    // The settings window, drawn by the directory it belongs to, in the same theme.
    internal sealed class SettingsWindow : Window
    {
        private readonly DirectoryBrowserWindow _directory;
        private IDisposable? _theme;
        private Vector2 _appliedMinimumSize;

        public SettingsWindow(DirectoryBrowserWindow directory)
            : base("FFXIV Venues Directory Settings###VenuesDirectorySettings", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _directory = directory;
            Size = SettingsDefaultSize;
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        // "Auto" is checked again whenever the settings open, in case Windows' format changed meanwhile.
        public override void OnOpen() => _directory.ApplyClockFormat();

        public override void PreDraw()
        {
            // As for the directory: the minimum size follows the interface size but stays within the game window.
            var minimumSize = UiScale.FitToScreen(SettingsMinSize * UiScale.Factor);
            if (_appliedMinimumSize != minimumSize)
            {
                _appliedMinimumSize = minimumSize;
                Size = UiScale.FitToScreen(SettingsDefaultSize);
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = minimumSize,
                    MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
                };
            }

            _theme = _directory._configuration.UseDalamudTheme ? null : PluginTheme.Push();
            base.PreDraw();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            _theme?.Dispose();
            _theme = null;
        }

        public override void Draw() => _directory.DrawSettings();
    }
}
