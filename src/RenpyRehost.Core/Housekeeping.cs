namespace RenpyRehost.Core;

/// <summary>
/// Finds and removes the scratch a conversion leaves behind — the reconstructed
/// project clone and Ren'Py's <c>&lt;name&gt;-dists</c> build output (which includes a
/// full <c>-web.zip</c> copy of the finished build). The finished build in
/// <c>--out</c> / the library is never touched.
/// </summary>
public static class Housekeeping
{
    /// <summary>Scratch directories produced for this one conversion.</summary>
    public static IEnumerable<string> ScratchDirsFor(ConversionContext ctx)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string work = Path.GetFullPath(ctx.Options.WorkDir);

        bool UnderWork(string p) =>
            Path.GetFullPath(p).StartsWith(
                work.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        // The reconstructed project clone.
        if (ctx.ProjectDir is { } proj && Directory.Exists(proj) && UnderWork(proj)
            && seen.Add(Path.GetFullPath(proj)))
            yield return Path.GetFullPath(proj);

        // Ren'Py's "<buildname>-dists" output dir — the -web folder Assemble took, plus
        // a full "<buildname>-web.zip" (a second copy of the port). Its name comes from
        // the game's build.name, not our project name, so find it via the web output path.
        if (ctx.WebBuildDir is { } web)
        {
            string dists = Path.GetDirectoryName(Path.GetFullPath(web)) ?? "";
            if (dists.Length > 0 && Directory.Exists(dists) && UnderWork(dists)
                && !string.Equals(dists, work, StringComparison.OrdinalIgnoreCase)
                && seen.Add(dists))
                yield return dists;

            // If Assemble copied rather than moved, the -web folder itself still sits there.
            string wf = Path.GetFullPath(web);
            bool isServeDir = ctx.ServeDir is { } s &&
                string.Equals(Path.GetFullPath(s), wf, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(wf) && !isServeDir && UnderWork(wf) && seen.Add(wf))
                yield return wf;
        }

        // Fallback: any "<projname>*-dists" sibling (covers odd build layouts).
        if (ctx.ProjectDir is { } p2)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(p2)) ?? "";
            string stem = Path.GetFileName(Path.GetFullPath(p2));
            if (parent.Length > 0 && Directory.Exists(parent))
                foreach (var d in SafeDirs(parent, stem + "*-dists"))
                {
                    string df = Path.GetFullPath(d);
                    if (UnderWork(df) && seen.Add(df)) yield return df;
                }
        }
    }

    /// <summary>Delete this conversion's scratch. Returns bytes reclaimed. Never throws — a failure is logged via <see cref="ConversionContext.Progress"/> and left for next time.</summary>
    public static long CleanScratch(ConversionContext ctx)
    {
        long freed = 0;
        foreach (var dir in ScratchDirsFor(ctx))
        {
            try
            {
                freed += DirSize(dir);
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                // Best effort — a locked file just means we free it next time — but
                // say so, rather than silently leaving scratch behind unexplained.
                ctx.Progress.Warn($"couldn't clean up {dir}: {ex.Message}");
            }
        }
        return freed;
    }

    /// <summary>How much reclaimable scratch sits under a work root right now.</summary>
    public static (long bytes, int items) SurveyWork(string workDir)
    {
        long bytes = 0;
        int items = 0;
        foreach (var dir in WorkScratchDirs(workDir))
        {
            bytes += DirSize(dir);
            items++;
        }
        return (bytes, items);
    }

    /// <summary>Delete every conversion's scratch under a work root (keeps SDKs and tools). Returns bytes reclaimed.</summary>
    public static long CleanAllWork(string workDir, Log? log = null)
    {
        long freed = 0;
        foreach (var dir in WorkScratchDirs(workDir))
        {
            try
            {
                freed += DirSize(dir);
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                log?.Exception($"couldn't delete {dir}", ex);
            }
        }
        return freed;
    }

    // work/proj/<name>, work/proj/<name>-dists, work/ingest/<hash> — all disposable.
    private static IEnumerable<string> WorkScratchDirs(string workDir)
    {
        foreach (var sub in new[] { "proj", "ingest" })
        {
            string root = Path.Combine(workDir, sub);
            if (!Directory.Exists(root)) continue;
            foreach (var d in SafeDirs(root, "*")) yield return d;
        }
    }

    private static IEnumerable<string> SafeDirs(string root, string pattern)
    {
        try { return Directory.EnumerateDirectories(root, pattern); }
        catch { return Array.Empty<string>(); }
    }

    public static long DirSize(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(f).Length; } catch { }
        }
        catch { }
        return total;
    }
}
