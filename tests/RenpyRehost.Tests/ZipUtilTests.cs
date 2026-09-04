using System.IO.Compression;
using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class ZipUtilTests
{
    private static string MakeZip(GameFixture g, string name, params string[] entries)
    {
        string path = g.At(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var e in entries)
            zip.CreateEntry(e).Open().Dispose();
        return path;
    }

    [Fact]
    public void Strips_a_single_common_top_folder()
    {
        using var g = GameFixture.Create();
        string zip = MakeZip(g, "sdk.zip",
            "renpy-7.4.11-sdk/renpy.py",
            "renpy-7.4.11-sdk/launcher/game/web.rpy");
        string dest = g.At("out");

        ZipUtil.Extract(zip, dest, stripTopFolder: true, NullProgressSink.Instance, "t");

        Assert.True(File.Exists(Path.Combine(dest, "renpy.py")));
        Assert.True(File.Exists(Path.Combine(dest, "launcher", "game", "web.rpy")));
        Assert.False(Directory.Exists(Path.Combine(dest, "renpy-7.4.11-sdk")));
    }

    [Fact]
    public void Keeps_the_web_folder_when_not_stripping()
    {
        using var g = GameFixture.Create();
        string zip = MakeZip(g, "web.zip", "web/index.html", "web/hash.txt", "web/index.wasm");
        string dest = g.At("sdk");

        ZipUtil.Extract(zip, dest, stripTopFolder: false, NullProgressSink.Instance, "t");

        Assert.True(File.Exists(Path.Combine(dest, "web", "index.html")));
        Assert.True(File.Exists(Path.Combine(dest, "web", "hash.txt")));
    }

    [Fact]
    public void Does_not_strip_when_top_level_is_mixed()
    {
        using var g = GameFixture.Create();
        string zip = MakeZip(g, "mixed.zip", "a/one.txt", "b/two.txt");
        string dest = g.At("out");

        ZipUtil.Extract(zip, dest, stripTopFolder: true, NullProgressSink.Instance, "t");

        Assert.True(File.Exists(Path.Combine(dest, "a", "one.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "b", "two.txt")));
    }

    [Fact]
    public void Rejects_path_traversal()
    {
        using var g = GameFixture.Create();
        string zip = MakeZip(g, "evil.zip", "../escape.txt");
        string dest = g.At("out");

        Assert.Throws<RehostException>(() =>
            ZipUtil.Extract(zip, dest, stripTopFolder: false, NullProgressSink.Instance, "t"));
    }
}
