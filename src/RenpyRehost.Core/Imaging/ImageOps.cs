using ImageMagick;

namespace RenpyRehost.Core.Imaging;

/// <summary>
/// Image work for the asset pipeline, on ImageMagick (handles PNG/JPG/WebP/AVIF/…
/// decode + encode uniformly, unlike GDI+ which needs OS codecs for WebP).
/// </summary>
public static class ImageOps
{
    /// <summary>Raster formats Ren'Py's web build treats as progressive-download images.</summary>
    public static readonly string[] WebImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".avif" };

    /// <summary>Formats the web build's placeholder generator (pygame_sdl2) can't load — must be transcoded.</summary>
    public static readonly string[] NeedsTranscodeExtensions = { ".webp", ".avif" };

    public readonly record struct DownscaleResult(bool Changed, long BytesBefore, long BytesAfter, string? SkipReason);

    /// <summary>
    /// Re-encode <paramref name="srcPath"/> (WebP/AVIF/…) into a format the web
    /// build's placeholder tool can read: JPEG when the image is fully opaque
    /// (much smaller), PNG when it has transparency. Deletes the original and
    /// returns the new path.
    /// </summary>
    public static string TranscodeInPlace(string srcPath)
    {
        using var img = new MagickImage(srcPath);
        bool opaque = !img.HasAlpha || img.IsOpaque;

        string dstPath;
        if (opaque)
        {
            dstPath = Path.ChangeExtension(srcPath, ".jpg");
            img.Format = MagickFormat.Jpeg;
            img.Quality = 90;
            if (img.HasAlpha) img.Alpha(AlphaOption.Remove);
        }
        else
        {
            dstPath = Path.ChangeExtension(srcPath, ".png");
            img.Format = MagickFormat.Png;
        }
        img.Write(dstPath);

        if (!string.Equals(dstPath, srcPath, StringComparison.OrdinalIgnoreCase))
            File.Delete(srcPath);
        return dstPath;
    }

    /// <summary>A 2×2 fully transparent PNG — stand-in for an image we couldn't read.</summary>
    public static void WriteTinyTransparentPng(string path)
    {
        using var img = new MagickImage(MagickColors.Transparent, 2, 2);
        img.Format = MagickFormat.Png32;
        img.Write(path);
    }

    /// <summary>
    /// Downscale to at most <paramref name="maxWidth"/> px wide, in place, keeping
    /// format and alpha. No-op if already narrow enough or the re-encode is larger.
    /// </summary>
    public static DownscaleResult DownscaleInPlace(string path, int maxWidth)
    {
        long before = new FileInfo(path).Length;
        using var img = new MagickImage(path);

        if (img.Width <= (uint)maxWidth)
            return new DownscaleResult(false, before, before, "already within width");

        uint w = (uint)maxWidth;
        uint h = (uint)Math.Max(1, Math.Round(img.Height * (maxWidth / (double)img.Width)));
        img.FilterType = FilterType.Lanczos;
        img.Resize(w, h);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
        {
            img.Format = MagickFormat.Jpeg;
            img.Quality = 88;
        }
        else
        {
            img.Format = MagickFormat.Png;
        }

        byte[] encoded = img.ToByteArray();
        if (encoded.LongLength >= before)
            return new DownscaleResult(false, before, before, "re-encode not smaller");

        // Write a fresh file and swap it in — never overwrite `path` in place. The
        // reconstructed project hard-links large assets from the source game, so an
        // in-place write would edit the player's original files too.
        string tmp = path + ".rehost-tmp";
        File.WriteAllBytes(tmp, encoded);
        File.Move(tmp, path, overwrite: true);
        return new DownscaleResult(true, before, encoded.LongLength, null);
    }
}
