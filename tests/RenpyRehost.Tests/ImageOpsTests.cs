using ImageMagick;
using RenpyRehost.Core.Imaging;

namespace RenpyRehost.Tests;

public class ImageOpsTests
{
    private static void WritePng(string path, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var img = new MagickImage(new MagickColor("#3a6ea5"), (uint)w, (uint)h);
        img.Format = MagickFormat.Png;
        img.Write(path);
    }

    private static void WriteWebp(string path, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var img = new MagickImage(new MagickColor("#c26a2a"), (uint)w, (uint)h);
        img.Format = MagickFormat.WebP;
        img.Write(path);
    }

    [Fact]
    public void Downscales_a_wide_png_and_shrinks_the_file()
    {
        using var g = GameFixture.Create();
        string p = g.At("images/cg.png");
        WritePng(p, 3840, 2160);
        long before = new FileInfo(p).Length;

        var r = ImageOps.DownscaleInPlace(p, 1920);

        Assert.True(r.Changed);
        Assert.True(r.BytesAfter < before);
        using var img = new MagickImage(p);
        Assert.Equal(1920u, img.Width);
        Assert.Equal(1080u, img.Height);
    }

    [Fact]
    public void Leaves_a_narrow_image_alone()
    {
        using var g = GameFixture.Create();
        string p = g.At("sprite.png");
        WritePng(p, 800, 600);
        long before = new FileInfo(p).Length;

        var r = ImageOps.DownscaleInPlace(p, 1920);

        Assert.False(r.Changed);
        Assert.Equal(before, new FileInfo(p).Length);
    }

    [Fact]
    public void Transcodes_opaque_webp_to_jpg_and_removes_the_original()
    {
        using var g = GameFixture.Create();
        string webp = g.At("images/bg.webp");
        WriteWebp(webp, 1280, 720); // solid colour -> opaque

        string result = ImageOps.TranscodeInPlace(webp);

        Assert.Equal(g.At("images/bg.jpg"), result);
        Assert.False(File.Exists(webp));
        using var img = new MagickImage(result);
        Assert.Equal(1280u, img.Width);
    }

    [Fact]
    public void Transcodes_transparent_webp_to_png()
    {
        using var g = GameFixture.Create();
        string webp = g.At("sprite.webp");
        using (var img = new MagickImage(MagickColors.Transparent, 400, 400))
        {
            img.Format = MagickFormat.WebP;
            img.Write(webp);
        }

        string result = ImageOps.TranscodeInPlace(webp);

        Assert.Equal(g.At("sprite.png"), result);
        Assert.False(File.Exists(webp));
    }
}
