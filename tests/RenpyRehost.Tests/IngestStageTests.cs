using RenpyRehost.Core;
using RenpyRehost.Core.Stages;

namespace RenpyRehost.Tests;

public class IngestStageTests
{
    private static ConversionContext Context(GameFixture g, string source)
    {
        var opt = new ConversionOptions
        {
            SdkCacheDir = g.At("_sdk"),
            ToolsCacheDir = g.At("_tools"),
            WorkDir = g.At("_work"),
            OutputDir = g.At("_out"),
        };
        return new ConversionContext(source, opt, NullProgressSink.Instance);
    }

    [Fact]
    public async Task Finds_game_folder_under_a_wrapper()
    {
        using var g = GameFixture.Create();
        g.Bytes("MyGame-1.2-pc/game/script.rpyc", 8);
        g.File("MyGame-1.2-pc/renpy/vc_version.py", "version_tuple = (8, 2, 3, x)");
        g.Bytes("MyGame-1.2-pc/MyGame.exe", 4);

        var ctx = Context(g, g.Root);
        await new IngestStage().RunAsync(ctx, default);

        Assert.Equal(g.At("MyGame-1.2-pc/game"), ctx.GameDir);
        Assert.Equal("My Game", ctx.GameName); // CamelCase split, version + "-pc" stripped
    }

    [Fact]
    public async Task Prefers_the_game_subfolder_over_its_parent()
    {
        using var g = GameFixture.Create();
        g.Bytes("game/script.rpyc", 8);

        var ctx = Context(g, g.Root);
        await new IngestStage().RunAsync(ctx, default);

        Assert.Equal(g.At("game"), ctx.GameDir);
    }

    [Fact]
    public async Task Accepts_an_rpa_only_game()
    {
        using var g = GameFixture.Create();
        g.Bytes("game/archive.rpa", 32);

        var ctx = Context(g, g.Root);
        await new IngestStage().RunAsync(ctx, default);

        Assert.Equal(g.At("game"), ctx.GameDir);
    }

    [Fact]
    public async Task Missing_game_folder_is_a_clear_error()
    {
        using var g = GameFixture.Create();
        g.File("readme.txt", "no game here");

        var ctx = Context(g, g.Root);
        var ex = await Assert.ThrowsAsync<RehostException>(() => new IngestStage().RunAsync(ctx, default));
        Assert.Contains("game", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dropped_exe_resolves_its_folder()
    {
        using var g = GameFixture.Create();
        g.Bytes("game/script.rpyc", 8);
        g.Bytes("Start.exe", 4);

        var ctx = Context(g, g.At("Start.exe"));
        await new IngestStage().RunAsync(ctx, default);

        Assert.Equal(g.At("game"), ctx.GameDir);
    }
}
