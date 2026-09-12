using RenpyRehost.Core;

namespace RenpyRehost.Tests;

public class LogTests
{
    [Fact]
    public void Start_creates_a_file_under_appRoot_logs_and_WriteLine_lands_in_it()
    {
        using var g = GameFixture.Create();
        string appRoot = g.At("app");

        using (var log = Log.Start("convert-mygame", appRoot))
        {
            Assert.StartsWith(Log.LogDir(appRoot), log.Path);
            Assert.EndsWith("_convert-mygame.log", log.Path);
            log.WriteLine("hello");
        }

        string text = File.ReadAllText(Directory.GetFiles(Log.LogDir(appRoot)).Single());
        Assert.Contains("=== convert-mygame", text);
        Assert.Contains("hello", text);
        Assert.Contains("=== done ===", text);
    }

    [Fact]
    public void Exception_records_message_and_stack_trace_for_the_whole_inner_chain()
    {
        using var g = GameFixture.Create();
        string appRoot = g.At("app");

        Exception caught;
        try
        {
            try { throw new InvalidOperationException("inner boom"); }
            catch (Exception inner) { throw new RehostException("outer boom", inner); }
        }
        catch (Exception ex) { caught = ex; }

        using var log = Log.Start("op", appRoot);
        log.Exception("something failed", caught);
        log.Dispose(); // flush before reading

        string text = File.ReadAllText(Directory.GetFiles(Log.LogDir(appRoot)).Single());
        Assert.Contains("ERROR — something failed", text);
        Assert.Contains("outer boom", text);
        Assert.Contains("inner boom", text);
    }

    [Fact]
    public void Sanitizes_the_operation_name_for_the_filename()
    {
        using var g = GameFixture.Create();
        string appRoot = g.At("app");

        using var log = Log.Start("convert C:\\weird/name*?", appRoot);

        string filename = Path.GetFileName(log.Path);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => filename.Contains(c));
    }

    [Fact]
    public void Prune_keeps_only_the_newest_N_log_files()
    {
        using var g = GameFixture.Create();
        string appRoot = g.At("app");

        for (int i = 0; i < 5; i++)
        {
            Log.Start($"op{i}", appRoot, keep: 3).Dispose();
            Thread.Sleep(15); // filenames + mtimes need to actually differ
        }

        var remaining = Directory.GetFiles(Log.LogDir(appRoot));
        Assert.Equal(3, remaining.Length);
        // the 3 most recent (op2, op3, op4) survive; op0/op1 were pruned
        Assert.Contains(remaining, f => f.Contains("op4"));
        Assert.DoesNotContain(remaining, f => f.Contains("op0"));
    }

    [Fact]
    public void A_missing_writable_directory_does_not_throw()
    {
        // Point appRoot somewhere that can't be created (a file, not a folder) —
        // Start() must degrade gracefully rather than take the caller down with it.
        using var g = GameFixture.Create();
        string blocker = g.At("blocked");
        File.WriteAllText(blocker, "not a directory");

        var log = Log.Start("op", blocker);
        log.WriteLine("should not throw");
        log.Exception("neither should this", new Exception("x"));
        log.Dispose();
    }
}
