using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class PreflightScannerTests
{
    private static ConversionReport Scan(GameFixture g)
    {
        var r = new ConversionReport { EngineVersion = new RenpyVersion(8, 3, 7), WebTargetNative = true };
        PreflightScanner.Scan(g.Root, null, r, default);
        return r;
    }

    [Fact]
    public void Flags_an_unreadable_archive_as_a_blocker()
    {
        using var g = GameFixture.Create();
        g.File("game/archive.rpa", "RPA-3.0 not a real header at all\ngarbage");

        var r = Scan(g);

        Assert.Contains(r.Blockers, b => b.Code == "keyed-archive");
        Assert.True(r.HasBlockers);
    }

    [Fact]
    public void Flags_obfuscated_bytecode_when_there_is_no_source()
    {
        using var g = GameFixture.Create();
        for (int i = 0; i < 12; i++)
            g.Bytes($"game/scene{i}.rpyc", 64); // zeroed — no RENPY magic

        var r = Scan(g);

        Assert.Contains(r.Blockers, b => b.Code == "obfuscated-bytecode");
    }

    [Fact]
    public void Clean_bytecode_with_matching_source_is_fine()
    {
        using var g = GameFixture.Create();
        for (int i = 0; i < 6; i++)
        {
            g.File($"game/scene{i}.rpy", "label start:\n    return");
            var p = g.At($"game/scene{i}.rpyc");
            File.WriteAllBytes(p, System.Text.Encoding.ASCII.GetBytes("RENPY RPC2\x00\x00\x00"));
        }

        var r = Scan(g);

        Assert.DoesNotContain(r.Blockers, b => b.Code == "obfuscated-bytecode");
    }

    [Fact]
    public void Warns_about_subprocess_and_native_modules()
    {
        using var g = GameFixture.Create();
        g.File("game/script.rpy", "init python:\n    import subprocess\n    subprocess.call(['x'])");
        g.Bytes("game/native/_engine.pyd", 32);

        var r = Scan(g);

        Assert.Contains(r.Warnings, w => w.Code == "api-subprocess");
        Assert.Contains(r.Warnings, w => w.Code == "native-module");
    }
}
