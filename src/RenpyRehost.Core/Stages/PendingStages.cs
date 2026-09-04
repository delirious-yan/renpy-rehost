using System.Text.Json;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Produces the final serve directory (the web build moved/copied to --out) and
/// writes the <c>rehost.json</c> sidecar describing the build.
/// </summary>
public sealed class AssembleStage : IPipelineStage
{
    public string Name => "Assemble";

    public Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        if (ctx.WebBuildDir is null)
            throw new RehostException("Assemble has no web build to work from.");

        string from = Path.GetFullPath(ctx.WebBuildDir);
        string outDir = Path.GetFullPath(ctx.Options.OutputDir);

        if (!string.Equals(outDir, from, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(outDir)!);

            // Same volume: an instant rename beats copying several GB.
            bool moved = false;
            if (string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(outDir), StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Move(from, outDir); moved = true; }
                catch (IOException) { /* fall back to copy */ }
            }
            if (!moved)
            {
                ctx.Progress.Info($"Copying web build to {outDir}...");
                CopyDir(from, outDir, ct);
            }
            else
            {
                ctx.Progress.Info($"web build → {outDir}");
            }
        }

        WriteSidecar(ctx, outDir);
        ctx.ServeDir = outDir;

        long buildSize = Housekeeping.DirSize(outDir);
        ctx.Notes["size"] = Humanize.Bytes(buildSize);

        // Assemble is the last point the reconstructed project / -dists output is
        // needed. Drop them unless the caller is iterating or asked to keep them.
        if (!ctx.Options.ReuseProject && !ctx.Options.KeepWork)
        {
            long freed = Housekeeping.CleanScratch(ctx);
            if (freed > 0) ctx.Progress.Info($"cleaned {Humanize.Bytes(freed)} of build scratch");
        }

        return Task.FromResult(StageOutcome.Completed(
            $"{Humanize.Bytes(buildSize)} → {outDir}"));
    }

    private static void WriteSidecar(ConversionContext ctx, string dir)
    {
        long size = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { size += new FileInfo(f).Length; } catch { }

        var notes = new List<string>();
        if (ctx.Notes.TryGetValue("assets", out var a)) notes.Add(a);
        if (ctx.Report is { } r)
            foreach (var w in r.Warnings) notes.Add(w.Message);

        var manifest = new
        {
            schema = 1,
            title = ctx.GameName ?? "game",
            sourceVersion = ctx.DetectedVersion?.ToString(),
            builtWith = ctx.Version?.ToString(),
            builtUtc = DateTimeOffset.UtcNow.ToString("O"),
            entry = "index.html",
            sizeBytes = size,
            notes,
        };
        File.WriteAllText(Path.Combine(dir, "rehost.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopyDir(string from, string to, CancellationToken ct)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }
    }
}
