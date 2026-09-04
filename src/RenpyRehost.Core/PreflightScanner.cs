using System.Text;
using System.Text.RegularExpressions;
using RenpyRehost.Core.Rpa;

namespace RenpyRehost.Core;

/// <summary>
/// The deeper preflight checks — the ones that decide whether a game *can* be
/// converted at all: readable archives, non-obfuscated bytecode, no native code,
/// no OS-reaching script calls.
/// </summary>
public static class PreflightScanner
{
    private static readonly byte[] RpycMagic = "RENPY RPC2"u8.ToArray();

    private static readonly (string code, Regex rx, string what)[] ApiChecks =
    {
        ("subprocess", new Regex(@"\bsubprocess\b", RegexOptions.Compiled), "spawns processes"),
        ("os-system",  new Regex(@"os\.(system|popen|startfile|exec)", RegexOptions.Compiled), "runs shell commands"),
        ("renpy-run",  new Regex(@"renpy\.run\s*\(", RegexOptions.Compiled), "uses renpy.run (opens files/URLs)"),
        ("ctypes",     new Regex(@"\bimport\s+ctypes\b|\bfrom\s+ctypes\b", RegexOptions.Compiled), "calls into native libraries via ctypes"),
        ("network",    new Regex(@"\bimport\s+(requests|urllib|http\.client|socket)\b", RegexOptions.Compiled), "makes network calls"),
        ("file-write", new Regex(@"open\s*\([^)]*['""][wa]\+?b?['""]", RegexOptions.Compiled), "writes arbitrary files"),
    };

    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".webp", ".avif", ".bmp", ".gif" };
    private static readonly string[] AudioExt = { ".ogg", ".opus", ".mp3", ".wav", ".flac", ".m4a", ".aac" };
    private static readonly string[] VideoExt = { ".webm", ".mp4", ".mkv", ".avi", ".mov", ".ogv" };

    public static void Scan(string gameDir, string? sdkRootHint, ConversionReport report, CancellationToken ct)
    {
        ScanArchives(gameDir, report, ct);
        ScanBytecode(gameDir, report, ct);
        ScanNativeModules(gameDir, report);
        ScanScriptApis(gameDir, report, ct);
    }

    private static void ScanArchives(string gameDir, ConversionReport report, CancellationToken ct)
    {
        foreach (var rpa in Directory.EnumerateFiles(gameDir, "*.rpa", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var a = RpaArchive.Open(rpa);
                long fileLen = new FileInfo(rpa).Length;
                bool sane = a.Entries.Count > 0 && a.Entries.All(e => e.Offset >= 0 && e.Offset < fileLen && e.Length >= 0);
                if (!sane)
                {
                    report.Blockers.Add(new Finding("keyed-archive",
                        $"{Path.GetFileName(rpa)}: archive index points outside the file — a custom obfuscation key. Can't unpack.",
                        FindingSeverity.Blocker));
                    continue;
                }

                // Fold the archived files into the size profile by type. ProfileSize
                // counts the .rpa's bytes only toward Total, leaving the breakdown to us.
                foreach (var e in a.Entries)
                {
                    string ext = Path.GetExtension(e.Path).ToLowerInvariant();
                    if (ImageExt.Contains(ext)) report.Size.Images += e.Length;
                    else if (AudioExt.Contains(ext)) report.Size.Audio += e.Length;
                    else if (VideoExt.Contains(ext)) report.Size.Video += e.Length;
                    else report.Size.Other += e.Length;
                }
            }
            catch (Exception ex)
            {
                report.Blockers.Add(new Finding("keyed-archive",
                    $"{Path.GetFileName(rpa)}: can't read the archive ({ex.Message}). Likely a custom key or a modified Ren'Py.",
                    FindingSeverity.Blocker));
            }
        }
    }

    private static void ScanBytecode(string gameDir, ConversionReport report, CancellationToken ct)
    {
        var rpyc = Directory.EnumerateFiles(gameDir, "*.rpyc", SearchOption.AllDirectories).Take(400).ToList();
        var rpy = new HashSet<string>(
            Directory.EnumerateFiles(gameDir, "*.rpy", SearchOption.AllDirectories)
                .Select(p => Path.ChangeExtension(p, null)!), StringComparer.OrdinalIgnoreCase);
        if (rpyc.Count == 0) return;

        int badMagic = 0, checkedCount = 0;
        foreach (var f in rpyc.Take(20))
        {
            ct.ThrowIfCancellationRequested();
            checkedCount++;
            try
            {
                var head = new byte[RpycMagic.Length];
                using var fs = File.OpenRead(f);
                if (fs.Read(head, 0, head.Length) < head.Length || !head.SequenceEqual(RpycMagic))
                    badMagic++;
            }
            catch { badMagic++; }
        }

        double sourceCoverage = rpyc.Count == 0 ? 1 : rpy.Count / (double)Math.Max(rpyc.Count, rpy.Count);

        if (badMagic > checkedCount / 2 && sourceCoverage < 0.5)
            report.Blockers.Add(new Finding("obfuscated-bytecode",
                "Most .rpyc files don't have the Ren'Py bytecode signature and there's little/no .rpy source. "
                + "This game's scripts are obfuscated — can't rebuild it.",
                FindingSeverity.Blocker));
        else if (sourceCoverage < 0.15)
            report.Warnings.Add(new Finding("no-source",
                "The game ships almost no .rpy source — the rebuild relies entirely on version-matched bytecode. "
                + "Get the SDK version exactly right.",
                FindingSeverity.Warning));
    }

    private static void ScanNativeModules(string gameDir, ConversionReport report)
    {
        var native = Directory.EnumerateFiles(gameDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".pyd" or ".so")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Take(10)
            .ToList();
        if (native.Count > 0)
            report.Warnings.Add(new Finding("native-module",
                $"Bundled native modules ({string.Join(", ", native)}) — these don't exist in the web runtime; "
                + "anything that imports them will fail at runtime.",
                FindingSeverity.Warning));
    }

    private static void ScanScriptApis(string gameDir, ConversionReport report, CancellationToken ct)
    {
        var seen = new HashSet<string>();
        int scanned = 0;
        foreach (var f in Directory.EnumerateFiles(gameDir, "*.rpy", SearchOption.AllDirectories))
        {
            if (scanned++ > 2000) break;
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = File.ReadAllText(f); } catch { continue; }
            foreach (var (code, rx, what) in ApiChecks)
            {
                if (seen.Contains(code)) continue;
                if (!rx.IsMatch(text)) continue;
                seen.Add(code);
                report.Warnings.Add(new Finding($"api-{code}",
                    $"Script {what} (found in {Path.GetFileName(f)}) — that won't work in a browser; "
                    + "usually harmless (a fallback or a rare path), but check.",
                    FindingSeverity.Info));
            }
        }
    }
}
