using RenpyRehost.Core.Net;

namespace RenpyRehost.Core.Media;

/// <summary>
/// Locates (or downloads once, into the tools cache) a static ffmpeg build, and
/// runs the video jobs the asset pipeline needs.
/// </summary>
public sealed class Ffmpeg
{
    // BtbN static Windows build (GitHub CDN — fast). GPL build: libvpx / libopus / x264.
    private const string WindowsBuildUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public string ExePath { get; }

    private Ffmpeg(string exePath) => ExePath = exePath;

    /// <summary>Find ffmpeg on PATH, in the tools cache, or download it. Windows only for the auto-download.</summary>
    public static async Task<Ffmpeg> EnsureAsync(string toolsCacheDir, bool offline, IProgressSink progress, CancellationToken ct)
    {
        string onPath = FindOnPath();
        if (onPath.Length > 0) return new Ffmpeg(onPath);

        string dir = Path.Combine(toolsCacheDir, "ffmpeg");
        string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (File.Exists(exe)) return new Ffmpeg(exe);

        if (offline)
            throw new RehostException("ffmpeg isn't installed or cached and --offline is set. "
                + "Install ffmpeg, or run once online to let it download.");
        if (!OperatingSystem.IsWindows())
            throw new RehostException("ffmpeg not found. Install it (e.g. `apt install ffmpeg` / `brew install ffmpeg`).");

        string zip = Path.Combine(toolsCacheDir, "_dl", "ffmpeg-win64-gpl.zip");
        progress.Info("Downloading ffmpeg (one-time, ~40 MB)...");
        await Downloader.DownloadAsync(WindowsBuildUrl, zip, progress, "ffmpeg.zip", ct: ct).ConfigureAwait(false);

        string staging = Path.Combine(toolsCacheDir, "_dl", "ffmpeg-extract");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        ZipUtil.Extract(zip, staging, stripTopFolder: true, progress, "ffmpeg extract", ct);

        // the build puts binaries in bin/
        string srcExe = Directory.EnumerateFiles(staging, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new RehostException("ffmpeg.exe not found in the downloaded build.");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(srcExe)!))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
        Directory.Delete(staging, recursive: true);

        return new Ffmpeg(exe);
    }

    private static string FindOnPath()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string p = Path.Combine(dir.Trim(), name);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return "";
    }

    /// <summary>Container-only remux (no re-encode) — for VP8/VP9-in-MKV → WebM.</summary>
    public Task<ProcessResult> RemuxToWebmAsync(string src, string dst, CancellationToken ct)
        => Run(new[] { "-y", "-i", src, "-c", "copy", "-f", "webm", dst }, ct);

    /// <summary>Re-encode lossless/uncompressed audio to Opus in an Ogg container.</summary>
    public Task<ProcessResult> ToOpusAsync(string src, string dst, CancellationToken ct)
        => Run(new[] { "-y", "-i", src, "-c:a", "libopus", "-b:a", "128k", "-vbr", "on", "-f", "ogg", dst }, ct);

    /// <summary>Full transcode to VP9 + Opus WebM — for anything not already VP8/VP9.</summary>
    public Task<ProcessResult> TranscodeToWebmAsync(string src, string dst, CancellationToken ct)
        => Run(new[]
        {
            "-y", "-i", src,
            "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "good", "-cpu-used", "3", "-row-mt", "1",
            "-c:a", "libopus", "-b:a", "128k",
            "-f", "webm", dst,
        }, ct);

    /// <summary>Probe a media file's video codec (lowercased), or "" if unknown.</summary>
    public async Task<string> VideoCodecAsync(string src, CancellationToken ct)
    {
        // ffmpeg writes stream info to stderr; -hide_banner keeps it short.
        var r = await Run(new[] { "-hide_banner", "-i", src }, ct, allowFailure: true).ConfigureAwait(false);
        var m = System.Text.RegularExpressions.Regex.Match(r.StdErr, @"Video:\s*([a-z0-9]+)");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
    }

    private async Task<ProcessResult> Run(string[] args, CancellationToken ct, bool allowFailure = false)
    {
        var r = await ProcessRunner.RunAsync(ExePath, args, workingDir: null, env: null, onLine: _ => { }, ct)
            .ConfigureAwait(false);
        if (!r.Ok && !allowFailure)
            throw new RehostException($"ffmpeg failed ({r.ExitCode}): {r.Tail(8)}");
        return r;
    }
}
