using RenpyRehost.Core;

namespace RenpyRehost.App;

/// <summary>Turns pipeline progress into events the form can bind to (marshalled to the UI thread by the form).</summary>
public sealed class UiProgressSink : IProgressSink
{
    public event Action<int, int, string>? StageStart;
    public event Action<int, string, string?>? StageEnd;
    public event Action<string, bool>? Line;                 // message, isWarning
    public event Action<string>? DetailLine;
    public event Action<string, double>? Sub;

    public void StageStarted(int index, int total, string name) => StageStart?.Invoke(index, total, name);
    public void StageFinished(int index, string name, string? skippedReason) => StageEnd?.Invoke(index, name, skippedReason);
    public void Info(string message) => Line?.Invoke(message, false);
    public void Warn(string message) => Line?.Invoke(message, true);
    public void Detail(string message) => DetailLine?.Invoke(message);
    public void SubProgress(string label, double fraction) => Sub?.Invoke(label, fraction);
}
