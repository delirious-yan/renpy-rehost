using System.IO.Compression;
using System.Text;
using RenpyRehost.Core;
using RenpyRehost.Core.Rpa;

namespace RenpyRehost.Tests;

public class RpaArchiveTests
{
    // Minimal RPA-3.0 writer: header line, file data, then a zlib'd pickle index
    // with offsets/lengths XOR-obfuscated by the key.
    private static void WriteRpa3(string path, long key, Dictionary<string, byte[]> files)
    {
        using var fs = new FileStream(path, FileMode.Create);

        // reserve a fixed-width header line we rewrite at the end
        string headerFmt = "RPA-3.0 {0:x16} {1:x8}\n";
        long headerLen = string.Format(headerFmt, 0, 0).Length;
        fs.Write(new byte[headerLen]);

        var placed = new List<(string name, long off, long len)>();
        foreach (var (name, data) in files)
        {
            long off = fs.Position;
            fs.Write(data);
            placed.Add((name, off, data.Length));
        }

        long indexOffset = fs.Position;
        byte[] pickle = BuildIndexPickle(placed, key);
        using (var z = new ZLibStream(fs, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(pickle);

        fs.Seek(0, SeekOrigin.Begin);
        var header = Encoding.ASCII.GetBytes(string.Format(headerFmt, indexOffset, key));
        fs.Write(header);
    }

    private static byte[] BuildIndexPickle(List<(string name, long off, long len)> entries, long key)
    {
        var b = new List<byte> { 0x80, 0x02, (byte)'}' }; // PROTO 2, EMPTY_DICT
        foreach (var (name, off, len) in entries)
        {
            var nb = Encoding.UTF8.GetBytes(name);
            b.Add((byte)'X');
            b.AddRange(BitConverter.GetBytes(nb.Length));
            b.AddRange(nb);                                  // BINUNICODE name
            b.Add((byte)']');                               // EMPTY_LIST
            AddBinInt(b, off ^ key);
            AddBinInt(b, len ^ key);
            b.Add((byte)'U'); b.Add(0);                     // SHORT_BINSTRING ""  (prefix)
            b.Add((byte)'\x87');                            // TUPLE3
            b.Add((byte)'a');                               // APPEND
            b.Add((byte)'s');                               // SETITEM
        }
        b.Add((byte)'.');                                   // STOP
        return b.ToArray();
    }

    private static void AddBinInt(List<byte> b, long v)
    {
        b.Add((byte)'J');
        b.AddRange(BitConverter.GetBytes((int)v));
    }

    [Fact]
    public void Round_trips_a_synthetic_rpa3()
    {
        using var g = GameFixture.Create();
        string rpa = g.At("archive.rpa");
        var files = new Dictionary<string, byte[]>
        {
            ["images/bg.png"] = Encoding.UTF8.GetBytes("PNGDATA-background"),
            ["gui/logo.png"] = Encoding.UTF8.GetBytes("logo-bytes"),
            ["script.rpyc"] = new byte[] { 1, 2, 3, 4, 5 },
        };
        WriteRpa3(rpa, 0x42424242, files);

        using var archive = RpaArchive.Open(rpa);
        Assert.Equal("3.0", archive.Version);
        Assert.Equal(3, archive.Entries.Count);

        string outDir = g.At("out");
        int wrote = archive.ExtractAll(outDir, NullProgressSink.Instance, "t", default);

        Assert.Equal(3, wrote);
        Assert.Equal("PNGDATA-background", File.ReadAllText(Path.Combine(outDir, "images", "bg.png")));
        Assert.Equal("logo-bytes", File.ReadAllText(Path.Combine(outDir, "gui", "logo.png")));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(Path.Combine(outDir, "script.rpyc")));
    }

    [Fact]
    public void Leaves_existing_loose_files_untouched()
    {
        using var g = GameFixture.Create();
        string rpa = g.At("archive.rpa");
        WriteRpa3(rpa, 0x1, new() { ["a.txt"] = Encoding.UTF8.GetBytes("from-archive") });

        string outDir = g.At("out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "a.txt"), "loose-wins");

        using var archive = RpaArchive.Open(rpa);
        int wrote = archive.ExtractAll(outDir, NullProgressSink.Instance, "t", default);

        Assert.Equal(0, wrote);
        Assert.Equal("loose-wins", File.ReadAllText(Path.Combine(outDir, "a.txt")));
    }

    [Fact]
    public void Rejects_an_unknown_header()
    {
        using var g = GameFixture.Create();
        string rpa = g.At("bad.rpa");
        File.WriteAllText(rpa, "NOT-AN-RPA 0000\n....");

        Assert.Throws<RehostException>(() => RpaArchive.Open(rpa));
    }
}
