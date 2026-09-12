namespace RenpyRehost.Core;

/// <summary>
/// Wraps a progress sink so every event also lands in a <see cref="Log"/> file —
/// full detail, always, regardless of whether the console/GUI is showing verbose
/// output. Forwards everything to <paramref name="inner"/> unchanged.
/// </summary>
public sealed class LoggingProgressSink(IProgressSink inner, Log log) : IProgressSink
{
    public void StageStarted(int index, int total, string name)
    {
        log.WriteLine($"[{index}/{total}] {name} — start");
        inner.StageStarted(index, total, name);
    }

    public void StageFinished(int index, string name, string? skippedReason)
    {
        log.WriteLine(skippedReason is null ? $"{name} — done" : $"{name} — skipped ({skippedReason})");
        inner.StageFinished(index, name, skippedReason);
    }

    public void Info(string message)
    {
        log.WriteLine(message);
        inner.Info(message);
    }

    public void Warn(string message)
    {
        log.WriteLine("WARN " + message);
        inner.Warn(message);
    }

    public void Detail(string message)
    {
        log.WriteLine(message);
        inner.Detail(message);
    }

    // Sub-progress ticks fire many times a second during downloads/scans — logging
    // every one would flood the file for no troubleshooting benefit.
    public void SubProgress(string label, double fraction) => inner.SubProgress(label, fraction);
}
