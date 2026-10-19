using SkiaSharp;

namespace Lcd207;

public enum FitMode { Cover, Contain, Stretch }

public static class ImageEncoder
{
    /// <summary>
    /// Loads an image, rotates it (degrees clockwise; null = auto-rotate landscape to portrait),
    /// scales to width x height and encodes a baseline 4:2:0 JPEG no larger than maxBytes.
    /// </summary>
    public static byte[] EncodeForPanel(string path, int width, int height, int maxBytes,
        int? rotation = null, FitMode fit = FitMode.Cover, int quality = 95)
    {
        using var source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Could not decode image: {path}");

        int rot = rotation ?? (source.Width > source.Height ? 90 : 0);
        using var rotated = Rotate(source, rot);
        using var canvasBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(canvasBitmap))
        {
            canvas.Clear(SKColors.Black);
            var dest = FitRect(rotated.Width, rotated.Height, width, height, fit);
            using var image = SKImage.FromBitmap(rotated);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }

        for (int q = quality; q >= 50; q -= 5)
        {
            byte[] jpeg = EncodeJpeg(canvasBitmap, q);
            if (jpeg.Length <= maxBytes)
                return jpeg;
        }
        throw new InvalidOperationException($"Could not encode image under {maxBytes} bytes");
    }

    public static byte[] SolidJpeg(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.Black);
        return EncodeJpeg(bmp, 95);
    }

    public static byte[] TransparentPng(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.Transparent);
        using var pixmap = bmp.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9))
            ?? throw new InvalidOperationException("Image encode failed");
        return data.ToArray();
    }

    private static byte[] EncodeJpeg(SKBitmap bmp, int quality)
    {
        using var pixmap = bmp.PeekPixels();
        var options = new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore);
        using var data = pixmap.Encode(options)
            ?? throw new InvalidOperationException("Image encode failed");
        return data.ToArray();
    }

    private static SKBitmap Rotate(SKBitmap src, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees is not (0 or 90 or 180 or 270))
            throw new ArgumentException("Rotation must be 0, 90, 180 or 270");

        bool swap = degrees is 90 or 270;
        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Black);
        canvas.Translate(dst.Width / 2f, dst.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-src.Width / 2f, -src.Height / 2f);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    private static SKRect FitRect(int srcW, int srcH, int dstW, int dstH, FitMode fit)
    {
        if (fit == FitMode.Stretch)
            return new SKRect(0, 0, dstW, dstH);

        float sx = (float)dstW / srcW, sy = (float)dstH / srcH;
        float scale = fit == FitMode.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
        float w = srcW * scale, h = srcH * scale;
        float x = (dstW - w) / 2f, y = (dstH - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }
}
