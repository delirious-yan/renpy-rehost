using System.IO.Compression;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Resolves what the user dropped down to the game's <c>game/</c> folder.
/// Accepts: a game root folder, a bare <c>game/</c> folder, a folder one level
/// above the game, a <c>.zip</c> of any of those, or a shipped <c>.exe</c>
/// (Ren'Py Windows builds ship the game beside the exe, not as an installer).
/// </summary>
public sealed class IngestStage : IPipelineStage
{
    public string Name => "Ingest";

    public async Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        string source = Path.GetFullPath(ctx.SourcePath);
        string searchRoot;

        if (Directory.Exists(source))
        {
            searchRoot = source;
        }
        else if (File.Exists(source))
        {
            string ext = Path.GetExtension(source).ToLowerInvariant();
            searchRoot = ext switch
            {
                ".zip" => await ExtractZipAsync(ctx, source, ct),
                ".exe" => Path.GetDirectoryName(source)!,
                _ => throw new RehostException(
                    $"Don't know how to open '{Path.GetFileName(source)}'. Drop the extracted game folder, "
                    + "a .zip of it, or the game's .exe."),
            };
        }
        else
        {
            throw new RehostException($"Path not found: {source}");
        }

        string gameDir = FindGameDir(searchRoot)
            ?? throw new RehostException(
                $"No Ren'Py 'game' folder (with .rpyc/.rpa files) found under {searchRoot}.");

        ctx.GameDir = gameDir;
        ctx.GameName = string.IsNullOrWhiteSpace(ctx.Options.Title)
            ? DeriveName(gameDir, source)
            : ctx.Options.Title!.Trim();

        return StageOutcome.Completed($"game folder: {gameDir}");
    }

    /// <summary>
    /// Locate the <c>game/</c> folder. Prefer a real <c>game/</c> subfolder over
    /// the folder that holds it; check the root, then <c>root/game</c>, then one
    /// level down, and only fall back to the root itself for a loose game.
    /// </summary>
    private static string? FindGameDir(string root)
    {
        if (IsNamedGame(root) && IsGameFolder(root)) return root;

        string direct = Path.Combine(root, "game");
        if (Directory.Exists(direct) && IsGameFolder(direct)) return direct;

        foreach (var sub in SafeSubdirs(root))
        {
            string nested = Path.Combine(sub, "game");
            if (Directory.Exists(nested) && IsGameFolder(nested)) return nested;
            if (IsNamedGame(sub) && IsGameFolder(sub)) return sub;
        }

        // loose game with no wrapper folder
        return IsGameFolder(root) ? root : null;
    }

    private static bool IsNamedGame(string dir)
        => Path.GetFileName(dir).Equals("game", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A Ren'Py <c>game/</c> folder has top-level bytecode (script.rpyc,
    /// options.rpyc, …) or top-level <c>.rpa</c> archives — everything else lives
    /// inside those.
    /// </summary>
    private static bool IsGameFolder(string dir)
    {
        try
        {
            if (Directory.EnumerateFiles(dir, "*.rpyc", SearchOption.TopDirectoryOnly).Any()) return true;
            if (Directory.EnumerateFiles(dir, "*.rpa", SearchOption.TopDirectoryOnly).Any()) return true;
            if (File.Exists(Path.Combine(dir, "script_version.txt"))) return true;
        }
        catch { /* unreadable */ }
        return false;
    }

    private static IEnumerable<string> SafeSubdirs(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch { return Array.Empty<string>(); }
    }

    private static async Task<string> ExtractZipAsync(ConversionContext ctx, string zipPath, CancellationToken ct)
    {
        string dest = Path.Combine(ctx.Options.WorkDir, "ingest",
            Path.GetFileNameWithoutExtension(zipPath));
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        Directory.CreateDirectory(dest);

        ctx.Progress.Info($"Extracting {Path.GetFileName(zipPath)}...");
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            int done = 0, total = archive.Entries.Count;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string target = Path.GetFullPath(Path.Combine(dest, entry.FullName));
                if (!target.StartsWith(dest, StringComparison.Ordinal))
                    throw new RehostException($"Refusing zip entry outside the extract folder: {entry.FullName}");
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
                if (++done % 200 == 0) ctx.Progress.SubProgress("extract", (double)done / total);
            }
        }, ct).ConfigureAwait(false);

        return dest;
    }

    private static string DeriveName(string gameDir, string source)
    {
        // .../MyGame-1.2-pc/game  ->  "MyGame"
        string candidate = Path.GetFileName(Path.GetDirectoryName(gameDir)!);
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Equals("game", StringComparison.OrdinalIgnoreCase))
            candidate = Path.GetFileNameWithoutExtension(source);

        foreach (var tag in new[] { "-pc", "-market", "-win", "-linux", "-mac", "-all" })
            if (candidate.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
                candidate = candidate[..^tag.Length];

        // strip a trailing version like "-1.2.3" or "_v0.9"
        candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"[-_ ]v?\d+(\.\d+)*$", "");
        candidate = candidate.Replace('_', ' ').Replace('-', ' ').Trim();

        // split CamelCase ("SomeCoolGame" -> "Some Cool Game"), but only when
        // there were no separators to begin with
        if (!candidate.Contains(' '))
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"(?<=[a-z0-9])(?=[A-Z])", " ");

        return string.IsNullOrWhiteSpace(candidate) ? "game" : candidate;
    }
}
