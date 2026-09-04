using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class LibraryTests
{
    private static string MakeBuild(GameFixture g, string name, string? sidecar)
    {
        string dir = g.At($"builds/{name}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(dir, "game.zip"), new string('x', 128));
        if (sidecar is not null) File.WriteAllText(Path.Combine(dir, "rehost.json"), sidecar);
        return dir;
    }

    [Fact]
    public void AddOrUpdate_reads_the_sidecar_and_round_trips_through_disk()
    {
        using var g = GameFixture.Create();
        string libPath = g.At("library.json");
        string build = MakeBuild(g, "sample", """
            { "schema": 1, "title": "Sample Game", "sourceVersion": "7.4.11",
              "builtWith": "8.3.7", "builtUtc": "2026-09-02T00:00:00Z", "sizeBytes": 123 }
            """);

        var lib = Library.Load(libPath);
        lib.AddOrUpdate(build);
        lib.Save(libPath);

        var reloaded = Library.Load(libPath);
        var e = Assert.Single(reloaded.Entries);
        Assert.Equal("Sample Game", e.Title);
        Assert.Equal("7.4.11", e.SourceVersion);
        Assert.Equal("8.3.7", e.BuiltWith);
        Assert.Equal(123, e.SizeBytes);
        Assert.True(e.Exists);
    }

    [Fact]
    public void AddOrUpdate_is_idempotent_by_path_and_falls_back_to_folder_name()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "no-sidecar", null);

        var lib = Library.Load(g.At("library.json"));
        lib.AddOrUpdate(build);
        lib.AddOrUpdate(build);

        var e = Assert.Single(lib.Entries);
        Assert.Equal("no-sidecar", e.Title);
        Assert.True(e.SizeBytes > 0); // measured from disk
    }

    [Fact]
    public void AddOrUpdate_rejects_a_folder_that_is_not_a_web_build()
    {
        using var g = GameFixture.Create();
        Directory.CreateDirectory(g.At("empty"));
        var lib = new Library();
        Assert.Throws<RehostException>(() => lib.AddOrUpdate(g.At("empty")));
    }

    [Fact]
    public void Remove_and_MarkPlayed_work_by_path()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "game", null);
        var lib = new Library();
        lib.AddOrUpdate(build);

        lib.MarkPlayed(build);
        Assert.NotNull(lib.Entries[0].LastPlayedUtc);

        Assert.True(lib.Remove(build));
        Assert.Empty(lib.Entries);
        Assert.False(lib.Remove(build));
    }

    [Fact]
    public void Move_relocates_the_build_and_repoints_the_entry()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "game", null);
        string destParent = g.At("elsewhere");

        var lib = new Library();
        lib.AddOrUpdate(build);
        string moved = lib.Move(build, destParent);

        Assert.Equal(Path.Combine(destParent, "game"), moved);
        Assert.False(Directory.Exists(build));
        Assert.True(File.Exists(Path.Combine(moved, "index.html")));
        Assert.Equal(moved, lib.Entries[0].Path);
        Assert.True(lib.Entries[0].Exists);
    }

    [Fact]
    public void Move_reports_an_instant_tick_for_a_same_volume_rename()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "game", null);
        var lib = new Library();
        lib.AddOrUpdate(build);

        var ticks = new List<Library.MoveProgress>();
        lib.Move(build, g.At("moved"), new SyncProgress<Library.MoveProgress>(ticks.Add));

        Assert.Contains(ticks, t => t is { Instant: true, Fraction: 1.0 });
    }

    private sealed class SyncProgress<T>(Action<T> h) : IProgress<T>
    {
        public void Report(T value) => h(value);
    }

    [Fact]
    public void Move_is_a_no_op_when_already_in_the_destination()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "game", null);
        var lib = new Library();
        lib.AddOrUpdate(build);

        string moved = lib.Move(build, Path.GetDirectoryName(build)!);

        Assert.Equal(build, moved);
        Assert.True(Directory.Exists(build));
    }

    [Fact]
    public void Move_rejects_an_unknown_build_or_an_occupied_destination()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "game", null);
        var lib = new Library();
        lib.AddOrUpdate(build);

        Assert.Throws<RehostException>(() => lib.Move(g.At("not-tracked"), g.At("x")));

        string dest = g.At("dest");
        Directory.CreateDirectory(Path.Combine(dest, "game"));
        Assert.Throws<RehostException>(() => lib.Move(build, dest));
    }

    [Fact]
    public void AddOrUpdate_records_the_source_path_when_given_one()
    {
        using var g = GameFixture.Create();
        string build = MakeBuild(g, "game", null);
        string src = g.At("SomeGame");
        Directory.CreateDirectory(src);

        var lib = new Library();
        var e = lib.AddOrUpdate(build, src);

        Assert.Equal(Path.GetFullPath(src), e.SourcePath);
    }

    [Fact]
    public void Load_returns_an_empty_library_for_a_missing_or_corrupt_file()
    {
        using var g = GameFixture.Create();
        Assert.Empty(Library.Load(g.At("nope.json")).Entries);

        File.WriteAllText(g.At("bad.json"), "{ not json");
        Assert.Empty(Library.Load(g.At("bad.json")).Entries);
    }
}
