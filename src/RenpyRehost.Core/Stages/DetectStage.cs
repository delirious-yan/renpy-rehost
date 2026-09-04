namespace RenpyRehost.Core.Stages;

/// <summary>Pins the engine version from the game files, or takes the caller's override.</summary>
public sealed class DetectStage : IPipelineStage
{
    public string Name => "Detect";

    public Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        if (ctx.GameDir is null)
            throw new RehostException("Ingest did not resolve a game folder.");

        var detection = VersionDetector.Detect(ctx.GameDir);
        ctx.DetectedVersion = detection.Version;
        foreach (var e in detection.Evidence)
            ctx.Progress.Detail(e);

        if (ctx.Options.VersionOverride is { } forced)
        {
            ctx.Version = forced;
            if (detection.Version is { } found && !found.Equals(forced))
                ctx.Progress.Warn($"Override {forced} differs from detected {found} — using the override.");
            return Task.FromResult(StageOutcome.Completed($"engine {forced} (override)"));
        }

        if (detection.Version is null)
        {
            string hint = detection.Source == VersionSource.LibRuntimeHint
                ? " The lib/ folder suggests the era but not the exact release."
                : "";
            throw new RehostException(
                "Couldn't pin the Ren'Py version from the game files." + hint
                + " Run the game once so it writes log.txt, or pass an explicit version (--renpy-version x.y.z).");
        }

        ctx.Version = detection.Version;
        return Task.FromResult(StageOutcome.Completed($"engine {detection.Version} (from {detection.Source})"));
    }
}
