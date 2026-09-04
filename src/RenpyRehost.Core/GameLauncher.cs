using System.Diagnostics;

namespace RenpyRehost.Core;

/// <summary>Opens a served web build in a browser.</summary>
public static class GameLauncher
{
    /// <summary>
    /// Set this to a browser executable's path to override everything else —
    /// handy for automation/testing. Takes priority over the remembered
    /// "Choose browser" pick.
    /// </summary>
    public const string PreferredBrowserEnvVar = "RENPY_REHOST_BROWSER";

    /// <summary>
    /// The browser <see cref="Open"/> will use: the env var if set, else the
    /// last browser remembered via <see cref="SetPreferredBrowser"/>, else null
    /// (system default). Falls through silently if a path no longer exists.
    /// </summary>
    public static string? PreferredBrowserExe
    {
        get
        {
            if (Environment.GetEnvironmentVariable(PreferredBrowserEnvVar) is { Length: > 0 } env && File.Exists(env))
                return Path.GetFullPath(env);

            string? remembered = AppSettings.Load().PreferredBrowserExe;
            return remembered is { Length: > 0 } && File.Exists(remembered) ? remembered : null;
        }
    }

    /// <summary>
    /// Remember a browser as the preferred one for <see cref="Open"/> from now
    /// on — this is what "Choose browser" persists. Pass null to go back to the
    /// system default.
    /// </summary>
    public static void SetPreferredBrowser(string? exePath)
    {
        var settings = AppSettings.Load();
        settings.PreferredBrowserExe = exePath is null ? null : Path.GetFullPath(exePath);
        settings.Save();
    }

    /// <summary>Open <paramref name="url"/> — the preferred browser (see <see cref="PreferredBrowserExe"/>) if set, else the system default.</summary>
    public static void Open(string url)
    {
        if (PreferredBrowserExe is { } exe)
        {
            try { OpenWith(url, exe); return; }
            catch { /* fall through to the system default */ }
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Open <paramref name="url"/> with a specific browser executable, one run only — doesn't change the remembered preference.</summary>
    public static void OpenWith(string url, string browserExe) =>
        Process.Start(new ProcessStartInfo(browserExe) { UseShellExecute = false, ArgumentList = { url } });
}
