using System.Text.Json;

namespace RenpyRehost.Core;

/// <summary>One converted web build the user keeps around to replay.</summary>
public sealed class LibraryEntry
{
    public required string Title { get; set; }

    /// <summary>Absolute path to the web build folder (the one with <c>index.html</c>).</summary>
    public required string Path { get; set; }

    public string? SourceVersion { get; set; }
    public string? BuiltWith { get; set; }
    public string? BuiltUtc { get; set; }
    public long SizeBytes { get; set; }
    public string? LastPlayedUtc { get; set; }

    /// <summary>Where the game was converted from (folder or .exe), if known — lets "move next to the game" work.</summary>
    public string? SourcePath { get; set; }

    /// <summary>The build folder still exists and looks like a web build.</summary>
    public bool Exists => Directory.Exists(Path) && File.Exists(System.IO.Path.Combine(Path, "index.html"));
}

/// <summary>
/// A small on-disk list of converted builds, shown in the GUI so games are one
/// click to replay. Stored at <c>%LOCALAPPDATA%\RenpyRehost\library.json</c>.
/// </summary>
public sealed class Library
{
    public List<LibraryEntry> Entries { get; set; } = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenpyRehost", "library.json");

    /// <summary>The managed spot for builds: <c>%LOCALAPPDATA%\RenpyRehost\out</c> (off OneDrive).</summary>
    public static string AppOutDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenpyRehost", "out");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Library Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Library>(File.ReadAllText(path)) ?? new Library();
        }
        catch { /* corrupt / unreadable — start fresh */ }
        return new Library();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>
    /// Add (or refresh) an entry for a web build folder, reading its
    /// <c>rehost.json</c> sidecar when present. Returns the entry.
    /// </summary>
    public LibraryEntry AddOrUpdate(string buildDir, string? sourcePath = null)
    {
        buildDir = Path.GetFullPath(buildDir);
        if (!File.Exists(Path.Combine(buildDir, "index.html")))
            throw new RehostException($"{buildDir} doesn't look like a web build (no index.html).");

        var sidecar = ReadSidecar(buildDir);
        long size = sidecar?.SizeBytes ?? DirSize(buildDir);

        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.Path, buildDir, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new LibraryEntry
            {
                Title = sidecar?.Title ?? Path.GetFileName(buildDir),
                Path = buildDir,
            };
            Entries.Add(existing);
        }

        existing.Title = sidecar?.Title ?? existing.Title;
        existing.SourceVersion = sidecar?.SourceVersion ?? existing.SourceVersion;
        existing.BuiltWith = sidecar?.BuiltWith ?? existing.BuiltWith;
        existing.BuiltUtc = sidecar?.BuiltUtc ?? existing.BuiltUtc;
        existing.SizeBytes = size;
        if (!string.IsNullOrWhiteSpace(sourcePath))
            existing.SourcePath = Path.GetFullPath(sourcePath);
        return existing;
    }

    /// <summary>Where a move is up to: a 0..1 fraction and the byte counts behind it.</summary>
    public readonly record struct MoveProgress(double Fraction, long BytesDone, long BytesTotal)
    {
        /// <summary>A same-volume rename — instant, no byte-by-byte copy.</summary>
        public bool Instant => BytesTotal == 0;
    }

    /// <summary>
    /// Move a build folder into <paramref name="destParent"/> and repoint its entry.
    /// Returns the new build path. Same-volume moves are a rename (one
    /// <see cref="MoveProgress"/> tick with <see cref="MoveProgress.Instant"/>); a
    /// cross-volume move copies file-by-file and reports progress as it goes, then
    /// deletes the original. The caller saves.
    /// </summary>
    public string Move(string buildDir, string destParent, IProgress<MoveProgress>? progress = null)
    {
        buildDir = Path.GetFullPath(buildDir);
        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.Path, buildDir, StringComparison.OrdinalIgnoreCase))
            ?? throw new RehostException($"{buildDir} isn't in the library.");
        if (!Directory.Exists(buildDir))
            throw new RehostException($"{buildDir} is gone — nothing to move.");

        destParent = Path.GetFullPath(destParent);
        string dest = Path.Combine(destParent, Path.GetFileName(buildDir));
        if (string.Equals(dest, buildDir, StringComparison.OrdinalIgnoreCase))
            return buildDir; // already there

        if (Directory.Exists(dest) || File.Exists(dest))
            throw new RehostException($"{dest} already exists — remove or rename it first.");
        if (IsInside(destParent, buildDir))
            throw new RehostException("Can't move a build inside itself.");

        Directory.CreateDirectory(destParent);
        MoveDir(buildDir, dest, progress);
        entry.Path = dest;
        return dest;
    }

    private static bool IsInside(string child, string parent)
    {
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(child).StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveDir(string source, string dest, IProgress<MoveProgress>? progress)
    {
        bool sameVolume = string.Equals(
            Path.GetPathRoot(Path.GetFullPath(source)),
            Path.GetPathRoot(Path.GetFullPath(dest)),
            StringComparison.OrdinalIgnoreCase);

        if (sameVolume)
        {
            Directory.Move(source, dest);
            progress?.Report(new MoveProgress(1.0, 0, 0));
            return;
        }

        // Cross-volume: Directory.Move won't; copy the tree then remove the original.
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        long total = 0;
        foreach (var f in files) { try { total += new FileInfo(f).Length; } catch { } }

        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));

        long done = 0;
        double lastReported = -1;
        progress?.Report(new MoveProgress(0, 0, total));
        foreach (var file in files)
        {
            string target = Path.Combine(dest, Path.GetRelativePath(source, file));
            CopyFile(file, target, ref done, total, progress, ref lastReported);
        }
        progress?.Report(new MoveProgress(1.0, total, total));

        Directory.Delete(source, recursive: true);
    }

    private static void CopyFile(string src, string dst, ref long done, long total,
        IProgress<MoveProgress>? progress, ref double lastReported)
    {
        const int bufSize = 1 << 20; // 1 MiB
        var buffer = new byte[bufSize];
        using var input = File.OpenRead(src);
        using var output = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufSize);
        int n;
        while ((n = input.Read(buffer, 0, bufSize)) > 0)
        {
            output.Write(buffer, 0, n);
            done += n;
            if (progress is null || total == 0) continue;
            double frac = (double)done / total;
            if (frac - lastReported >= 0.002 || frac >= 1.0) // ~0.2% steps — smooth but not chatty
            {
                lastReported = frac;
                progress.Report(new MoveProgress(frac, done, total));
            }
        }
    }

    public bool Remove(string buildDir)
    {
        buildDir = Path.GetFullPath(buildDir);
        return Entries.RemoveAll(e =>
            string.Equals(e.Path, buildDir, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public void MarkPlayed(string buildDir)
    {
        var e = Entries.FirstOrDefault(x =>
            string.Equals(x.Path, Path.GetFullPath(buildDir), StringComparison.OrdinalIgnoreCase));
        if (e is not null) e.LastPlayedUtc = DateTimeOffset.UtcNow.ToString("O");
    }

    private sealed record Sidecar(string? Title, string? SourceVersion, string? BuiltWith, string? BuiltUtc, long SizeBytes);

    private static Sidecar? ReadSidecar(string buildDir)
    {
        string p = Path.Combine(buildDir, "rehost.json");
        if (!File.Exists(p)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(p));
            var r = doc.RootElement;
            return new Sidecar(
                Str(r, "title"), Str(r, "sourceVersion"), Str(r, "builtWith"), Str(r, "builtUtc"),
                r.TryGetProperty("sizeBytes", out var s) && s.TryGetInt64(out var v) ? v : 0);
        }
        catch { return null; }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static long DirSize(string dir)
    {
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { total += new FileInfo(f).Length; } catch { }
        return total;
    }
}
