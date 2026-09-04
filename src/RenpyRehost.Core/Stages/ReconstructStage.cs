using RenpyRehost.Core.Rpa;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Builds a Ren'Py project the SDK can compile: a fresh project folder whose
/// <c>game/</c> is a clone of the shipped one. Script files are copied (the build
/// recompiles them); large media and archives are hard-linked so a multi-GB game
/// doesn't get duplicated on disk.
/// </summary>
public sealed class ReconstructStage : IPipelineStage
{
    public string Name => "Reconstruct";

    // Files the build may rewrite — always a real copy, never a link.
    private static readonly HashSet<string> ScriptExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rpy", ".rpyc", ".rpyb", ".rpym", ".rpymc", ".rpi",
    };

    private const long CopyUnderBytes = 1 * 1024 * 1024;

    public async Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        string gameDir = ctx.GameDir ?? throw new RehostException("Reconstruct has no game folder.");
        string name = Sanitize(ctx.GameName ?? "game");
        string projDir = Path.Combine(ctx.Options.WorkDir, "proj", name);
        string destGame = Path.Combine(projDir, "game");

        if (ctx.Options.ReuseProject
            && Directory.Exists(destGame)
            && Directory.EnumerateFiles(destGame, "*.rpyc", SearchOption.AllDirectories).Any())
        {
            ctx.ProjectDir = projDir;
            return StageOutcome.Skip($"reusing existing project at {projDir}");
        }

        if (Directory.Exists(projDir))
        {
            ctx.Progress.Detail($"clearing previous project at {projDir}");
            Directory.Delete(projDir, recursive: true);
        }
        Directory.CreateDirectory(destGame);

        int copied = 0, linked = 0;
        long copiedBytes = 0, linkedBytes = 0;

        await Task.Run(() =>
        {
            foreach (var src in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string rel = Path.GetRelativePath(gameDir, src);
                string dst = Path.Combine(destGame, rel);
                long len;
                try { len = new FileInfo(src).Length; } catch { len = 0; }

                bool mustCopy = ScriptExt.Contains(Path.GetExtension(src)) || len < CopyUnderBytes;
                if (mustCopy)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, overwrite: true);
                    copied++; copiedBytes += len;
                }
                else
                {
                    bool madeLink = SdkLayout.LinkOrCopy(src, dst);
                    if (madeLink) { linked++; linkedBytes += len; }
                    else { copied++; copiedBytes += len; }
                }

                if ((copied + linked) % 500 == 0)
                    ctx.Progress.SubProgress("clone", 0); // indeterminate tick
            }
        }, ct).ConfigureAwait(false);

        ctx.Progress.Info($"cloned game/: {copied} files copied ({Humanize.Bytes(copiedBytes)}), "
                        + $"{linked} hard-linked ({Humanize.Bytes(linkedBytes)})");

        // Unpack any .rpa archives into loose files. A shipped archive is included
        // in the web build *whole* otherwise — the progressive-download repack can
        // only see (and stream) individual images/audio when they sit loose.
        var archives = Directory.GetFiles(destGame, "*.rpa", SearchOption.AllDirectories);
        long unpacked = 0;
        foreach (var rpa in archives)
        {
            ct.ThrowIfCancellationRequested();
            long rpaSize = new FileInfo(rpa).Length;
            ctx.Progress.Info($"Unpacking {Path.GetFileName(rpa)} ({Humanize.Bytes(rpaSize)}) — needs that much free disk...");
            try
            {
                using var archive = RpaArchive.Open(rpa);
                ctx.Progress.Detail($"RPA-{archive.Version}, {archive.Entries.Count} entries");
                int wrote = archive.ExtractAll(destGame, ctx.Progress, $"unpack {Path.GetFileName(rpa)}", ct);
                ctx.Progress.Detail($"{wrote} files written ({archive.Entries.Count - wrote} already present loose)");
            }
            catch (RehostException ex)
            {
                throw new RehostException(
                    $"Couldn't unpack {Path.GetFileName(rpa)}: {ex.Message} "
                    + "If the archive uses a custom key, this game can't be converted automatically.");
            }
            File.Delete(rpa); // the project's copy/link only — the source game is untouched
            unpacked += rpaSize;
        }

        if (!File.Exists(Path.Combine(destGame, "options.rpy")) &&
            !File.Exists(Path.Combine(destGame, "options.rpyc")))
            ctx.Progress.Warn("No options.rpy in the game — the SDK will use build defaults.");

        // Deliberately NOT writing progressive_download.txt: Ren'Py 7.4's default
        // rules (all game/ images + game/audio music become progressive) are
        // exactly right for a large game. P2 will tune this per preflight.

        ctx.ProjectDir = projDir;
        string cloneNote = $"{copied} copied ({Humanize.Bytes(copiedBytes)}), "
                         + $"{linked} hard-linked ({Humanize.Bytes(linkedBytes)})";
        ctx.Notes["reconstruct"] = archives.Length > 0
            ? cloneNote + $"; unpacked {archives.Length} archive(s) ({Humanize.Bytes(unpacked)})"
            : cloneNote;
        return StageOutcome.Completed(ctx.Notes["reconstruct"] + $" — {projDir}");
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().Length == 0 ? "game" : name.Trim();
    }
}
