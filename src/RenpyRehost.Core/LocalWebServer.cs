using System.Net;

namespace RenpyRehost.Core;

/// <summary>
/// Minimal static file server for a web build: correct MIME types (notably
/// <c>application/wasm</c>), HTTP Range support, and the cross-origin isolation
/// headers the Ren'Py web runtime needs for SharedArrayBuffer. Runs until disposed.
/// </summary>
public sealed class LocalWebServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public string Url { get; }

    private LocalWebServer(string root, int port)
    {
        _root = Path.GetFullPath(root);
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
    }

    public static LocalWebServer Start(string root, int? port = null)
    {
        int p = port ?? FreePort();
        var server = new LocalWebServer(root, p);
        server._listener.Start();
        server._loop = Task.Run(server.LoopAsync);
        return server;
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            // cross-origin isolation — Ren'Py web needs SharedArrayBuffer
            ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            ctx.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            ctx.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            string rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath.TrimStart('/'));
            if (rel.Length == 0) rel = "index.html";
            string path = Path.GetFullPath(Path.Combine(_root, rel));
            if (!path.StartsWith(_root, StringComparison.Ordinal) || !File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            ctx.Response.ContentType = MimeFor(path);
            var info = new FileInfo(path);
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);

            long start = 0, end = info.Length - 1;
            string? range = ctx.Request.Headers["Range"];
            if (range is not null && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var span = range["bytes=".Length..].Split('-');
                if (long.TryParse(span[0], out var s)) start = s;
                if (span.Length > 1 && long.TryParse(span[1], out var e)) end = e;
                end = Math.Min(end, info.Length - 1);
                if (start > end) { ctx.Response.StatusCode = 416; ctx.Response.Close(); return; }
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{info.Length}";
            }

            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            ctx.Response.ContentLength64 = end - start + 1;
            fs.Seek(start, SeekOrigin.Begin);

            long remaining = end - start + 1;
            var buffer = new byte[1 << 20];
            while (remaining > 0)
            {
                int want = (int)Math.Min(buffer.Length, remaining);
                int read = await fs.ReadAsync(buffer.AsMemory(0, want)).ConfigureAwait(false);
                if (read == 0) break;
                await ctx.Response.OutputStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                remaining -= read;
            }
        }
        catch { /* client went away */ }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".wasm" => "application/wasm",
        ".json" => "application/json; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".data" or ".mem" => "application/octet-stream",
        ".zip" => "application/zip",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".opus" => "audio/opus",
        ".wav" => "audio/wav",
        ".webm" => "video/webm",
        ".mp4" => "video/mp4",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { }
        }
        _listener.Close();
        _cts.Dispose();
    }
}
