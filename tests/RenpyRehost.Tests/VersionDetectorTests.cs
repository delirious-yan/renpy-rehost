using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class VersionDetectorTests
{
    [Fact]
    public void Reads_vc_version_py_first()
    {
        using var g = GameFixture.Create();
        g.File("renpy/vc_version.py", "version_tuple = (8, 2, 3, vc_version)\nofficial = True");
        g.File("log.txt", "Ren'Py 8.1.0.11111"); // should be ignored â€” vc_version wins

        var d = VersionDetector.Detect(g.Root);

        Assert.Equal(VersionSource.VcVersionPy, d.Source);
        Assert.Equal(new RenpyVersion(8, 2, 3), d.Version);
    }

    [Fact]
    public void Falls_back_to_log_txt_banner()
    {
        using var g = GameFixture.Create();
        g.File("log.txt", "Windows-10-10.0.22631\nRen'Py 8.0.3.22090809\nBootstrap to the start of init.");

        var d = VersionDetector.Detect(g.Root);

        Assert.Equal(VersionSource.LogTxt, d.Source);
        Assert.Equal(new RenpyVersion(8, 0, 3), d.Version);
    }

    [Fact]
    public void Lib_py3_gives_era_hint_but_no_version()
    {
        using var g = GameFixture.Create();
        g.Dir("lib/py3-windows-x86_64");

        var d = VersionDetector.Detect(g.Root);

        Assert.Equal(VersionSource.LibRuntimeHint, d.Source);
        Assert.Null(d.Version);
        Assert.Contains(d.Evidence, e => e.Contains("py3"));
    }

    [Fact]
    public void Accepts_the_game_folder_directly_and_walks_up()
    {
        using var g = GameFixture.Create();
        g.File("renpy/vc_version.py", "version_tuple = (8, 3, 1, vc_version)");
        g.Bytes("game/script.rpyc", 8);

        var d = VersionDetector.Detect(g.At("game"));

        Assert.Equal(new RenpyVersion(8, 3, 1), d.Version);
    }

    [Fact]
    public void No_markers_yields_none()
    {
        using var g = GameFixture.Create();
        g.File("readme.txt", "hello");

        var d = VersionDetector.Detect(g.Root);

        Assert.Equal(VersionSource.None, d.Source);
        Assert.Null(d.Version);
    }
}
