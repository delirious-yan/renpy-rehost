using System.Security.Cryptography;

namespace RenpyRehost.Core.Net;

/// <summary>Streamed HTTP download to a file, with resume, progress, and optional SHA-256 verification.</summary>
public static class Downloader
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        // large SDK zips — allow a long overall transfer, but fail a stalled read
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "RenpyRehost/0.0.1 (+local converter)" } },
    };

    /// <summary>
    /// Download <paramref name="url"/> to <paramref name="destPath"/>. Uses a
    /// <c>.part</c> file and HTTP Range to resume an interrupted transfer. If the
    /// final file already exists (and matches <paramref name="expectedSha256"/>
    /// when given), does nothing.
    /// </summary>
    public static async Task DownloadAsync(
        string url, string destPath, IProgressSink progress, string label,
        string? expectedSha256 = null, long? expectedSize = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        if (File.Exists(destPath))
        {
            if (expectedSha256 is null || await MatchesHashAsync(destPath, expectedSha256, ct))
            {
                progress.Detail($"{label}: already downloaded");
                return;
            }
            progress.Warn($"{label}: cached file failed hash check — re-downloading");
            File.Delete(destPath);
        }

        string partPath = destPath + ".part";
        long haveBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (haveBytes > 0)
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(haveBytes, null);

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        bool resuming = haveBytes > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (haveBytes > 0 && !resuming)
        {
            // server ignored Range — start over
            File.Delete(partPath);
            haveBytes = 0;
        }
        resp.EnsureSuccessStatusCode();

        long? total = (resp.Content.Headers.ContentLength is { } len ? len + (resuming ? haveBytes : 0) : expectedSize);

        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(partPath, resuming ? FileMode.Append : FileMode.Create,
                     FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long done = haveBytes;
            int read;
            using var readTimeout = new CancellationTokenSource();
            while (true)
            {
                readTimeout.CancelAfter(TimeSpan.FromSeconds(100));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, readTimeout.Token);
                try
                {
                    read = await src.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (readTimeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new RehostException($"{label}: download stalled (no data for 100s).");
                }
                if (read == 0) break;
                readTimeout.TryReset();

                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total is { } t && t > 0)
                    progress.SubProgress(label, (double)done / t);
            }
        }

        if (expectedSize is { } exp && new FileInfo(partPath).Length != exp)
            throw new RehostException(
                $"{label}: downloaded {new FileInfo(partPath).Length} bytes, expected {exp}.");

        if (expectedSha256 is not null && !await MatchesHashAsync(partPath, expectedSha256, ct))
            throw new RehostException($"{label}: SHA-256 mismatch — download may be corrupt or the URL wrong.");

        File.Move(partPath, destPath, overwrite: true);
        progress.SubProgress(label, 1.0);
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<bool> MatchesHashAsync(string path, string expected, CancellationToken ct)
        => string.Equals(await Sha256Async(path, ct), expected.Trim().ToLowerInvariant(), StringComparison.Ordinal);
}
