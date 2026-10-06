using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FFXIV.Venues.Directory.Infrastructure.Media;

// The frames of an animated image (GIF, animated WebP, APNG), fully composed and in RGBA, with how long each one shows.
internal sealed record AnimatedImage(int Width, int Height, IReadOnlyList<AnimatedImageFrame> Frames);

internal sealed record AnimatedImageFrame(byte[] Rgba, int DurationMs);

internal static class AnimatedImageDecoder
{
    // The longest side frames are scaled down to; description images and banners are drawn much smaller.
    private const int MaxSide = 1024;

    // All frames of one animation together, in bytes of RGBA; larger animations are scaled down to fit.
    private const long MaxBytes = 48L * 1024 * 1024;

    private const int MaxFrames = 300;

    // All frames of one animation together, in bytes of RGBA at its own size: every frame is decoded whole before it is scaled down, so a larger animation is shown still instead.
    private const long MaxDecodedBytes = 256L * 1024 * 1024;

    // Browsers show frames with a delay under 20 ms for 100 ms, as most such files were made for that.
    private const int MinDurationMs = 20;
    private const int ShortDurationMs = 100;

    // Returns the frames when the image is animated, or null for a still image (or one this decoder cannot read, or too large to decode whole), which is then shown still.
    public static AnimatedImage? TryDecode(byte[] bytes)
    {
        try
        {
            var info = Image.Identify(bytes);
            var framesToDecode = Math.Min(info.FrameMetadataCollection.Count, MaxFrames);
            if (framesToDecode < 2 || (long)info.Width * info.Height * 4 * framesToDecode > MaxDecodedBytes)
            {
                return null;
            }

            using var image = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = MaxFrames }, bytes);
            var frameCount = image.Frames.Count;
            if (frameCount < 2)
            {
                return null;
            }

            var scale = Math.Min(1.0, (double)MaxSide / Math.Max(image.Width, image.Height));
            var bytesAtScale = (double)image.Width * image.Height * 4 * frameCount * scale * scale;
            if (bytesAtScale > MaxBytes)
            {
                scale *= Math.Sqrt(MaxBytes / bytesAtScale);
            }

            if (scale < 1.0)
            {
                image.Mutate(x => x.Resize(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
            }

            var format = image.Metadata.DecodedImageFormat;
            var frames = new List<AnimatedImageFrame>(frameCount);
            foreach (var frame in image.Frames)
            {
                var rgba = new byte[image.Width * image.Height * 4];
                frame.CopyPixelDataTo(rgba);
                frames.Add(new AnimatedImageFrame(rgba, DurationMs(frame.Metadata, format)));
            }

            return new AnimatedImage(image.Width, image.Height, frames);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }
    }

    private static int DurationMs(SixLabors.ImageSharp.Metadata.ImageFrameMetadata metadata, IImageFormat? format)
    {
        var ms = format switch
        {
            GifFormat => metadata.GetGifMetadata().FrameDelay * 10,
            WebpFormat => (int)Math.Min(int.MaxValue, metadata.GetWebpMetadata().FrameDelay),
            PngFormat => (int)Math.Round(metadata.GetPngMetadata().FrameDelay.ToDouble() * 1000),
            _ => ShortDurationMs,
        };
        return ms < MinDurationMs ? ShortDurationMs : ms;
    }
}
