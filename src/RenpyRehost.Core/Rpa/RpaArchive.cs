using System.IO.Compression;
using System.Text;

namespace RenpyRehost.Core.Rpa;

public sealed record RpaEntry(string Path, long Offset, long Length, byte[] Prefix);

/// <summary>
/// Reads a Ren'Py archive (<c>.rpa</c>), versions 3.0 / 3.2 / 2.0 / 1.0. Unkeyed
/// (or well-known-key) archives only — a game with a custom obfuscation key is a
/// preflight blocker, not something we unpack here.
/// </summary>
public sealed class RpaArchive : IDisposable
{
    private readonly FileStream _fs;

    public IReadOnlyList<RpaEntry> Entries { get; }
    public string Version { get; }

    private RpaArchive(FileStream fs, string version, List<RpaEntry> entries)
    {
        _fs = fs;
        Version = version;
        Entries = entries;
    }

    public static RpaArchive Open(string path)
    {
        var fs = File.OpenRead(path);
        string first = ReadLine(fs);

        long indexOffset;
        long key = 0;
        string version;

        if (first.StartsWith("RPA-3.2 "))
        {
            var p = first.Split(' ');
            version = "3.2";
            indexOffset = Convert.ToInt64(p[2], 16);
            for (int k = 3; k < p.Length; k++) key ^= Convert.ToInt64(p[k], 16);
        }
        else if (first.StartsWith("RPA-3.0 "))
        {
            var p = first.Split(' ');
            version = "3.0";
            indexOffset = Convert.ToInt64(p[1], 16);
            for (int k = 2; k < p.Length; k++) key ^= Convert.ToInt64(p[k], 16);
        }
        else if (first.StartsWith("RPA-2.0 "))
        {
            version = "2.0";
            indexOffset = Convert.ToInt64(first.Split(' ')[1], 16);
        }
        else
        {
            // RPA-1.0 has no header line — the whole file is a zlib index; rare, skip for now.
            fs.Dispose();
            throw new RehostException($"Unrecognised archive header: '{first[..Math.Min(20, first.Length)]}'. RPA 2.0/3.0/3.2 supported.");
        }

        fs.Seek(indexOffset, SeekOrigin.Begin);
        byte[] indexBytes;
        using (var z = new ZLibStream(fs, CompressionMode.Decompress, leaveOpen: true))
        using (var ms = new MemoryStream())
        {
            z.CopyTo(ms);
            indexBytes = ms.ToArray();
        }

        var root = Pickle.Loads(indexBytes) as Dictionary<object, object?>
            ?? throw new RehostException("Archive index is not a dict — unexpected format.");

        var entries = new List<RpaEntry>(root.Count);
        foreach (var (k, v) in root)
        {
            string name = k as string ?? Encoding.UTF8.GetString((byte[])k);
            // value is a list of tuples; take the first (Ren'Py splits only for edge cases)
            var list = (List<object?>)v!;
            var tuple = (object?[])list[0]!;
            long off = Convert.ToInt64(tuple[0]);
            long len = Convert.ToInt64(tuple[1]);
            byte[] prefix = tuple.Length > 2
                ? tuple[2] switch { byte[] b => b, string s => Encoding.UTF8.GetBytes(s), _ => Array.Empty<byte>() }
                : Array.Empty<byte>();

            if (version.StartsWith('3'))
            {
                off ^= key;
                len ^= key;
            }
            entries.Add(new RpaEntry(name.Replace('/', Path.DirectorySeparatorChar), off, len, prefix));
        }

        return new RpaArchive(fs, version, entries);
    }

    /// <summary>
    /// Extract every entry under <paramref name="destDir"/>. Existing files are
    /// left alone (a loose file overrides its archived copy in Ren'Py, so the
    /// clone's real files win). Returns the number of files written.
    /// </summary>
    public int ExtractAll(string destDir, IProgressSink progress, string label, CancellationToken ct)
    {
        var buffer = new byte[1 << 20];
        int done = 0, written = 0, total = Entries.Count;
        string full = Path.GetFullPath(destDir);

        foreach (var e in Entries)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            string target = Path.GetFullPath(Path.Combine(destDir, e.Path));
            if (target != full && !target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new RehostException($"{label}: archive entry escapes the target: {e.Path}");

            if (File.Exists(target)) { if (done % 200 == 0) progress.SubProgress(label, (double)done / total); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var outFs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            if (e.Prefix.Length > 0) outFs.Write(e.Prefix);

            _fs.Seek(e.Offset, SeekOrigin.Begin);
            long remaining = e.Length - e.Prefix.Length;
            while (remaining > 0)
            {
                int want = (int)Math.Min(buffer.Length, remaining);
                int read = _fs.Read(buffer, 0, want);
                if (read == 0) break;
                outFs.Write(buffer, 0, read);
                remaining -= read;
            }

            written++;
            if (done % 200 == 0) progress.SubProgress(label, (double)done / total);
        }
        progress.SubProgress(label, 1.0);
        return written;
    }

    private static string ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        int b;
        while ((b = s.ReadByte()) is not (-1 or '\n'))
            sb.Append((char)b);
        return sb.ToString();
    }

    public void Dispose() => _fs.Dispose();
}
