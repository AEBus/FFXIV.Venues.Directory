using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using FFXIV.Venues.Directory.Infrastructure.Ui;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

internal sealed partial class DirectoryBrowserWindow
{
    private enum UiButtonTone
    {
        Primary,
        Secondary
    }

    private enum UiChipTone
    {
        Neutral,
        Accent,
        Warning,
        Positive,
        Caution
    }

    private static class UiStyle
    {
        private static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

        public static float CardRounding => Scale(6f);
        public const float CardSpacingUnits = 24f;
        public static float InlineSpacing => Scale(6f);
        public static float InlineGroupSpacing => Scale(12f);
        public static float CompactRowSpacing => Scale(6f);
        public static float MinimumRowHeight => Scale(28f);
        public static float BadgeSide => Scale(18f);
        public static float TagRightInset => Scale(12f);
        public static float FilterRowSpacing => Scale(6f);
        public static float FilterButtonGap => Scale(6f);
        public static float FilterSplitButtonGap => Scale(6f);
        public static float SegmentGap => Scale(3f);
        public static float QuickFilterButtonWidth => Scale(96f);
        public static float ToolbarButtonInset => Scale(6f);
        public static float ListSelectedAccentWidth => Scale(3f);
        public static float ListStatusDotSize => Scale(6f);
        public static float ListSizeColumnWidth => Scale(28f);
        public static float ListSizeBadgeWidth => Scale(24f);
        public static float ListMarkerReserve => Scale(60f);
        public static float ListStatusColumnTextReserve => Scale(12f);
        public static float ListStatusHorizontalInset => Scale(6f);
        // Names get more room than locations: a location is cut with an ellipsis (full in the tooltip), a name wraps.
        public const float ListVenueColumnWeight = 1.35f;
        public const float ListAddressColumnWeight = 1f;

        public static Vector2 CardPadding => UiScale.Vector(12f, 12f);
        public static Vector2 ChipPadding => UiScale.Vector(12f, 6f);
        public static Vector2 ToggleChipPadding => UiScale.Vector(24f, 6f);
        public static Vector2 ActionButtonPadding => UiScale.Vector(12f, 6f);
        public static Vector2 IconActionButtonPadding => UiScale.Vector(7f, 5f);
        public static Vector2 RemovableChipPadding => UiScale.Vector(9f, 3f);

        public static Vector4 BodyText => ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        public static Vector4 BodyStrongText => BodyText;
        public static Vector4 BodyMutedText => WithAlpha(BodyText, 0.72f);
        public static Vector4 BodySubtleText => WithAlpha(BodyText, 0.56f);
        public static Vector4 SectionAccentText => new(0.55f, 0.70f, 1f, 1f);
        public static Vector4 SectionHeaderText => new(0.76f, 0.70f, 0.54f, 1f);
        public static Vector4 DisplayTitleText => new(0.97f, 0.97f, 1f, 1f);
        public static Vector4 TabUnderline => new(0.85f, 0.71f, 0.44f, 1f);
        public static Vector4 SelectedRowAccent => new(0.55f, 0.70f, 1f, 1f);
        public static Vector4 WarningText => ImGuiColors.DalamudYellow;
        public static Vector4 ErrorText => ImGuiColors.DalamudRed;
        public static Vector4 PositiveText => new(0.39f, 0.75f, 0.48f, 1f);
        public static Vector4 SoonText => new(0.95f, 0.72f, 0.36f, 1f);
        public static Vector4 FavoriteText => new(0.96f, 0.80f, 0.34f, 1f);
        public static Vector4 EventText => new(1f, 0.52f, 0.68f, 1f);
        public static Vector4 LinkText => ImGuiColors.ParsedBlue;
        public static Vector4 HeaderShadowText => new(0f, 0f, 0f, 0.85f);
        public static Vector4 ListHeaderText => BodyMutedText;
        public static Vector4 ListHeaderBackground => new(0x1b / 255f, 0x20 / 255f, 0x34 / 255f, 1f);
        public static Vector4 ListAlternateRowBackground => new(1f, 1f, 1f, 0.02f);
        public static Vector4 ListSelectedAccent => new(0.78f, 0.66f, 0.30f, 1f);

        public static Vector4 SectionBackground => DefaultSectionBackground;
        public static Vector4 SectionHighlightBackground => HighlightSectionBackground;
        public static Vector4 RaisedSurfaceBackground => PluginTheme.Surface;
        public static Vector4 DetailInsetBackground => RaisedSurfaceBackground;
        public static Vector4 FilterGroupBackground => RaisedSurfaceBackground;
        public static Vector4 TagChipBackground => PluginTheme.Control;
        public static Vector4 AccentChipBackground => PluginTheme.Accent;
        public static Vector4 WarningChipBackground => new(0.48f, 0.18f, 0.18f, 1f);
        public static Vector4 PositiveChipBackground => new(0.15f, 0.34f, 0.22f, 1f);
        public static Vector4 CautionChipBackground => new(0.42f, 0.30f, 0.10f, 1f);

        public static Vector4 SelectedRowBackground => new(0x2b / 255f, 0x3a / 255f, 0x64 / 255f, 1f);
        public static Vector4 SelectedRowHoveredBackground => new(0x30 / 255f, 0x40 / 255f, 0x6c / 255f, 1f);
        public static Vector4 SelectedRowActiveBackground => PluginTheme.Accent;

        public static Vector4 PrimaryButtonBackground => PluginTheme.Accent;
        public static Vector4 PrimaryButtonHoveredBackground => PluginTheme.AccentHovered;
        public static Vector4 PrimaryButtonActiveBackground => PluginTheme.AccentActive;
        public static Vector4 SecondaryButtonBackground => PluginTheme.Control;
        public static Vector4 SecondaryButtonHoveredBackground => PluginTheme.ControlHovered;
        public static Vector4 SecondaryButtonActiveBackground => PluginTheme.ControlActive;
    }

    // Changes whenever prepared rich text would come out differently: another UI font, another clock format.
    private static int RichTextVersion => ((UiGlyphs.Current?.Generation ?? 0) << 1) | (Use12HourClock ? 1 : 0);

    private static RichTextColors RichTextColors => new(
        UiStyle.BodyText,
        UiStyle.LinkText,
        new Vector4(1f, 0.52f, 0.68f, 1f),
        new Vector4(0.34f, 0.11f, 0.20f, 1f),
        UiStyle.BodyMutedText,
        new Vector4(0.20f, 0.22f, 0.30f, 1f),
        UiStyle.BodySubtleText);
}
