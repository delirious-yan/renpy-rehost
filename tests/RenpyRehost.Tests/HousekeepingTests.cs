using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class HousekeepingTests
{
    private static void File(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, new string('x', bytes));
    }

    [Fact]
    public void SurveyWork_measures_proj_and_ingest_but_not_sdk_or_tools()
    {
        using var g = GameFixture.Create();
        string work = g.At("work");
        File(Path.Combine(work, "proj", "Game", "game", "big.dat"), 5000);
        File(Path.Combine(work, "proj", "Game-1.0-dists", "Game-web.zip"), 8000);
        File(Path.Combine(work, "ingest", "abc123", "x.rpa"), 2000);

        // siblings that must be left alone
        File(Path.Combine(g.At("sdk"), "8.3.7", "renpy.exe"), 9999);
        File(Path.Combine(work, "..", "tools", "ffmpeg.exe"), 9999);

        var (bytes, items) = Housekeeping.SurveyWork(work);

        Assert.Equal(15000, bytes);
        Assert.Equal(3, items); // proj/Game, proj/Game-1.0-dists, ingest/abc123
    }

    [Fact]
    public void CleanAllWork_deletes_scratch_and_returns_bytes_freed()
    {
        using var g = GameFixture.Create();
        string work = g.At("work");
        File(Path.Combine(work, "proj", "Game", "a.dat"), 4000);
        File(Path.Combine(work, "ingest", "h", "b.dat"), 1000);

        long freed = Housekeeping.CleanAllWork(work);

        Assert.Equal(5000, freed);
        Assert.False(Directory.Exists(Path.Combine(work, "proj", "Game")));
        Assert.False(Directory.Exists(Path.Combine(work, "ingest", "h")));
    }

    [Fact]
    public void CleanAllWork_on_a_missing_work_dir_is_a_no_op()
    {
        using var g = GameFixture.Create();
        Assert.Equal(0, Housekeeping.CleanAllWork(g.At("nope")));
    }

    [Fact]
    public void ScratchDirsFor_finds_the_project_and_its_dists_sibling()
    {
        using var g = GameFixture.Create();
        string work = g.At("work");
        string proj = Path.Combine(work, "proj", "Game");
        File(Path.Combine(proj, "game", "s.rpy"), 10);
        File(Path.Combine(work, "proj", "Game-0.9-dists", "Game-web.zip"), 20);

        var ctx = new ConversionContext("src", Opts(work), new NullProgressSink()) { ProjectDir = proj };
        var dirs = Housekeeping.ScratchDirsFor(ctx).ToList();

        Assert.Contains(proj, dirs);
        Assert.Contains(Path.Combine(work, "proj", "Game-0.9-dists"), dirs);
    }

    private static ConversionOptions Opts(string work) => new()
    {
        SdkCacheDir = work + "\\sdk",
        ToolsCacheDir = work + "\\tools",
        WorkDir = work,
        OutputDir = work + "\\out",
    };
}
