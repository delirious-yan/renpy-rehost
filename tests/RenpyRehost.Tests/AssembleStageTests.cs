using System.Text.Json;
using RenpyRehost.Core;
using RenpyRehost.Core.Stages;

namespace RenpyRehost.Tests;

public class AssembleStageTests
{
    private static ConversionContext MakeContext(GameFixture g, out string webBuild, out string outDir)
    {
        webBuild = g.At("build/MyGame-web");
        outDir = g.At("out/MyGame");
        Directory.CreateDirectory(webBuild);
        File.WriteAllText(Path.Combine(webBuild, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(webBuild, "game.zip"), "zip");
        Directory.CreateDirectory(Path.Combine(webBuild, "game"));
        File.WriteAllText(Path.Combine(webBuild, "game", "bg.jpg"), new string('x', 500));

        var opt = new ConversionOptions
        {
            SdkCacheDir = g.At("_sdk"),
            ToolsCacheDir = g.At("_tools"),
            WorkDir = g.At("_work"),
            OutputDir = outDir,
        };
        var ctx = new ConversionContext(g.Root, opt, NullProgressSink.Instance)
        {
            WebBuildDir = webBuild,
            GameName = "My Game",
            Version = new RenpyVersion(8, 3, 7),
            DetectedVersion = new RenpyVersion(7, 4, 11),
        };
        return ctx;
    }

    [Fact]
    public async Task Moves_the_build_to_out_and_writes_a_valid_sidecar()
    {
        using var g = GameFixture.Create();
        var ctx = MakeContext(g, out var webBuild, out var outDir);

        await new AssembleStage().RunAsync(ctx, default);

        Assert.Equal(outDir, ctx.ServeDir);
        Assert.True(File.Exists(Path.Combine(outDir, "index.html")));
        Assert.False(Directory.Exists(webBuild)); // moved, not copied

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "rehost.json")));
        var root = doc.RootElement;
        Assert.Equal("My Game", root.GetProperty("title").GetString());
        Assert.Equal("7.4.11", root.GetProperty("sourceVersion").GetString());
        Assert.Equal("8.3.7", root.GetProperty("builtWith").GetString());
        Assert.Equal("index.html", root.GetProperty("entry").GetString());
        Assert.True(root.GetProperty("sizeBytes").GetInt64() > 0);
    }
}
