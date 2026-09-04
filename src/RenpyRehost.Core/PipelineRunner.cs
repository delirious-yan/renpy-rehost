using System.Diagnostics;

namespace RenpyRehost.Core;

public sealed record ConversionResult(
    bool Success,
    ConversionContext Context,
    string? FailedStage,
    Exception? Error,
    TimeSpan Elapsed);

/// <summary>Runs an ordered list of stages against a context, reporting progress and stopping on the first failure.</summary>
public sealed class PipelineRunner
{
    private readonly IReadOnlyList<IPipelineStage> _stages;

    public PipelineRunner(IEnumerable<IPipelineStage> stages)
        => _stages = stages.ToList();

    /// <summary>The default full conversion pipeline. Stages are added as they land (see ROADMAP).</summary>
    public static PipelineRunner Default() => new(new IPipelineStage[]
    {
        new Stages.IngestStage(),
        new Stages.DetectStage(),
        new Stages.PreflightStage(),
        new Stages.AcquireSdkStage(),
        new Stages.ReconstructStage(),
        new Stages.AssetPipelineStage(),
        new Stages.BuildStage(),
        new Stages.AssembleStage(),
        new Stages.ServeStage(),
    });

    public async Task<ConversionResult> RunAsync(ConversionContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int total = _stages.Count;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var stage = _stages[i];
            int number = i + 1;

            if (!stage.ShouldRun(ctx))
            {
                ctx.Progress.StageStarted(number, total, stage.Name);
                ctx.Progress.StageFinished(number, stage.Name, "not applicable");
                continue;
            }

            ctx.Progress.StageStarted(number, total, stage.Name);
            try
            {
                var outcome = await stage.RunAsync(ctx, ct).ConfigureAwait(false);
                ctx.Progress.StageFinished(number, stage.Name, outcome.Skipped ? outcome.Note ?? "skipped" : null);
                if (!outcome.Skipped && outcome.Note is { } note)
                    ctx.Progress.Info(note);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RehostException ex)
            {
                sw.Stop();
                ctx.Progress.Warn($"{stage.Name} failed: {ex.Message}");
                return new ConversionResult(false, ctx, stage.Name, ex, sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                ctx.Progress.Warn($"{stage.Name} hit an unexpected error: {ex.Message}");
                return new ConversionResult(false, ctx, stage.Name, ex, sw.Elapsed);
            }
        }

        sw.Stop();
        return new ConversionResult(true, ctx, null, null, sw.Elapsed);
    }
}
