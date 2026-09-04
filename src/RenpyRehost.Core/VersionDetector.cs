using System.Text.RegularExpressions;

namespace RenpyRehost.Core;

/// <summary>Where a detected version came from, most trustworthy first.</summary>
public enum VersionSource
{
    VcVersionPy,        // renpy/vc_version.py  — version_tuple = (8, 2, 3, ...)
    RenpyInitPy,        // renpy/__init__.py    — version_tuple fallback
    LogTxt,             // log.txt              — "Ren'Py 8.2.3.24061702"
    LibRuntimeHint,     // lib/ folder names    — py3 => 7.4+/8.x only, no exact version
    None,
}

public sealed record VersionDetection(RenpyVersion? Version, VersionSource Source, IReadOnlyList<string> Evidence);

/// <summary>
/// Figures out which Ren'Py engine a shipped game was built with, by reading the
/// files that pin it. Mirrors tools/Get-RenpyVersion.ps1. Give it the game's
/// root (the folder holding the .exe and <c>game/</c>) or the <c>game/</c> folder
/// itself — it walks up one level if needed.
/// </summary>
public static class VersionDetector
{
    private static readonly Regex VersionTuple = new(
        @"version_tuple\s*=\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex RenpyBanner = new(
        @"Ren'?Py\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    public static VersionDetection Detect(string path)
    {
        string root = ResolveRoot(path);
        var evidence = new List<string>();

        // 1. renpy/vc_version.py
        if (TryVersionTupleFile(Path.Combine(root, "renpy", "vc_version.py"), out var v, out var line))
        {
            evidence.Add($"renpy/vc_version.py: {line}");
            return new VersionDetection(v, VersionSource.VcVersionPy, evidence);
        }

        // 2. renpy/__init__.py
        if (TryVersionTupleFile(Path.Combine(root, "renpy", "__init__.py"), out v, out line))
        {
            evidence.Add($"renpy/__init__.py: {line}");
            return new VersionDetection(v, VersionSource.RenpyInitPy, evidence);
        }

        // 3. log.txt / errors.txt banners
        foreach (var rel in new[] { "log.txt", "errors.txt", "traceback.txt", Path.Combine("game", "saves", "log.txt") })
        {
            string p = Path.Combine(root, rel);
            if (!File.Exists(p)) continue;
            foreach (var l in ReadLinesSafe(p, 40))
            {
                var m = RenpyBanner.Match(l);
                if (!m.Success) continue;
                evidence.Add($"{rel}: {l.Trim()}");
                return new VersionDetection(RenpyVersion.Parse(m.Groups[1].Value), VersionSource.LogTxt, evidence);
            }
        }

        // 4. lib/ runtime hint — no exact version, just an era
        string lib = Path.Combine(root, "lib");
        if (Directory.Exists(lib))
        {
            var names = Directory.GetDirectories(lib).Select(d => Path.GetFileName(d)!).ToArray();
            if (names.Length > 0)
            {
                evidence.Add("lib/: " + string.Join(", ", names));
                if (names.Any(n => n.Contains("py3"))) evidence.Add("py3 runtime => Ren'Py 7.4+ or 8.x");
                else if (names.Any(n => n.Contains("py2"))) evidence.Add("py2 runtime => Ren'Py 7.3 or older");
                return new VersionDetection(null, VersionSource.LibRuntimeHint, evidence);
            }
        }

        return new VersionDetection(null, VersionSource.None, evidence);
    }

    /// <summary>If <paramref name="path"/> is (or contains) a <c>game/</c> folder, return the folder that holds it.</summary>
    private static string ResolveRoot(string path)
    {
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        path = Path.GetFullPath(path);

        // Given .../MyGame/game -> use .../MyGame
        if (string.Equals(Path.GetFileName(path), "game", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (parent is not null) return parent;
        }
        return path;
    }

    private static bool TryVersionTupleFile(string file, out RenpyVersion? version, out string matchedLine)
    {
        version = null;
        matchedLine = "";
        if (!File.Exists(file)) return false;
        string text;
        try { text = File.ReadAllText(file); }
        catch { return false; }

        var m = VersionTuple.Match(text);
        if (!m.Success) return false;
        version = new RenpyVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        matchedLine = m.Value;
        return true;
    }

    private static IEnumerable<string> ReadLinesSafe(string file, int max)
    {
        List<string> lines = new();
        try
        {
            using var r = new StreamReader(file);
            string? l;
            while (lines.Count < max && (l = r.ReadLine()) is not null)
                lines.Add(l);
        }
        catch { /* unreadable — skip */ }
        return lines;
    }
}
