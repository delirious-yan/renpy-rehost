using RenpyRehost.Cli;
using RenpyRehost.Core;

// Hand-rolled arg handling for now — small surface, no dependency. Swap for
// System.CommandLine if the flag set grows.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

string command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "detect" => CmdDetect(rest),
        "preflight" => await CmdConvert(rest, preflightOnly: true),
        "convert" => await CmdConvert(rest, preflightOnly: false),
        "serve" => await CmdServe(rest),
        "play" => await CmdPlay(rest),
        "library" or "lib" => CmdLibrary(rest),
        "clean" => CmdClean(rest),
        "gui" => CmdGui(),
        "version" or "--version" => PrintVersion(),
        _ => Fail($"Unknown command '{command}'. Try `rehost help`."),
    };
}
catch (RehostException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int CmdDetect(string[] args)
{
    if (args.Length == 0) return Fail("Usage: rehost detect <game folder or .exe>");
    if (!Directory.Exists(args[0]) && !File.Exists(args[0]))
        return Fail($"Path not found: {args[0]}");
    var d = VersionDetector.Detect(args[0]);
    foreach (var e in d.Evidence) Console.WriteLine($"  {e}");
    Console.WriteLine();
    if (d.Version is { } v)
    {
        Console.WriteLine($"Ren'Py {v}  (source: {d.Source})");
        Console.WriteLine($"web target native: {(v.SupportsWebTarget ? "yes" : "no — needs port-forward")}");
        Console.WriteLine($"SDK: https://www.renpy.org/dl/{v.DownloadKey}/");
        return 0;
    }
    Console.WriteLine($"Version not pinned (best source: {d.Source}). Run the game once for log.txt, "
        + "or pass --renpy-version x.y.z to `convert`.");
    return 2;
}

static async Task<int> CmdConvert(string[] args, bool preflightOnly)
{
    if (args.Length == 0 || args[0].StartsWith('-'))
        return Fail($"Usage: rehost {(preflightOnly ? "preflight" : "convert")} <game folder or .exe> [options]");

    string source = args[0];
    var opt = ParseOptions(args.Skip(1).ToArray());

    string appRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenpyRehost");
    string outputDir = opt.Out ?? Path.Combine(Directory.GetCurrentDirectory(),
        Path.GetFileNameWithoutExtension(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + "-web");

    var options = new ConversionOptions
    {
        SdkCacheDir = opt.SdkCache ?? Path.Combine(appRoot, "sdk"),
        ToolsCacheDir = Path.Combine(appRoot, "tools"),
        WorkDir = opt.Work ?? Path.Combine(appRoot, "work"),
        OutputDir = outputDir,
        Serve = opt.Serve,
        ServePort = opt.Port,
        EmitOnly = opt.Emit,
        OfflineOnly = opt.Offline,
        VersionOverride = opt.VersionOverride,
        Title = opt.Title,
        ReuseProject = opt.ReuseProject,
        KeepWork = opt.KeepWork,
        Force = opt.Force || preflightOnly, // preflight just reports; it never hard-stops
        AssetPipeline = opt.Assets,
        ImageMaxWidth = opt.ImageWidth ?? 1920,
        Verbose = opt.Verbose,
    };
    bool verbose = opt.Verbose;

    var sink = new ConsoleProgressSink(verbose);
    var ctx = new ConversionContext(Path.GetFullPath(source), options, sink);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    if (preflightOnly)
    {
        var runner = new PipelineRunner(new IPipelineStage[]
        {
            new RenpyRehost.Core.Stages.IngestStage(),
            new RenpyRehost.Core.Stages.DetectStage(),
            new RenpyRehost.Core.Stages.PreflightStage(),
        });
        var r = await runner.RunAsync(ctx, cts.Token);
        if (ctx.Report is { } rep) PrintReport(rep);
        return r.Success ? 0 : 1;
    }

    var result = await PipelineRunner.Default().RunAsync(ctx, cts.Token);
    Console.WriteLine();
    if (!result.Success)
    {
        Console.Error.WriteLine($"failed at {result.FailedStage}: {result.Error?.Message}");
        return 1;
    }

    Console.WriteLine($"done in {result.Elapsed.TotalSeconds:0.0}s");
    if (ctx.Notes.TryGetValue("size", out var sz))
        Console.WriteLine($"  port size: {sz}   (the web build only — not the original game)");
    if (ctx.Notes.TryGetValue("assets", out var ap)) Console.WriteLine($"  assets: {ap}");

    if (ctx.ServeDir is { } built)
    {
        try
        {
            var lib = Library.Load();
            lib.AddOrUpdate(built, ctx.SourcePath);
            lib.Save();
            Console.WriteLine($"  added to library ({Library.DefaultPath})");
        }
        catch { /* library is a convenience — never fail the convert over it */ }
    }

    if (ctx.ServeUrl is { } url && ctx.RunningServer is { } server)
    {
        Console.WriteLine($"serving at {url}  —  press Ctrl+C to stop");
        TryOpenBrowser(url);
        try { await Task.Delay(Timeout.Infinite, cts.Token); }
        catch (OperationCanceledException) { }
        await server.DisposeAsync();
        Console.WriteLine("server stopped.");
    }
    else if (ctx.ServeDir is { } dir)
    {
        Console.WriteLine($"web build: {dir}");
        Console.WriteLine($"play it with:   rehost play \"{dir}\"");
    }
    return 0;
}

static ConvertArgs ParseOptions(string[] args)
{
    var o = new ConvertArgs();
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-v" or "--verbose": o.Verbose = true; break;
            case "--serve": o.Serve = true; break;
            case "--emit": o.Emit = true; break;
            case "--offline": o.Offline = true; break;
            case "--force": o.Force = true; break;
            case "--reuse-project": o.ReuseProject = true; break;
            case "--keep-work": o.KeepWork = true; break;
            case "--out": o.Out = Next(args, ref i); break;
            case "--sdk-cache": o.SdkCache = Next(args, ref i); break;
            case "--work": o.Work = Next(args, ref i); break;
            case "--port": o.Port = int.Parse(Next(args, ref i)); o.Serve = true; break;
            case "--image-width": o.ImageWidth = int.Parse(Next(args, ref i)); break;
            case "--title": o.Title = Next(args, ref i); break;
            case "--assets":
                o.Assets = Next(args, ref i).ToLowerInvariant() switch
                {
                    "off" => AssetPipelineMode.Off,
                    "auto" => AssetPipelineMode.Auto,
                    "force" => AssetPipelineMode.Force,
                    var x => throw new RehostException($"--assets expects off|auto|force, got '{x}'"),
                };
                break;
            case "--renpy-version":
                o.VersionOverride = RenpyVersion.Parse(Next(args, ref i))
                    ?? throw new RehostException("--renpy-version expects x.y or x.y.z");
                break;
            default: throw new RehostException($"Unknown option '{args[i]}'.");
        }
    }
    return o;

    static string Next(string[] a, ref int i)
    {
        if (i + 1 >= a.Length) throw new RehostException($"'{a[i]}' needs a value.");
        return a[++i];
    }
}

static void PrintReport(ConversionReport r)
{
    Console.WriteLine();
    Console.WriteLine("── preflight ─────────────────────────────");
    Console.WriteLine($"engine         Ren'Py {r.EngineVersion}");
    Console.WriteLine($"web target     {(r.WebTargetNative ? "native" : "not native — needs port-forward")}");
    Console.WriteLine($"size           {Humanize.Bytes(r.Size.Total)} total "
        + $"(img {Humanize.Bytes(r.Size.Images)} · aud {Humanize.Bytes(r.Size.Audio)} · vid {Humanize.Bytes(r.Size.Video)})");
    if (r.Size.Archives.Count > 0)
        Console.WriteLine($"archives       {r.Size.Archives.Count} .rpa ({Humanize.Bytes(r.Size.Archives.Values.Sum())})");
    Console.WriteLine($"recommendation {r.Recommendation}");
    foreach (var f in r.Blockers) Console.WriteLine($"  BLOCK  [{f.Code}] {f.Message}");
    foreach (var f in r.Warnings) Console.WriteLine($"  warn   [{f.Code}] {f.Message}");
    Console.WriteLine("──────────────────────────────────────────");
}

static async Task<int> CmdServe(string[] args)
{
    if (args.Length == 0) return Fail("Usage: rehost serve <web build folder> [--port N]");
    string dir = args[0];
    if (!Directory.Exists(dir)) return Fail($"Folder not found: {dir}");
    if (!File.Exists(Path.Combine(dir, "index.html")))
        return Fail($"{dir} doesn't look like a web build (no index.html).");

    int? port = null;
    for (int i = 1; i < args.Length; i++)
        if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);

    await using var server = LocalWebServer.Start(dir, port);
    string url = server.Url + "index.html";
    Console.WriteLine($"serving {dir}");
    Console.WriteLine($"  {url}  —  press Ctrl+C to stop");
    TryOpenBrowser(url);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
    return 0;
}

static async Task<int> CmdPlay(string[] args)
{
    if (args.Length == 0)
        return Fail("Usage: rehost play <web build folder | library number> [--port N] [--with <browser exe>]");

    string? browserExe = null;
    int? port = null;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
        else if (args[i] == "--with" && i + 1 < args.Length) browserExe = args[++i];
    }

    string dir = args[0];
    if (int.TryParse(dir, out var n))
    {
        var lib = Library.Load();
        if (n < 1 || n > lib.Entries.Count) return Fail($"No library entry #{n}. Run `rehost library`.");
        dir = lib.Entries[n - 1].Path;
    }
    if (!Directory.Exists(dir)) return Fail($"Folder not found: {dir}");
    if (!File.Exists(Path.Combine(dir, "index.html")))
        return Fail($"{dir} doesn't look like a web build (no index.html).");

    await using var server = LocalWebServer.Start(dir, port);
    string url = server.Url + "index.html";
    if (browserExe is null) GameLauncher.Open(url); else GameLauncher.OpenWith(url, browserExe);
    Console.WriteLine($"serving {dir}");
    Console.WriteLine($"  {url}  —  opened in your browser");
    Console.WriteLine("  press Ctrl+C to stop the server");

    try
    {
        var lib = Library.Load();
        lib.MarkPlayed(Path.GetFullPath(dir));
        lib.Save();
    }
    catch { }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
    return 0;
}

static int CmdLibrary(string[] args)
{
    var lib = Library.Load();

    if (args.Length > 0 && args[0] is "add")
    {
        if (args.Length < 2) return Fail("Usage: rehost library add <web build folder>");
        var e = lib.AddOrUpdate(args[1]);
        lib.Save();
        Console.WriteLine($"added: {e.Title}  ({e.Path})");
        return 0;
    }
    if (args.Length > 0 && args[0] is "remove" or "rm")
    {
        if (args.Length < 2) return Fail("Usage: rehost library remove <folder | number>");
        string target = args[1];
        if (int.TryParse(target, out var n) && n >= 1 && n <= lib.Entries.Count)
            target = lib.Entries[n - 1].Path;
        bool removed = lib.Remove(target);
        lib.Save();
        Console.WriteLine(removed ? "removed." : "not in the library.");
        return removed ? 0 : 1;
    }
    if (args.Length > 0 && args[0] is "move" or "mv")
    {
        if (args.Length < 3)
            return Fail("Usage: rehost library move <folder | number> <appdata | game [<game folder>] | destination folder>");

        var entry = int.TryParse(args[1], out var mn) && mn >= 1 && mn <= lib.Entries.Count
            ? lib.Entries[mn - 1]
            : lib.Entries.FirstOrDefault(e =>
                string.Equals(e.Path, Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase));
        if (entry is null) return Fail($"Not in the library: {args[1]}");

        string destParent;
        switch (args[2].ToLowerInvariant())
        {
            case "appdata":
                destParent = Library.AppOutDir;
                break;
            case "game":
                destParent = GameFolderOf(entry) ?? (args.Length >= 4 ? Path.GetFullPath(args[3]) : null)
                    ?? throw new RehostException(
                        $"Don't know where \"{entry.Title}\" came from. Name the game folder:\n"
                        + $"  rehost library move {args[1]} game \"<path to the game folder>\"");
                if (GameFolderOf(entry) is null) entry.SourcePath = destParent; // remember it
                break;
            default:
                destParent = args[2];
                break;
        }

        var bar = new InlineProgress<Library.MoveProgress>(p =>
        {
            if (p.Instant) return;
            Console.Write($"\r  copying… {p.Fraction * 100,5:0.0}%  ({Humanize.Bytes(p.BytesDone)} / {Humanize.Bytes(p.BytesTotal)})   ");
        });
        string moved = lib.Move(entry.Path, destParent, bar);
        lib.Save();
        Console.WriteLine($"\rmoved: {moved}".PadRight(60));
        return 0;
    }

    if (lib.Entries.Count == 0)
    {
        Console.WriteLine("Library is empty. `rehost convert ...` adds builds automatically,");
        Console.WriteLine("or `rehost library add <folder>` to register an existing one.");
        return 0;
    }

    int i = 1;
    foreach (var e in lib.Entries)
    {
        string tag = e.Exists ? "" : "  (missing)";
        Console.WriteLine($"{i,2}. {e.Title}{tag}");
        Console.WriteLine($"    {e.Path}");
        Console.WriteLine($"    Ren'Py {e.SourceVersion ?? "?"} → built with {e.BuiltWith ?? "?"} · {Humanize.Bytes(e.SizeBytes)}"
            + (e.LastPlayedUtc is { } lp ? $" · last played {DateTimeOffset.Parse(lp).LocalDateTime:yyyy-MM-dd}" : ""));
        i++;
    }
    Console.WriteLine();
    Console.WriteLine("Play one with:  rehost play <number>");
    return 0;
}

// The folder to drop a build into when "move to the game" is asked for.
static string? GameFolderOf(LibraryEntry e)
{
    if (string.IsNullOrWhiteSpace(e.SourcePath)) return null;
    if (Directory.Exists(e.SourcePath)) return e.SourcePath;
    if (File.Exists(e.SourcePath)) return Path.GetDirectoryName(e.SourcePath);
    return null;
}

static int CmdClean(string[] args)
{
    string appRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenpyRehost");
    string workDir = appRoot + "\\work";
    for (int i = 0; i < args.Length; i++)
        if (args[i] == "--work" && i + 1 < args.Length) workDir = args[++i];
    bool dryRun = args.Contains("--dry-run") || args.Contains("-n");

    var (bytes, items) = Housekeeping.SurveyWork(workDir);
    if (items == 0)
    {
        Console.WriteLine($"No build scratch under {workDir} — nothing to clean.");
        return 0;
    }

    Console.WriteLine($"{Humanize.Bytes(bytes)} of build scratch in {items} folder(s) under {workDir}");
    Console.WriteLine("(reconstructed projects + intermediate build output — finished builds and the SDK cache are untouched)");

    if (dryRun) { Console.WriteLine("dry run — nothing deleted."); return 0; }

    long freed = Housekeeping.CleanAllWork(workDir);
    Console.WriteLine($"reclaimed {Humanize.Bytes(freed)}.");
    return 0;
}

static int CmdGui()
{
    string[] candidates =
    {
        Path.Combine(AppContext.BaseDirectory, "RenpyRehost.exe"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "RenpyRehost.App", "bin", "Release", "net8.0-windows", "RenpyRehost.exe"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "RenpyRehost.App", "bin", "Debug", "net8.0-windows", "RenpyRehost.exe"),
    };
    string? exe = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    if (exe is null)
        return Fail("Couldn't find RenpyRehost.exe next to the CLI. Run publish.cmd to build both.");

    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
    return 0;
}

static void TryOpenBrowser(string url)
{
    try { GameLauncher.Open(url); }
    catch { /* headless / no browser — the URL is printed anyway */ }
}

static int PrintVersion()
{
    var v = typeof(Program).Assembly.GetName().Version;
    Console.WriteLine($"rehost {v?.ToString(3)}");
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        rehost — local Ren'Py -> web converter

        USAGE
          rehost detect     <game folder or .exe>
          rehost preflight  <game folder or .exe> [--renpy-version x.y.z]
          rehost convert    <game folder or .exe> [options]
          rehost serve      <web build folder> [--port N]
          rehost play       <web build folder | library number> [--with <browser exe>]
          rehost library    [add <folder> | remove <folder|number>
                            | move <folder|number> <appdata | game [<game folder>] | folder>]
          rehost clean      [--dry-run] [--work <dir>]   delete leftover build scratch
          rehost gui        open the desktop app

        CONVERT OPTIONS
          --out <dir>           web build output (default ./<name>-web)
          --serve              start a local server and open a browser
          --port <n>            fixed server port (implies --serve)
          --emit               produce the folder only — no server (host it yourself, or `rehost play` it later)
          --renpy-version x.y.z force the engine version (no log.txt / old build)
          --title "Name"       display title for rehost.json / PWA (default: from folder)
          --assets off|auto|force  asset pipeline (default auto — downscales big images)
          --image-width <px>   max image width for the downscale pass (default 1920)
          --reuse-project      skip Reconstruct if the project clone already exists (iterating on Build)
          --keep-work          keep the project clone + build scratch afterwards (default: delete, ~= the port size again)
          --force              proceed past preflight blockers (keyed archives, obfuscated scripts)
          --sdk-cache <dir>    SDK download cache (default %LOCALAPPDATA%\RenpyRehost\sdk)
          --work <dir>          scratch dir (default %LOCALAPPDATA%\RenpyRehost\work)
          --offline            never hit the network; require a cached SDK
          -v, --verbose        show command lines and per-file detail

        Set RENPY_REHOST_BROWSER to a browser .exe path to make `play`/`convert --serve`
        prefer it over the system default. `rehost play --with <exe>` picks one per run.

        Local, single-user use only. See ROADMAP.md for what's implemented.
        """);
}

// IProgress<T> that runs the handler inline on the reporting thread — for a CLI
// that reports from its own thread, this keeps status lines in order.
sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

sealed class ConvertArgs
{
    public string? SdkCache, Work, Out, Title;
    public bool Serve, Emit, Offline, Verbose, ReuseProject, KeepWork, Force;
    public int? Port, ImageWidth;
    public RenpyVersion? VersionOverride;
    public AssetPipelineMode Assets = AssetPipelineMode.Auto;
}

// Needed for typeof(Program) in PrintVersion under top-level statements.
internal sealed partial class Program;
