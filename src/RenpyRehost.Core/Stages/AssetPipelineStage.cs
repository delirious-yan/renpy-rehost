using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using RenpyRehost.Core.Imaging;
using RenpyRehost.Core.Media;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Shrinks and normalises the reconstructed project's assets so the web build
/// succeeds and fits a browser tab. P2. Runs between Reconstruct and Build,
/// editing the clone in place (never the source game).
///
/// Passes: (1) transcode WebP/AVIF to PNG — the web build's placeholder generator
/// (pygame_sdl2) can't read them; (2) downscale oversized PNG/JPG.
/// Audio recompress and video transcode come next.
/// </summary>
public sealed class AssetPipelineStage : IPipelineStage
{
    public string Name => "AssetPipeline";

    public bool ShouldRun(ConversionContext ctx)
        => ctx.Options.AssetPipeline != AssetPipelineMode.Off;

    public async Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        string projDir = ctx.ProjectDir ?? throw new RehostException("AssetPipeline has no project.");
        string game = Path.Combine(projDir, "game");
        var state = PipelineState.Load(projDir);
        var notes = new List<string>();

        if (state.WebpTranscoded) ctx.Progress.Detail("webp transcode already done for this clone — skipping");
        else
        {
            var (transcoded, blanked) = await TranscodeUnreadableImagesAsync(ctx, projDir, game, ct).ConfigureAwait(false);
            if (transcoded > 0) notes.Add($"{transcoded} webp/avif → jpg/png");
            if (blanked > 0) notes.Add($"{blanked} unreadable image(s) blanked");
            state.WebpTranscoded = true; state.Save(projDir);
        }

        if (state.ImagesDownscaled) ctx.Progress.Detail("image downscale already done — skipping");
        else
        {
            long imgSaved = await DownscaleImagesAsync(ctx, game, ct).ConfigureAwait(false);
            if (imgSaved > 0) notes.Add($"images −{Humanize.Bytes(imgSaved)}");
            state.ImagesDownscaled = true; state.Save(projDir);
        }

        if (state.VideoConverted) ctx.Progress.Detail("video conversion already done — skipping");
        else
        {
            int videosDone = await NormaliseVideoAsync(ctx, projDir, game, ct).ConfigureAwait(false);
            if (videosDone > 0) notes.Add($"{videosDone} video(s) → webm");
            state.VideoConverted = true; state.Save(projDir);
        }

        if (state.AudioRecompressed) ctx.Progress.Detail("audio recompress already done — skipping");
        else
        {
            long audioSaved = await RecompressLosslessAudioAsync(ctx, projDir, game, ct).ConfigureAwait(false);
            if (audioSaved > 0) notes.Add($"audio −{Humanize.Bytes(audioSaved)}");
            state.AudioRecompressed = true; state.Save(projDir);
        }

        if (notes.Count == 0)
            return StageOutcome.Completed("no asset changes needed");

        ctx.Notes["assets"] = string.Join(", ", notes);
        return StageOutcome.Completed(ctx.Notes["assets"]);
    }

    // ---- pass 1: WebP/AVIF -> PNG, and fix references ----

    private static async Task<(int transcoded, int blanked)> TranscodeUnreadableImagesAsync(
        ConversionContext ctx, string projDir, string gameDir, CancellationToken ct)
    {
        var targets = Directory.EnumerateFiles(gameDir, "*.*", SearchOption.AllDirectories)
            .Where(f => ImageOps.NeedsTranscodeExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        if (targets.Count == 0) return (0, 0);

        ctx.Progress.Info($"Transcoding {targets.Count} webp/avif image(s) to jpg/png "
            + "(the web build's placeholder tool can't read webp)...");

        int done = 0, failed = 0;
        var failures = new ConcurrentBag<string>();
        // game-relative old path (with ext) -> new game-relative path (with ext)
        var renamed = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            var opts = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
            Parallel.ForEach(targets, opts, f =>
            {
                string oldRel = Rel(gameDir, f);
                try
                {
                    string newFull = ImageOps.TranscodeInPlace(f);
                    renamed[oldRel] = Rel(gameDir, newFull);
                }
                catch (Exception ex)
                {
                    // Corrupt/unreadable source (some shipped archives carry a
                    // zeroed entry). Substitute a tiny transparent PNG.
                    Interlocked.Increment(ref failed);
                    if (failures.Count < 20) failures.Add($"{oldRel}: {ex.Message}");
                    try
                    {
                        string png = Path.ChangeExtension(f, ".png");
                        ImageOps.WriteTinyTransparentPng(png);
                        if (!f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) File.Delete(f);
                        renamed[oldRel] = Rel(gameDir, png);
                    }
                    catch { try { File.Delete(f); } catch { } }
                }
                int d = Interlocked.Increment(ref done);
                if (d % 100 == 0) ctx.Progress.SubProgress("transcode", (double)d / targets.Count);
            });
        }, ct).ConfigureAwait(false);
        ctx.Progress.SubProgress("transcode", 1.0);
        foreach (var e in failures) ctx.Progress.Warn("unreadable image, replaced with a blank: " + e);

        RewritePathReferences(ctx, projDir, renamed);

        return (done - failed, failed);
    }

    private static string Rel(string gameDir, string full)
        => Path.GetRelativePath(gameDir, full).Replace('\\', '/');

    /// <summary>
    /// Rewrite exact quoted <c>"game-relative/path.ext"</c> occurrences in the
    /// project's .rpy source using <paramref name="map"/> (old rel → new rel), and
    /// drop stale .rpyc. Auto-scanned assets (no extension in the ref) don't need
    /// this; explicit paths do.
    /// </summary>
    private static void RewritePathReferences(ConversionContext ctx, string projDir, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return;

        int filesTouched = 0, refsTouched = 0;
        foreach (var rpy in Directory.EnumerateFiles(Path.Combine(projDir, "game"), "*.rpy", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(rpy);
            if (text.IndexOf(".webp", StringComparison.OrdinalIgnoreCase) < 0
                && text.IndexOf(".avif", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string updated = text;
            foreach (var (oldRel, newRel) in map)
            {
                if (string.Equals(oldRel, newRel, StringComparison.Ordinal)) continue;
                foreach (var q in new[] { '"', '\'' })
                {
                    string from = $"{q}{oldRel}{q}";
                    if (updated.Contains(from, StringComparison.OrdinalIgnoreCase))
                    {
                        updated = updated.Replace(from, $"{q}{newRel}{q}", StringComparison.OrdinalIgnoreCase);
                        refsTouched++;
                    }
                }
            }
            if (updated != text)
            {
                File.WriteAllText(rpy, updated);
                string rpyc = Path.ChangeExtension(rpy, ".rpyc");
                if (File.Exists(rpyc)) File.Delete(rpyc);
                filesTouched++;
            }
        }
        if (filesTouched > 0)
            ctx.Progress.Detail($"rewrote {refsTouched} explicit image ref(s) in {filesTouched} .rpy file(s)");
    }

    /// <summary>
    /// Rewrite quoted <c>"...oldExt"</c> paths in the project's .rpy source to
    /// <paramref name="newExt"/>, and drop stale .rpyc so Ren'Py recompiles.
    /// For asset classes where every file gets the same new extension (video).
    /// </summary>
    private static void RewriteExtensionReferences(ConversionContext ctx, string projDir, string newExt, params string[] oldExts)
    {
        var pattern = new Regex(
            "(?<q>['\"])(?<path>[^'\"]+?)(?<ext>" + string.Join("|", oldExts.Select(Regex.Escape)) + ")(?<q2>['\"])",
            RegexOptions.IgnoreCase);

        int filesTouched = 0;
        foreach (var rpy in Directory.EnumerateFiles(Path.Combine(projDir, "game"), "*.rpy", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(rpy);
            string updated = pattern.Replace(text, m => $"{m.Groups["q"].Value}{m.Groups["path"].Value}{newExt}{m.Groups["q2"].Value}");
            if (updated != text)
            {
                File.WriteAllText(rpy, updated);
                string rpyc = Path.ChangeExtension(rpy, ".rpyc");
                if (File.Exists(rpyc)) File.Delete(rpyc);
                filesTouched++;
            }
        }
        if (filesTouched > 0)
            ctx.Progress.Detail($"rewrote {newExt} references in {filesTouched} .rpy file(s)");
    }

    // ---- pass 2: downscale oversized PNG/JPG ----

    private static async Task<long> DownscaleImagesAsync(ConversionContext ctx, string gameDir, CancellationToken ct)
    {
        int maxW = ctx.Options.ImageMaxWidth;
        var candidates = Directory.EnumerateFiles(gameDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
            .Where(f => !IsUnder(gameDir, f, "gui"))
            .ToList();
        if (candidates.Count == 0) return 0;

        ctx.Progress.Info($"Downscaling images over {maxW}px wide ({candidates.Count} to check)...");

        long totalSaved = 0, changed = 0, failed = 0;
        int done = 0;
        var errors = new ConcurrentBag<string>();
        await Task.Run(() =>
        {
            var opts = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
            Parallel.ForEach(candidates, opts, file =>
            {
                try
                {
                    var r = ImageOps.DownscaleInPlace(file, maxW);
                    if (r.Changed)
                    {
                        Interlocked.Add(ref totalSaved, r.BytesBefore - r.BytesAfter);
                        Interlocked.Increment(ref changed);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    if (errors.Count < 5) errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
                int d = Interlocked.Increment(ref done);
                if (d % 200 == 0) ctx.Progress.SubProgress("downscale", (double)d / candidates.Count);
            });
        }, ct).ConfigureAwait(false);
        ctx.Progress.SubProgress("downscale", 1.0);

        foreach (var e in errors) ctx.Progress.Detail("downscale skip " + e);
        ctx.Progress.Info($"downscaled {changed} image(s), saved {Humanize.Bytes(totalSaved)}"
            + (failed > 0 ? $" ({failed} skipped)" : ""));
        return totalSaved;
    }

    // ---- pass 3: video -> WebM ----

    private static readonly string[] VideoExt = { ".mkv", ".avi", ".mov", ".ogv", ".mpg", ".mpeg", ".mp4", ".m4v" };

    private static async Task<int> NormaliseVideoAsync(
        ConversionContext ctx, string projDir, string gameDir, CancellationToken ct)
    {
        var targets = Directory.EnumerateFiles(gameDir, "*.*", SearchOption.AllDirectories)
            .Where(f => VideoExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        if (targets.Count == 0) return 0;

        Ffmpeg ff;
        try
        {
            ff = await Ffmpeg.EnsureAsync(ctx.Options.ToolsCacheDir, ctx.Options.OfflineOnly, ctx.Progress, ct)
                .ConfigureAwait(false);
        }
        catch (RehostException ex)
        {
            ctx.Progress.Warn($"{targets.Count} video(s) left as-is — {ex.Message}");
            return 0;
        }

        ctx.Progress.Info($"Converting {targets.Count} video(s) to WebM "
            + "(VP8/VP9 remuxed, others transcoded)...");

        int done = 0, converted = 0, failed = 0;
        foreach (var src in targets)
        {
            ct.ThrowIfCancellationRequested();
            string dst = Path.ChangeExtension(src, ".webm");
            try
            {
                string codec = await ff.VideoCodecAsync(src, ct).ConfigureAwait(false);
                if (codec is "vp8" or "vp9")
                    await ff.RemuxToWebmAsync(src, dst, ct).ConfigureAwait(false);
                else
                    await ff.TranscodeToWebmAsync(src, dst, ct).ConfigureAwait(false);

                if (File.Exists(dst) && new FileInfo(dst).Length > 0)
                {
                    if (!string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) File.Delete(src);
                    converted++;
                }
                else failed++;
            }
            catch (RehostException ex)
            {
                failed++;
                ctx.Progress.Detail($"video skip {Path.GetFileName(src)}: {ex.Message}");
            }
            if (++done % 10 == 0) ctx.Progress.SubProgress("video", (double)done / targets.Count);
        }
        ctx.Progress.SubProgress("video", 1.0);

        RewriteExtensionReferences(ctx, projDir, ".webm", VideoExt);
        if (failed > 0) ctx.Progress.Warn($"{failed} video(s) couldn't be converted — they may not play.");
        ctx.Progress.Info($"converted {converted} video(s) to WebM");
        return converted;
    }

    private static bool IsUnder(string root, string file, string folder)
        => Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(seg => seg.Equals(folder, StringComparison.OrdinalIgnoreCase));

    // ---- pass 4: lossless audio -> Opus ----
    // MP3/OGG are left alone (already lossy; re-encoding loses quality for little gain).

    private static readonly string[] LosslessAudioExt = { ".wav", ".flac", ".aiff", ".aif" };

    private static async Task<long> RecompressLosslessAudioAsync(
        ConversionContext ctx, string projDir, string gameDir, CancellationToken ct)
    {
        var targets = Directory.EnumerateFiles(gameDir, "*.*", SearchOption.AllDirectories)
            .Where(f => LosslessAudioExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        if (targets.Count == 0) return 0;

        Ffmpeg ff;
        try { ff = await Ffmpeg.EnsureAsync(ctx.Options.ToolsCacheDir, ctx.Options.OfflineOnly, ctx.Progress, ct).ConfigureAwait(false); }
        catch (RehostException) { return 0; }

        ctx.Progress.Info($"Recompressing {targets.Count} lossless audio file(s) to Opus...");
        long saved = 0;
        int done = 0;
        foreach (var src in targets)
        {
            ct.ThrowIfCancellationRequested();
            string dst = Path.ChangeExtension(src, ".opus");
            long before = new FileInfo(src).Length;
            try
            {
                await ff.ToOpusAsync(src, dst, ct).ConfigureAwait(false);
                if (File.Exists(dst) && new FileInfo(dst).Length > 0 && new FileInfo(dst).Length < before)
                {
                    saved += before - new FileInfo(dst).Length;
                    File.Delete(src);
                }
                else if (File.Exists(dst)) File.Delete(dst);
            }
            catch (RehostException) { if (File.Exists(dst)) File.Delete(dst); }
            if (++done % 20 == 0) ctx.Progress.SubProgress("audio", (double)done / targets.Count);
        }
        ctx.Progress.SubProgress("audio", 1.0);
        RewriteExtensionReferences(ctx, projDir, ".opus", LosslessAudioExt);
        return saved;
    }
}
