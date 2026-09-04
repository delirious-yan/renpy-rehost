using System.Text.Json;

namespace RenpyRehost.Core;

/// <summary>
/// Small persisted preferences, shared by the CLI and the GUI. Stored at
/// <c>%LOCALAPPDATA%\RenpyRehost\settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The browser "Play" remembers from the last "Choose browser" pick. Null = system default.</summary>
    public string? PreferredBrowserExe { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenpyRehost", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { /* corrupt / unreadable — start fresh rather than crash */ }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }
}
