using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Round_trips_the_preferred_browser_through_disk()
    {
        using var g = GameFixture.Create();
        string path = g.At("settings.json");

        var settings = AppSettings.Load(path);
        Assert.Null(settings.PreferredBrowserExe);

        settings.PreferredBrowserExe = @"C:\Browsers\thing.exe";
        settings.Save(path);

        var reloaded = AppSettings.Load(path);
        Assert.Equal(@"C:\Browsers\thing.exe", reloaded.PreferredBrowserExe);
    }

    [Fact]
    public void Load_returns_defaults_for_a_missing_or_corrupt_file()
    {
        using var g = GameFixture.Create();
        Assert.Null(AppSettings.Load(g.At("nope.json")).PreferredBrowserExe);

        string bad = g.At("bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.Null(AppSettings.Load(bad).PreferredBrowserExe);
    }

    [Fact]
    public void Save_can_clear_the_preference_back_to_null()
    {
        using var g = GameFixture.Create();
        string path = g.At("settings.json");

        var settings = new AppSettings { PreferredBrowserExe = @"C:\x.exe" };
        settings.Save(path);

        var reloaded = AppSettings.Load(path);
        reloaded.PreferredBrowserExe = null;
        reloaded.Save(path);

        Assert.Null(AppSettings.Load(path).PreferredBrowserExe);
    }
}
