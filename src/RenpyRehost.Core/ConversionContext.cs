namespace RenpyRehost.Core;

/// <summary>
/// Mutable state threaded through the pipeline. Each stage reads what earlier
/// stages set and fills in its own outputs. A property being null means "the
/// stage that produces it hasn't run (or was skipped)".
/// </summary>
public sealed class ConversionContext
{
    public ConversionContext(string sourcePath, ConversionOptions options, IProgressSink progress)
    {
        SourcePath = sourcePath;
        Options = options;
        Progress = progress;
    }

    public ConversionOptions Options { get; }
    public IProgressSink Progress { get; }

    /// <summary>What the user dropped — a game folder, or a shipped .exe.</summary>
    public string SourcePath { get; }

    // ---- filled in by stages, in order ----

    /// <summary>The resolved <c>game/</c> folder (has .rpyc / .rpa). Set by Ingest.</summary>
    public string? GameDir { get; set; }

    /// <summary>A friendly game name derived from the source. Set by Ingest.</summary>
    public string? GameName { get; set; }

    /// <summary>The engine version the build uses (detected, or the caller's override). Set by Detect.</summary>
    public RenpyVersion? Version { get; set; }

    /// <summary>What the game files actually pin, even when <see cref="Version"/> is an override. Set by Detect.</summary>
    public RenpyVersion? DetectedVersion { get; set; }

    /// <summary>Preflight verdict. Set by Preflight.</summary>
    public ConversionReport? Report { get; set; }

    /// <summary>Unpacked, web-support-installed SDK for <see cref="Version"/>. Set by AcquireSdk.</summary>
    public string? SdkDir { get; set; }

    /// <summary>Reconstructed Ren'Py project (skeleton + game/ + patches). Set by Reconstruct.</summary>
    public string? ProjectDir { get; set; }

    /// <summary>Raw <c>web_build</c> output folder. Set by Build.</summary>
    public string? WebBuildDir { get; set; }

    /// <summary>Final serve directory (bundle + streamed archives + manifest). Set by Assemble.</summary>
    public string? ServeDir { get; set; }

    /// <summary>URL the local server is listening on. Set by Serve (null when EmitOnly).</summary>
    public string? ServeUrl { get; set; }

    /// <summary>The running local server, if Serve started one. The caller disposes it when done.</summary>
    public IAsyncDisposable? RunningServer { get; set; }

    /// <summary>Free-form notes any stage can stash for diagnostics.</summary>
    public Dictionary<string, string> Notes { get; } = new();
}
