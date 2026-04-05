using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;

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
        Warning
    }

    private static class UiStyle
    {
        private static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

        public static float CardRounding => Scale(6f);
        public const float CardSpacingUnits = 24f;
        public static float InlineSpacing => Scale(6f);
        public static float InlineGroupSpacing => Scale(12f);
        public static float CompactRowSpacing => Scale(6f);
        public static float MinimumRowHeight => Scale(24f);
        public static float BadgeSide => Scale(18f);
        public static float TagRightInset => Scale(12f);
        public static float FilterRowSpacing => Scale(6f);
        public static float FilterButtonGap => Scale(6f);
        public static float FilterSplitButtonGap => Scale(6f);
        public static float QuickFilterButtonWidth => Scale(96f);
        public static float ToolbarButtonInset => Scale(6f);
        public static float ListSelectedAccentWidth => Scale(3f);
        public static float ListStatusDotSize => Scale(6f);
        public static float ListSizeColumnWidth => Scale(28f);
        public static float ListSizeBadgeWidth => Scale(24f);
        public static float ListNarrowVenueColumnWidth => Scale(300f);
        public static float ListNarrowAddressColumnWidth => Scale(372f);
        public static float ListNarrowStatusColumnWidth => Scale(148f);
        public static float ListAddressColumnWidth => Scale(240f);
        public static float ListStatusColumnWidth => Scale(248f);
        public static float ListStatusColumnTextReserve => Scale(56f);
        public static float ListStatusHorizontalInset => Scale(6f);
        public const float ListVenueColumnWeight = 1f;

        public static Vector2 CardPadding => ImGuiHelpers.ScaledVector2(12f, 12f);
        public static Vector2 ChipPadding => ImGuiHelpers.ScaledVector2(12f, 6f);
        public static Vector2 ToggleChipPadding => ImGuiHelpers.ScaledVector2(24f, 6f);
        public static Vector2 ActionButtonPadding => ImGuiHelpers.ScaledVector2(12f, 6f);
        public static Vector2 IconActionButtonPadding => ImGuiHelpers.ScaledVector2(7f, 5f);

        public static Vector4 BodyText => ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        public static Vector4 BodyStrongText => BodyText;
        public static Vector4 BodyMutedText => WithAlpha(BodyText, 0.72f);
        public static Vector4 BodySubtleText => WithAlpha(BodyText, 0.56f);
        public static Vector4 SectionAccentText => ImGuiColors.DalamudViolet;
        public static Vector4 SectionHeaderText => new(0.76f, 0.70f, 0.54f, 1f);
        public static Vector4 DisplayTitleText => new(0.88f, 0.80f, 0.60f, 1f);
        public static Vector4 WarningText => ImGuiColors.DalamudYellow;
        public static Vector4 ErrorText => ImGuiColors.DalamudRed;
        public static Vector4 PositiveText => new(0.39f, 0.75f, 0.48f, 1f);
        public static Vector4 LinkText => ImGuiColors.ParsedBlue;
        public static Vector4 HeaderShadowText => new(0f, 0f, 0f, 0.85f);
        public static Vector4 ListHeaderText => BodyMutedText;
        public static Vector4 ListHeaderBackground => new(0.16f, 0.16f, 0.19f, 1f);
        public static Vector4 ListAlternateRowBackground => new(1f, 1f, 1f, 0.02f);
        public static Vector4 ListSelectedAccent => new(0.78f, 0.66f, 0.30f, 1f);

        public static Vector4 SectionBackground => DefaultSectionBackground;
        public static Vector4 SectionHighlightBackground => HighlightSectionBackground;
        public static Vector4 DrawerOverlayBackground => new(0.10f, 0.10f, 0.13f, 1f);
        public static Vector4 RaisedSurfaceBackground => new(0.18f, 0.18f, 0.22f, 0.93f);
        public static Vector4 DetailInsetBackground => RaisedSurfaceBackground;
        public static Vector4 FilterGroupBackground => RaisedSurfaceBackground;
        public static Vector4 TagChipBackground => new(0.24f, 0.24f, 0.28f, 1f);
        public static Vector4 AccentChipBackground => new(0.23f, 0.31f, 0.45f, 1f);
        public static Vector4 WarningChipBackground => new(0.48f, 0.18f, 0.18f, 1f);

        public static Vector4 SelectedRowBackground => new(0.26f, 0.30f, 0.50f, 0.80f);
        public static Vector4 SelectedRowHoveredBackground => new(0.24f, 0.28f, 0.45f, 0.85f);
        public static Vector4 SelectedRowActiveBackground => new(0.30f, 0.35f, 0.58f, 0.90f);

        public static Vector4 PrimaryButtonBackground => new(0.23f, 0.31f, 0.45f, 1f);
        public static Vector4 PrimaryButtonHoveredBackground => new(0.27f, 0.35f, 0.50f, 1f);
        public static Vector4 PrimaryButtonActiveBackground => new(0.20f, 0.27f, 0.40f, 1f);
        public static Vector4 SecondaryButtonBackground => new(0.24f, 0.24f, 0.28f, 1f);
        public static Vector4 SecondaryButtonHoveredBackground => new(0.28f, 0.28f, 0.33f, 1f);
        public static Vector4 SecondaryButtonActiveBackground => new(0.20f, 0.20f, 0.24f, 1f);
    }
}
