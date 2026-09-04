namespace RenpyRehost.Core.Stages;

/// <summary>
/// Scans the game for anything that blocks or complicates conversion and
/// produces a <see cref="ConversionReport"/>. P0 does the cheap structural
/// checks (web-target support, size profile); the deeper scans — keyed archives,
/// obfuscated bytecode, native modules, problem API calls — land in P3.
/// </summary>
public sealed class PreflightStage : IPipelineStage
{
    public string Name => "Preflight";

    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".webp", ".avif", ".bmp" };
    private static readonly string[] AudioExt = { ".ogg", ".opus", ".mp3", ".wav", ".flac", ".m4a" };
    private static readonly string[] VideoExt = { ".webm", ".mp4", ".mkv", ".avi", ".mov", ".ogv" };

    public Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        if (ctx.GameDir is null || ctx.Version is null)
            throw new RehostException("Preflight needs a resolved game folder and version.");

        var report = new ConversionReport
        {
            EngineVersion = ctx.Version,
            WebTargetNative = ctx.Version.SupportsWebTarget,
        };

        ProfileSize(ctx.GameDir, report.Size, ct);
        PreflightScanner.Scan(ctx.GameDir, ctx.SdkDir, report, ct);

        long gb = 1024L * 1024 * 1024;
        if (report.Size.Total > 2 * gb)
            report.Warnings.Add(new Finding("large-game",
                $"{Humanize.Bytes(report.Size.Total)} of assets — the asset pipeline will run to keep the web build tab-sized.",
                FindingSeverity.Warning));
        if (report.Size.Video > 0)
            report.Warnings.Add(new Finding("has-video",
                "Contains video — non-VP8/VP9 will be transcoded to WebM; anything ffmpeg can't handle won't play.",
                FindingSeverity.Info));

        // ---- recommendation ----
        if (report.HasBlockers)
            report.Recommendation = Recommendation.Stop;
        else if (!report.WebTargetNative)
        {
            report.Warnings.Add(new Finding("no-web-target",
                $"Ren'Py {ctx.Version} has no native Web build target; needs the decompile-and-port path (not built yet). "
                + "Try --renpy-version 8.3.7 to rebuild on a newer SDK.",
                FindingSeverity.Warning));
            report.Recommendation = Recommendation.NeedsPortForward;
        }
        else
        {
            bool needsPipeline = report.Size.Total > 2 * gb
                || report.Size.Video > 0
                || report.Warnings.Any(w => w.Code is "native-module" or "no-source");
            report.Recommendation = needsPipeline ? Recommendation.GoWithPipeline : Recommendation.Go;
        }

        ctx.Report = report;

        if (report.HasBlockers && !ctx.Options.Force)
            throw new RehostException(
                "can't convert this game:\n" + string.Join("\n", report.Blockers.Select(b => $"  • {b.Message}"))
                + "\n(pass --force to try anyway)");

        string verdict = report.Recommendation switch
        {
            Recommendation.Go => "Go",
            Recommendation.GoWithPipeline => "Go (needs asset pipeline)",
            Recommendation.NeedsPortForward => "Needs port-forward",
            Recommendation.Stop => "Stop",
            _ => "?",
        };
        return Task.FromResult(StageOutcome.Completed(
            $"{verdict} — {report.Warnings.Count} warning(s), {report.Blockers.Count} blocker(s)"));
    }

    private static void ProfileSize(string gameDir, SizeProfile size, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            long len;
            try { len = new FileInfo(file).Length; } catch { continue; }
            size.Total += len;

            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".rpa")
            {
                size.Archives[Path.GetFileName(file)] = len;
                continue; // PreflightScanner breaks the .rpa contents down by type
            }
            if (ImageExt.Contains(ext)) size.Images += len;
            else if (AudioExt.Contains(ext)) size.Audio += len;
            else if (VideoExt.Contains(ext)) size.Video += len;
            else size.Other += len;
        }
    }
}
