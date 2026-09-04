using Microsoft.Win32;

namespace RenpyRehost.App;

/// <summary>Finds browsers installed on this machine, for the "choose a browser" picker.</summary>
public static class BrowserCatalog
{
    public readonly record struct Browser(string Name, string ExePath);

    /// <summary>
    /// Reads the registry's <c>StartMenuInternet</c> client list (the same place
    /// Windows' own "choose default browser" picker reads from), user-scope first.
    /// </summary>
    public static IReadOnlyList<Browser> Installed()
    {
        var found = new Dictionary<string, Browser>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, subKey) in new[]
        {
            (Registry.CurrentUser, @"SOFTWARE\Clients\StartMenuInternet"),
            (Registry.LocalMachine, @"SOFTWARE\Clients\StartMenuInternet"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"),
        })
        {
            try
            {
                using var root = hive.OpenSubKey(subKey);
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name);
                    using var cmd = key?.OpenSubKey(@"shell\open\command");
                    string? exe = ParseExePath(cmd?.GetValue(null) as string);
                    if (exe is null || !File.Exists(exe)) continue;

                    string display = key?.GetValue(null) as string ?? name;
                    found.TryAdd(exe, new Browser(display, exe)); // first hit (user scope) wins
                }
            }
            catch { /* a locked/missing key just means fewer entries */ }
        }
        return found.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ParseExePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        commandLine = commandLine.Trim();
        if (commandLine.StartsWith('"'))
        {
            int end = commandLine.IndexOf('"', 1);
            return end > 0 ? commandLine[1..end] : null;
        }
        int space = commandLine.IndexOf(' ');
        return space > 0 ? commandLine[..space] : commandLine;
    }
}
