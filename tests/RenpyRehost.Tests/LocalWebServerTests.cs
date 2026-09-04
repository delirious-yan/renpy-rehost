using System.Net.Http.Headers;
using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class LocalWebServerTests
{
    [Fact]
    public async Task Serves_files_with_wasm_mime_and_isolation_headers()
    {
        using var g = GameFixture.Create();
        g.File("index.html", "<h1>hi</h1>");
        g.Bytes("index.wasm", 16);

        await using var server = LocalWebServer.Start(g.Root);
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };

        var html = await http.GetAsync("index.html");
        Assert.True(html.IsSuccessStatusCode);
        Assert.Equal("same-origin", html.Headers.GetValues("Cross-Origin-Opener-Policy").Single());

        var wasm = await http.GetAsync("index.wasm");
        Assert.Equal("application/wasm", wasm.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Root_serves_index_html()
    {
        using var g = GameFixture.Create();
        g.File("index.html", "root");

        await using var server = LocalWebServer.Start(g.Root);
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };

        Assert.Equal("root", await http.GetStringAsync(""));
    }

    [Fact]
    public async Task Honours_range_requests()
    {
        using var g = GameFixture.Create();
        File.WriteAllText(g.At("data.bin"), "0123456789");

        await using var server = LocalWebServer.Start(g.Root);
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };

        var req = new HttpRequestMessage(HttpMethod.Get, "data.bin");
        req.Headers.Range = new RangeHeaderValue(2, 5);
        var resp = await http.SendAsync(req);

        Assert.Equal(System.Net.HttpStatusCode.PartialContent, resp.StatusCode);
        Assert.Equal("2345", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Blocks_path_traversal()
    {
        using var g = GameFixture.Create();
        g.File("wwwroot/index.html", "safe");
        g.File("secret.txt", "nope");

        await using var server = LocalWebServer.Start(g.At("wwwroot"));
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };

        var resp = await http.GetAsync("../secret.txt");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }
}
