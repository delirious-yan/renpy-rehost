using RenpyRehost.Core.Net;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Ensures a matching Ren'Py SDK — with web-support files installed at
/// <c>&lt;sdk&gt;/web/</c> — is unpacked in the cache, downloading from renpy.org
/// if needed. Downloads are cached in <c>&lt;cache&gt;/_dl/</c> and resume.
/// </summary>
public sealed class AcquireSdkStage : IPipelineStage
{
    public string Name => "AcquireSdk";

    public async Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        var version = ctx.Version ?? throw new RehostException("AcquireSdk has no engine version.");
        if (!version.SupportsWebTarget)
            throw new RehostException(
                $"Ren'Py {version} has no Web build target. This game needs a newer SDK and a source "
                + "port (roadmap P4), which isn't built yet.");

        string sdkRoot = Path.Combine(ctx.Options.SdkCacheDir, version.DownloadKey);
        string dlDir = Path.Combine(ctx.Options.SdkCacheDir, "_dl");
        var layout = new SdkLayout(sdkRoot);
        var manifest = SdkManifest.Load().Get(version);
        var notes = new List<string>();

        // ---- SDK ----
        if (!layout.IsUnpacked)
        {
            if (ctx.Options.OfflineOnly)
                throw new RehostException($"SDK {version} not in the cache and --offline is set.");

            string url = $"https://www.renpy.org/dl/{version.DownloadKey}/renpy-{version.DownloadKey}-sdk.zip";
            string zip = Path.Combine(dlDir, $"renpy-{version.DownloadKey}-sdk.zip");

            ctx.Progress.Info($"Downloading Ren'Py {version} SDK...");
            WarnIfUnverified(ctx, manifest?.Sdk, "SDK");
            await Downloader.DownloadAsync(url, zip, ctx.Progress, "sdk.zip",
                manifest?.Sdk?.Sha256, manifest?.Sdk?.Size, ct).ConfigureAwait(false);
            await ReportArtifactAsync(ctx, zip, "sdk.zip", ct);

            ctx.Progress.Info("Extracting SDK...");
            ZipUtil.Extract(zip, sdkRoot, stripTopFolder: true, ctx.Progress, "sdk extract", ct);
            if (!layout.IsUnpacked)
                throw new RehostException($"SDK extracted but renpy.py is missing under {sdkRoot}.");
            notes.Add("SDK downloaded");
        }
        else
        {
            notes.Add("SDK cached");
        }

        // ---- web support ----
        if (!layout.HasWebSupport)
        {
            if (ctx.Options.OfflineOnly)
                throw new RehostException($"Web-support files for {version} not installed and --offline is set.");

            string url = $"https://www.renpy.org/dl/{version.DownloadKey}/renpy-{version.DownloadKey}-web.zip";
            string zip = Path.Combine(dlDir, $"renpy-{version.DownloadKey}-web.zip");

            ctx.Progress.Info("Downloading web-support files...");
            WarnIfUnverified(ctx, manifest?.Web, "web-support");
            await Downloader.DownloadAsync(url, zip, ctx.Progress, "web.zip",
                manifest?.Web?.Sha256, manifest?.Web?.Size, ct).ConfigureAwait(false);
            await ReportArtifactAsync(ctx, zip, "web.zip", ct);

            ctx.Progress.Info("Installing web-support files into the SDK...");
            // The web zip's entries all sit under web/ — keep that folder.
            ZipUtil.Extract(zip, sdkRoot, stripTopFolder: false, ctx.Progress, "web extract", ct);
            if (!layout.HasWebSupport)
                throw new RehostException(
                    $"Web zip extracted but {layout.WebDir}\\hash.txt / index.html are missing.");
            notes.Add("web support installed");
        }
        else
        {
            notes.Add("web support present");
        }

        ctx.SdkDir = sdkRoot;
        ctx.Progress.Detail($"runner: {layout.RenpyRunner}");
        return StageOutcome.Completed(string.Join(", ", notes) + $" — {sdkRoot}");
    }

    private static void WarnIfUnverified(ConversionContext ctx, SdkArtifact? artifact, string what)
    {
        if (artifact?.Size is null && artifact?.Sha256 is null)
            ctx.Progress.Detail($"{what}: no pinned checksum for this version — download will be unverified.");
    }

    private static async Task ReportArtifactAsync(ConversionContext ctx, string path, string label, CancellationToken ct)
    {
        if (!ctx.Options.Verbose) return;
        long size = new FileInfo(path).Length;
        string sha = await Downloader.Sha256Async(path, ct).ConfigureAwait(false);
        ctx.Progress.Detail($"{label}: size {size}  sha256 {sha}");
    }
}
