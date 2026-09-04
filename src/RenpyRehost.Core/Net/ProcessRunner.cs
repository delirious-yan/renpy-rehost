using System.Diagnostics;
using System.Text;

namespace RenpyRehost.Core;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
    /// <summary>Last <paramref name="lines"/> lines across stdout+stderr, for error messages.</summary>
    public string Tail(int lines = 20)
    {
        var all = (StdOut + "\n" + StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", all[Math.Max(0, all.Length - lines)..]);
    }
}

public static class ProcessRunner
{
    /// <summary>
    /// Run a child process to completion, streaming each output line to
    /// <paramref name="onLine"/> as it arrives, and also capturing it. Kills the
    /// process if the token is cancelled.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string exe, IEnumerable<string> args, string? workingDir,
        IDictionary<string, string>? env, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(exe),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null)
            foreach (var (k, v) in env) psi.Environment[k] = v;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outSb = new StringBuilder();
        var errSb = new StringBuilder();
        var done = new TaskCompletionSource();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outSb) outSb.AppendLine(e.Data);
            onLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (errSb) errSb.AppendLine(e.Data);
            onLine(e.Data);
        };
        proc.Exited += (_, _) => done.TrySetResult();

        if (!proc.Start())
            throw new RehostException($"Failed to start {exe}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await using (ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        }))
        {
            await done.Task.ConfigureAwait(false);
        }
        // let the async readers drain
        proc.WaitForExit();

        ct.ThrowIfCancellationRequested();
        return new ProcessResult(proc.ExitCode, outSb.ToString(), errSb.ToString());
    }
}
