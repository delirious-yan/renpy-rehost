using System.IO.Compression;

namespace RenpyRehost.Core;

public static class ZipUtil
{
    /// <summary>
    /// Extract <paramref name="zipPath"/> into <paramref name="destDir"/>.
    /// When <paramref name="stripTopFolder"/> is true and every entry sits under a
    /// single top-level folder (Ren'Py's SDK zip: <c>renpy-7.4.11-sdk/…</c>), that
    /// folder is removed so files land directly in <paramref name="destDir"/>.
    /// The web-support zip keeps its <c>web/</c> folder, so pass false there.
    /// Guards against path traversal.
    /// </summary>
    public static void Extract(string zipPath, string destDir, bool stripTopFolder,
        IProgressSink progress, string label, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        string full = Path.GetFullPath(destDir);

        using var archive = ZipFile.OpenRead(zipPath);
        string? prefix = stripTopFolder ? CommonTopFolder(archive) : null;

        int done = 0, total = archive.Entries.Count;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            string rel = entry.FullName;
            if (prefix is not null)
            {
                if (rel.Length <= prefix.Length) continue; // the bare prefix folder entry
                rel = rel[prefix.Length..];
            }

            rel = rel.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(full, rel));
            if (target != full && !target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new RehostException($"{label}: zip entry escapes the target folder: {entry.FullName}");

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            if (++done % 250 == 0)
            {
                progress.SubProgress(label, (double)done / total);
                progress.Detail($"{label}: {done}/{total} entries");
            }
        }
        progress.SubProgress(label, 1.0);
    }

    /// <summary>The single top-level folder every entry shares (with trailing slash), or null.</summary>
    private static string? CommonTopFolder(ZipArchive archive)
    {
        string? top = null;
        foreach (var e in archive.Entries)
        {
            int slash = e.FullName.IndexOf('/');
            if (slash < 0) return null; // a file at the root — no common folder
            string first = e.FullName[..(slash + 1)];
            if (top is null) top = first;
            else if (!string.Equals(top, first, StringComparison.Ordinal)) return null;
        }
        return top;
    }
}
