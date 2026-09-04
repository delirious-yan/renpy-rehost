namespace RenpyRehost.Core;

/// <summary>Outcome of a single stage. Failure is signalled by throwing <see cref="RehostException"/>.</summary>
public readonly record struct StageOutcome(bool Skipped, string? Note)
{
    public static readonly StageOutcome Done = new(false, null);
    public static StageOutcome Completed(string note) => new(false, note);
    public static StageOutcome Skip(string reason) => new(true, reason);
}

/// <summary>
/// One step of the conversion. Stages are ordered and run once each; they
/// communicate only through <see cref="ConversionContext"/>.
/// </summary>
public interface IPipelineStage
{
    /// <summary>Short name shown in progress output, e.g. "Detect", "Build".</summary>
    string Name { get; }

    /// <summary>
    /// Return false to skip without running (e.g. asset pipeline when mode is Off).
    /// Skipped stages still advance the progress counter.
    /// </summary>
    bool ShouldRun(ConversionContext ctx) => true;

    Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct);
}
