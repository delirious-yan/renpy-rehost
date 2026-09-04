using System.Text.RegularExpressions;

namespace RenpyRehost.Core.Stages;

/// <summary>
/// Runs the headless web build. Ren'Py 7.4 has no <c>web_build</c> CLI command —
/// only the launcher's internal <c>build_web(project)</c> — so we drop a tiny
/// command-registration script into the SDK's launcher and invoke that.
/// </summary>
public sealed class BuildStage : IPipelineStage
{
    public string Name => "Build";

    // Injected into <sdk>/launcher/game/. Registers `rehost_web <project>` which
    // calls the launcher's own build_web() with the dev server stubbed out.
    private const string CommandScript = """
        # Added by Ren'Py Rehost. Registers a headless web-build command.
        init python:
            def _rehost_web_command():
                ap = renpy.arguments.ArgumentParser(description="Ren'Py Rehost headless web build.")
                ap.add_argument("project", help="Path to the project directory.")
                args = ap.parse_args()

                import webserver
                webserver.start = lambda *a, **kw: None

                p = project.Project(args.project)
                build_web(p, gui=False)
                return False

            renpy.arguments.register_command("rehost_web", _rehost_web_command)
        """;

    public async Task<StageOutcome> RunAsync(ConversionContext ctx, CancellationToken ct)
    {
        string sdkRoot = ctx.SdkDir ?? throw new RehostException("Build has no SDK.");
        string projDir = ctx.ProjectDir ?? throw new RehostException("Build has no reconstructed project.");
        var layout = new SdkLayout(sdkRoot);
        bool nativeCommand = ctx.Version?.HasWebBuildCommand == true;

        // 8.2+ has a real `web_build` CLI command. Older lines only expose the
        // launcher's internal build_web(), so we inject a command that calls it.
        string[] renpyArgs;
        if (nativeCommand)
        {
            ctx.Progress.Detail("using the SDK's native web_build command");
            renpyArgs = new[] { layout.LauncherDir, "web_build", projDir };
        }
        else
        {
            string scriptPath = Path.Combine(layout.LauncherGameDir, "zzrehost.rpy");
            await File.WriteAllTextAsync(scriptPath, CommandScript.ReplaceLineEndings("\n"), ct).ConfigureAwait(false);
            string scriptPyc = Path.ChangeExtension(scriptPath, ".rpyc");
            if (File.Exists(scriptPyc)) File.Delete(scriptPyc); // stale compiled copy would shadow our edits
            ctx.Progress.Detail("injected a rehost_web command (SDK has no native web_build)");
            renpyArgs = new[] { layout.LauncherDir, "rehost_web", projDir };
        }

        var env = new Dictionary<string, string>
        {
            ["SDL_AUDIODRIVER"] = "dummy",
            ["SDL_VIDEODRIVER"] = "dummy",
            ["RENPY_LESS_MEMORY"] = "1",
        };

        ctx.Progress.Info("Running the Ren'Py web build (this can take a while on a large game)...");

        var lastLine = "";
        var stepRx = new Regex(@"^(?<label>.+?)\s*-\s*(?<n>\d+)\s+of\s+(?<total>\d+)\s*$", RegexOptions.Compiled);
        string lastPhase = "";
        var result = await ProcessRunner.RunAsync(
            layout.RenpyRunner,
            renpyArgs,
            workingDir: sdkRoot,
            env,
            onLine: line =>
            {
                line = line.TrimEnd();
                if (line.Length == 0) return;
                lastLine = line;

                var m = stepRx.Match(line);
                if (m.Success)
                {
                    string label = m.Groups["label"].Value.Trim();
                    if (label != lastPhase) { lastPhase = label; ctx.Progress.Info(label); }
                    ctx.Progress.SubProgress(label,
                        double.Parse(m.Groups["n"].Value) / double.Parse(m.Groups["total"].Value));
                }
                else if (Regex.IsMatch(line, @"^(Preparing|Scanning|Building|Copying|Ren'Py|I:|W:|E:|Traceback|\w+Error:)"))
                {
                    ctx.Progress.Info(line);
                }
                else
                {
                    ctx.Progress.Detail(line);
                }
            },
            ct).ConfigureAwait(false);

        if (!result.Ok)
        {
            string all = result.StdOut + result.StdErr;
            if (all.Contains("BadZipfile") || all.Contains("Bad magic number for central directory"))
                throw new RehostException(
                    "the intermediate game.zip grew past ~4 GB and this SDK's build tooling can't handle it "
                    + "(Python 2 zip64 limit on the Ren'Py 7.x line).\n"
                    + "This game needs the asset pipeline (P2 — downscale images to shrink it) and/or a "
                    + "port to an 8.x SDK (P4). Try `--renpy-version 8.3.7`.\n"
                    + $"--- last output ---\n{result.Tail(20)}");
            throw new RehostException(
                $"web build exited with code {result.ExitCode}.\n--- last output ---\n{result.Tail(25)}");
        }

        string webDir = FindWebOutput(projDir, ctx)
            ?? throw new RehostException(
                "web build finished but no '*-web' output folder was found near "
                + $"{Path.GetDirectoryName(projDir)}.\nlast line: {lastLine}");

        // A web build must contain at least the runtime + the game payload.
        if (!File.Exists(Path.Combine(webDir, "index.html")) ||
            !File.Exists(Path.Combine(webDir, "game.zip")))
            throw new RehostException($"web output at {webDir} is missing index.html / game.zip.");

        ctx.WebBuildDir = webDir;
        long size = DirSize(webDir);
        ctx.Notes["build"] = $"web output {Humanize.Bytes(size)} at {webDir}";
        return StageOutcome.Completed(ctx.Notes["build"]);
    }

    private static string? FindWebOutput(string projDir, ConversionContext ctx)
    {
        string parent = Path.GetDirectoryName(projDir)!;
        // get_web_destination: <parent>/<build.destination>/<dir>-web — search a couple levels.
        var candidates = new List<string>();
        foreach (var dir in SafeDirs(parent, "*-web", SearchOption.AllDirectories))
            candidates.Add(dir);
        // also right beside the project
        foreach (var dir in SafeDirs(Path.GetDirectoryName(parent) ?? parent, "*-web", SearchOption.TopDirectoryOnly))
            candidates.Add(dir);

        return candidates
            .Distinct()
            .Where(d => File.Exists(Path.Combine(d, "index.html")))
            .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
            .FirstOrDefault();
    }

    private static IEnumerable<string> SafeDirs(string root, string pattern, SearchOption opt)
    {
        try { return Directory.EnumerateDirectories(root, pattern, opt); }
        catch { return Array.Empty<string>(); }
    }

    private static long DirSize(string dir)
    {
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { total += new FileInfo(f).Length; } catch { }
        return total;
    }
}
