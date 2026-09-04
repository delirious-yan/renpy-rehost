namespace RenpyRehost.Core;

/// <summary>
/// Where pipeline progress goes. The CLI writes an ANSI console sink; the GUI
/// binds it to the staged progress list + log pane. Implementations must be safe
/// to call from a background thread.
/// </summary>
public interface IProgressSink
{
    /// <summary>A new stage has started (1-based <paramref name="index"/> of <paramref name="total"/>).</summary>
    void StageStarted(int index, int total, string name);

    /// <summary>The current stage finished. <paramref name="skippedReason"/> non-null means it was skipped.</summary>
    void StageFinished(int index, string name, string? skippedReason);

    void Info(string message);
    void Warn(string message);

    /// <summary>Verbose-only detail (command lines, per-file notes).</summary>
    void Detail(string message);

    /// <summary>Fractional progress within the current stage (0..1), for long steps like downloads.</summary>
    void SubProgress(string label, double fraction);
}

/// <summary>A sink that drops everything — for tests and headless callers that don't care.</summary>
public sealed class NullProgressSink : IProgressSink
{
    public static readonly NullProgressSink Instance = new();
    public void StageStarted(int index, int total, string name) { }
    public void StageFinished(int index, string name, string? skippedReason) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Detail(string message) { }
    public void SubProgress(string label, double fraction) { }
}
