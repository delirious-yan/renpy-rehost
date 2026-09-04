namespace RenpyRehost.Core;

public enum FindingSeverity { Info, Warning, Blocker }

/// <summary>One preflight observation about the game.</summary>
/// <param name="Code">Stable slug, e.g. "keyed-archive", "h264-video".</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record Finding(string Code, string Message, FindingSeverity Severity);

public enum Recommendation
{
    /// <summary>Modern, well-behaved — convert as-is.</summary>
    Go,
    /// <summary>Convertible, but needs the asset pipeline to run acceptably.</summary>
    GoWithPipeline,
    /// <summary>No native web target — needs the decompile-and-port path (P4).</summary>
    NeedsPortForward,
    /// <summary>A hard blocker (DRM, keyed archives, native modules) — cannot convert.</summary>
    Stop,
}

/// <summary>Size breakdown, bytes.</summary>
public sealed class SizeProfile
{
    public long Total { get; set; }
    public long Images { get; set; }
    public long Audio { get; set; }
    public long Video { get; set; }
    public long Other { get; set; }
    public Dictionary<string, long> Archives { get; } = new();
    /// <summary>Rough estimate of peak in-tab working set, bytes (heuristic).</summary>
    public long EstimatedPeakWorkingSet { get; set; }
}

/// <summary>Preflight's structured verdict. The GUI renders it; the CLI prints it.</summary>
public sealed class ConversionReport
{
    public RenpyVersion? EngineVersion { get; set; }
    public bool WebTargetNative { get; set; }
    public List<Finding> Blockers { get; } = new();
    public List<Finding> Warnings { get; } = new();
    public SizeProfile Size { get; } = new();
    public Recommendation Recommendation { get; set; } = Recommendation.Go;

    public bool HasBlockers => Blockers.Count > 0;
}
