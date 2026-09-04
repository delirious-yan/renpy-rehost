namespace RenpyRehost.Core.Stages;

/// <summary>
/// Starts a local static server for the assembled web build. The server keeps
/// running after the pipeline returns — the caller (CLI/GUI) owns its lifetime
/// via <see cref="ConversionContext.RunningServer"/>.
/// </summary>
public sealed class ServeStage : IPipelineStage
{
    public string Name => "Serve";

    public bool ShouldRun(ConversionContext ctx) => ctx.Options.Serve && !ctx.Options.EmitOnly;

    public Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        string dir = ctx.ServeDir ?? throw new RehostException("Serve has no assembled build.");

        LocalWebServer server;
        try
        {
            server = LocalWebServer.Start(dir, ctx.Options.ServePort);
        }
        catch (System.Net.HttpListenerException ex)
        {
            throw new RehostException(
                $"Couldn't start the local server ({ex.Message}). "
                + (ctx.Options.ServePort is { } p ? $"Port {p} may be in use — try a different --port." : ""));
        }

        ctx.RunningServer = server;
        ctx.ServeUrl = server.Url + "index.html";
        return Task.FromResult(StageOutcome.Completed($"serving at {ctx.ServeUrl}"));
    }
}
