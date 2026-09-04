using System.Diagnostics;

namespace RenpyRehost.Core;

/// <summary>Opens a served web build in a browser.</summary>
public static class GameLauncher
{
    /// <summary>
    /// Point this at any browser executable to make <see cref="Open"/> prefer it
    /// over the system default — a portable browser, a specific profile, whatever
    /// you'd rather games opened in. Falls back to the system default if the path
    /// is unset or no longer exists.
    /// </summary>
    public const string PreferredBrowserEnvVar = "RENPY_REHOST_BROWSER";

    public static string? PreferredBrowserExe =>
        Environment.GetEnvironmentVariable(PreferredBrowserEnvVar) is { Length: > 0 } p && File.Exists(p)
            ? Path.GetFullPath(p)
            : null;

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

    /// <summary>Open <paramref name="url"/> with a specific browser executable.</summary>
    public static void OpenWith(string url, string browserExe) =>
        Process.Start(new ProcessStartInfo(browserExe) { UseShellExecute = false, ArgumentList = { url } });
}
