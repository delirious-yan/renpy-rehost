namespace RenpyRehost.Core;

public enum AssetPipelineMode
{
    /// <summary>Never touch assets — build straight from the original game files.</summary>
    Off,
    /// <summary>Run the passes preflight recommends (default once P2 lands).</summary>
    Auto,
    /// <summary>Run every asset pass regardless of preflight.</summary>
    Force,
}

/// <summary>
/// Everything the caller configures for one conversion. Paths are absolute by the
/// time the pipeline sees them (the CLI/GUI resolves them).
/// </summary>
public sealed class ConversionOptions
{
    /// <summary>Where downloaded Ren'Py SDKs are cached, one subfolder per version.</summary>
    public required string SdkCacheDir { get; init; }

    /// <summary>Where bundled/downloaded tools (ffmpeg) are cached.</summary>
    public required string ToolsCacheDir { get; init; }

    /// <summary>Scratch root for reconstructed projects and intermediate build output.</summary>
    public required string WorkDir { get; init; }

    /// <summary>Final serve directory (bundle + streamed archives + manifest).</summary>
    public required string OutputDir { get; init; }

    /// <summary>Start a local web server and (unless <see cref="EmitOnly"/>) open a browser.</summary>
    public bool Serve { get; init; }

    /// <summary>Fixed port for the local server; null picks a free one.</summary>
    public int? ServePort { get; init; }

    /// <summary>Produce the serve folder and stop — no server. For hosting the build yourself.</summary>
    public bool EmitOnly { get; init; }

    /// <summary>Asset pipeline behaviour. Auto runs the passes that help this game.</summary>
    public AssetPipelineMode AssetPipeline { get; init; } = AssetPipelineMode.Auto;

    /// <summary>Downscale images wider than this (px) in the asset pipeline.</summary>
    public int ImageMaxWidth { get; init; } = 1920;

    /// <summary>Never hit the network; fail if a needed SDK is not already cached.</summary>
    public bool OfflineOnly { get; init; }

    /// <summary>Force a specific engine version when the game files don't pin one (no log.txt, old build).</summary>
    public RenpyVersion? VersionOverride { get; init; }

    /// <summary>Display title for the build (rehost.json, PWA). Defaults to a name derived from the folder.</summary>
    public string? Title { get; init; }

    /// <summary>Reuse an existing reconstructed project (skip the clone + .rpa unpack). For iterating on Build.</summary>
    public bool ReuseProject { get; init; }

    /// <summary>Keep the reconstructed project + intermediate build output after a successful convert (default: delete it).</summary>
    public bool KeepWork { get; init; }

    /// <summary>Proceed past preflight blockers (keyed archives, obfuscated bytecode). Rarely useful.</summary>
    public bool Force { get; init; }

    /// <summary>Emit verbose per-step detail.</summary>
    public bool Verbose { get; init; }
}
