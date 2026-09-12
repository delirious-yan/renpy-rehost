namespace RenpyRehost.Core;

/// <summary>
/// Always-on file logging so a failed convert / move / clean / whatever leaves a
/// trail to troubleshoot from, even when the console wasn't run with <c>-v</c>.
/// One plain-text file per operation, timestamped lines, at
/// <c>%LOCALAPPDATA%\RenpyRehost\logs\</c>. Never throws — a logging failure must
/// not take down the operation it's trying to record.
/// </summary>
public sealed class Log : IDisposable
{
    public string Path { get; }
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    private Log(string path, StreamWriter? writer)
    {
        Path = path;
        _writer = writer;
    }

    public static string LogDir(string? appRoot = null) => System.IO.Path.Combine(
        appRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\RenpyRehost",
        "logs");

    /// <summary>
    /// Start a new log file for one operation (e.g. "convert-mygame", "move",
    /// "clean"). Prunes old log files so the folder doesn't grow forever.
    /// </summary>
    public static Log Start(string operation, string? appRoot = null, int keep = 50)
    {
        string dir = LogDir(appRoot);
        try
        {
            Directory.CreateDirectory(dir);

            string safe = Sanitize(operation);
            string file = System.IO.Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{safe}.log");
            // A BOM so Notepad / PowerShell's Get-Content (which otherwise guesses the
            // system codepage for a BOM-less file) both read it back correctly.
            var writer = new StreamWriter(file, append: false, new System.Text.UTF8Encoding(true)) { AutoFlush = true };
            var log = new Log(file, writer);
            log.WriteLine($"=== {operation} — {DateTimeOffset.Now:u} ===");
            log.WriteLine($"rehost {typeof(Log).Assembly.GetName().Version?.ToString(3)} on {Environment.OSVersion}");

            // Prune after writing, not before — so "keep" is the total left
            // standing (this file included), not the count before it existed.
            Prune(dir, keep);
            return log;
        }
        catch
        {
            // Can't write logs (locked-down folder, disk full, ...) — the operation
            // itself must still proceed, just without a trail this time.
            return new Log(System.IO.Path.Combine(dir, $"{Sanitize(operation)}.log"), null);
        }
    }

    public void WriteLine(string line)
    {
        if (_writer is null) return;
        lock (_gate)
        {
            try { _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}"); }
            catch { /* best effort */ }
        }
    }

    /// <summary>Log a caught exception with full detail — type, message, stack trace, and any inner exceptions.</summary>
    public void Exception(string context, Exception? ex)
    {
        WriteLine($"ERROR — {context}");
        for (var e = ex; e is not null; e = e.InnerException)
        {
            WriteLine($"  {e.GetType().FullName}: {e.Message}");
            foreach (var line in (e.StackTrace ?? "").Split('\n'))
                if (line.Trim().Length > 0) WriteLine($"    {line.TrimEnd()}");
        }
    }

    public void Dispose()
    {
        if (_writer is null) return;
        try { WriteLine("=== done ==="); _writer.Dispose(); } catch { }
    }

    private static string Sanitize(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars);
    }

    private static void Prune(string dir, int keep)
    {
        try
        {
            var stale = Directory.GetFiles(dir, "*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(0, keep));
            foreach (var f in stale) { try { f.Delete(); } catch { } }
        }
        catch { }
    }
}
