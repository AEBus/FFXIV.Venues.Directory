using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIV.Venues.Directory.Infrastructure.Net;

namespace FFXIV.Venues.Directory.Infrastructure.Ui;

// Hovering an image shows where a click goes, and the image whole when previews are on; a click opens that address in the browser.
internal static class ImagePreview
{
    // The share of the game window the preview may cover at most.
    private const float MaxScreenShare = 0.6f;

    private const int MaxShownUrlLength = 80;

    // Call right after drawing the image (or its placeholder) as the last item, with the texture when it is loaded.
    public static void HandleHover(IDalamudTextureWrap? texture, string target, bool showImage)
    {
        if (!ImGui.IsItemHovered())
        {
            return;
        }

        Show(texture, target, showImage);
    }

    // For images drawn straight to the draw list: the caller decides whether the image is hovered.
    public static void Show(IDalamudTextureWrap? texture, string target, bool showImage)
    {
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        using (ImRaii.Tooltip())
        {
            if (showImage && texture != null && texture.Width > 0 && texture.Height > 0)
            {
                ImGui.Image(texture.Handle, FitSize(texture.Width, texture.Height));
            }

            ImGui.TextUnformatted(Shorten(target));
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            WebLink.Open(target);
        }
    }

    // The image at its own size and the interface scale, made smaller to fit the screen share, never larger.
    private static Vector2 FitSize(int width, int height)
    {
        var size = new Vector2(width, height) * UiScale.Total;
        var limit = ImGuiHelpers.MainViewport.Size * MaxScreenShare;
        var scale = MathF.Min(1f, MathF.Min(limit.X / size.X, limit.Y / size.Y));
        return size * scale;
    }

    private static string Shorten(string url) =>
        url.Length <= MaxShownUrlLength ? url : string.Concat(url.AsSpan(0, MaxShownUrlLength - 1), "…");
}
