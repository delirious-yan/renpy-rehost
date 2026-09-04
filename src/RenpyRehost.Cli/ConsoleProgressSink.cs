using RenpyRehost.Core;

namespace RenpyRehost.Cli;

/// <summary>Plain, line-oriented console output. Verbose gates the Detail channel.</summary>
public sealed class ConsoleProgressSink : IProgressSink
{
    private readonly bool _verbose;
    private readonly bool _redirected = Console.IsOutputRedirected;
    private readonly object _lock = new();
    private string _lastSubLabel = "";
    private int _lastPct = -1;

    public ConsoleProgressSink(bool verbose) => _verbose = verbose;

    public void StageStarted(int index, int total, string name)
    {
        lock (_lock) Console.WriteLine($"[{index}/{total}] {name}");
    }

    public void StageFinished(int index, string name, string? skippedReason)
    {
        if (skippedReason is null) return;
        lock (_lock) Console.WriteLine($"      skipped — {skippedReason}");
    }

    public void Info(string message)
    {
        lock (_lock) Console.WriteLine($"      {message}");
    }

    public void Warn(string message)
    {
        lock (_lock)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"      ! {message}");
            Console.ForegroundColor = prev;
        }
    }

    public void Detail(string message)
    {
        if (!_verbose) return;
        lock (_lock)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"      · {message}");
            Console.ForegroundColor = prev;
        }
    }

    public void SubProgress(string label, double fraction)
    {
        lock (_lock)
        {
            int pct = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
            bool newLabel = label != _lastSubLabel;
            if (newLabel) { _lastSubLabel = label; _lastPct = -1; }

            if (_redirected)
            {
                // no carriage-return trick when piped — one line every 10%
                if (pct == _lastPct || (pct % 10 != 0 && pct < 100)) return;
                _lastPct = pct;
                Console.WriteLine($"      {label}: {pct}%");
            }
            else
            {
                if (pct == _lastPct) return;
                _lastPct = pct;
                if (newLabel) Console.WriteLine();
                Console.Write($"\r      {label}: {pct,3}%   ");
                if (pct >= 100) { Console.WriteLine(); _lastSubLabel = ""; }
            }
        }
    }
}
