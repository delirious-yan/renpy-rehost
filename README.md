# Ren'Py Rehost

A local tool that turns a Ren'Py game you own into a browser **Web (WASM)** build and
serves it — so it plays in an ordinary browser tab instead of the native engine. No
hosting, no shared library of converted games: point it at a game folder on your own
machine and it hands you back a build you can play, or serve to yourself, locally.

Full scope: version-matched headless rebuild **+ an asset-optimising pipeline** (image
downscale, audio/video transcode, original-archive streaming over localhost) so
multi-GB games actually run, **+ a GUI**.

**Status:** working end-to-end. A 5.9 GB commercial VN (Ren'Py 7.4.11) converts to a
served, progressive-download web build on an 8.3.7 SDK, with its art transcoded and
video remuxed to WebM. See `CONVERSION-LOG.md` and `ROADMAP.md`.

**Stack:** .NET 8, Windows — `RenpyRehost.Core` + `rehost` CLI + WinForms `App`;
Magick.NET for images, ffmpeg (auto-downloaded) for video, per-version downloaded
Ren'Py SDK. 54 unit tests.

## Build it

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows.

```
git clone https://github.com/delirious-yan/renpy-rehost.git
cd renpy-rehost
dotnet build
```

`.\publish.cmd` produces portable single-file `dist\rehost.exe` (~90 MB) and
`dist\RenpyRehost.exe` — self-contained, no .NET install needed to run them.
`.\install.cmd` puts both in `%LOCALAPPDATA%\RenpyRehost\bin`, adds that to your PATH
(so `rehost` / `renpyrehost` work from any terminal), and drops a Start Menu shortcut.
ffmpeg and the Ren'Py SDKs download on first use into `%LOCALAPPDATA%\RenpyRehost\`.

## Use

```
rehost convert "C:\path\to\GameFolder" --serve
rehost convert "C:\path\to\GameFolder" --emit --out "C:\somewhere"   # folder only, no server
rehost serve   "C:\somewhere"
rehost play    "C:\somewhere"          # serve + open in a browser
rehost play    2                       # play library entry #2
rehost library                         # list converted builds
rehost library move 2 appdata          # relocate a build: appdata | game [<folder>] | a folder
rehost clean                           # delete leftover build scratch (keeps builds + SDKs)
rehost detect  "C:\path\to\GameFolder"
rehost preflight "C:\path\to\GameFolder"
rehost gui                             # open the desktop app
```

Or run `RenpyRehost.App` (WinForms): a **Convert** tab (drop a game folder, watch the
9 stages, it opens in your browser when done) and a **Library** tab (every build
you've made, one click to replay — Add… / Remove / Folder / **Move** a build to
`%LOCALAPPDATA%`, next to its original game, or any folder you pick, with a progress
bar for a cross-drive copy; **Clean up** deletes leftover build scratch; **Choose
browser** picks which installed browser to open a build in).

Each build's size shown in the library / on the result bar / after `convert` is the
**web port only** — not the original game. A successful `convert` deletes its own
scratch (the reconstructed project clone + Ren'Py's intermediate `-dists` output,
roughly the port size again) unless you pass `--keep-work` or `--reuse-project`.
`rehost clean` / the GUI's **Clean up** button sweep anything left from older or
interrupted runs. Downloaded SDKs and ffmpeg are never touched.

**Which browser:** `play` and the GUI open the system default browser by default. Set
`RENPY_REHOST_BROWSER` to a browser's `.exe` path to make that the preferred one
instead, pick one per run with `rehost play --with "<exe>"`, or use the GUI's
**Choose browser** button.

Key flags: `--renpy-version 8.3.7` (force the SDK — needed for games >~4 GB, which
hit a Python-2 zip limit on the 7.x line), `--assets off|auto|force`,
`--image-width 1920`, `--title "Name"`, `--keep-work` (don't delete build scratch),
`--reuse-project` (skip the clone+unpack when iterating — asset passes that already
ran are cached and skipped), `--force` (proceed past preflight blockers).

## Why

Ren'Py can export a **Web** target — the game compiled to WebAssembly, run by an
Emscripten build of the Python interpreter. That runs in any browser tab: no native
install, sandboxed, portable to a machine that doesn't have (or can't have) the
engine's native dependencies. A shipped game already carries everything the exporter
needs — scripts as `.rpyc` bytecode, assets as `.rpa` archives — this tool just does
the version-matched rebuild for you.

## The pipeline

```
game.exe  ->  unpack  ->  game/ (.rpyc + .rpa)
                          |
                Ren'Py SDK (matched version)
                          |
            Build Distributions -> [x] Web
                          |
          web/  (index.html + game.wasm + assets)
                          |
              http://localhost  ->  your browser
```

Version match is make-or-break: `.rpyc` bytecode is minor-version-bound. 8.2 bytecode
will not load on 8.1. Match exactly, get a clean build, then step the SDK up one point
release at a time if needed.

## Folder layout

### In the repo

| Path | What |
| --- | --- |
| `ROADMAP.md` | Phased build plan (P0–P5) with acceptance criteria |
| `docs/DESIGN.md` | Architecture — components, pipeline stages, key mechanisms |
| `docs/PIPELINE.md` | The manual playbook, failure modes, feasibility matrix |
| `src/` | .NET solution — `RenpyRehost.Core` / `.Cli` / `.App` |
| `tests/` | `RenpyRehost.Tests` (xUnit) |
| `tools/` | Helper scripts (version detection) |
| `CONVERSION-LOG.md` | Running log — one entry per game, every break and fix |

### Working data — NOT in the repo

If your clone sits in a synced folder (OneDrive, Dropbox, etc.), keep large/transient
data out of it — `%LOCALAPPDATA%\RenpyRehost\` instead (the CLI defaults there):

| Path | What |
| --- | --- |
| `%LOCALAPPDATA%\RenpyRehost\sdk\<version>\` | Downloaded Ren'Py SDKs + web support, cached per version |
| `%LOCALAPPDATA%\RenpyRehost\work\` | Reconstructed projects, intermediate build output (auto-cleaned after each convert) |
| `%LOCALAPPDATA%\RenpyRehost\out\` or `--out` | Final web builds |

Source games stay wherever you downloaded them — pass the path to `rehost`.

## Ground rules

Personal, local use only. Rebuilding a game **you own** to play it yourself is fine.
Hosting someone's commercial VN as a web build for other people is redistribution —
don't. This project ships no game content and won't gain a "publish" or "share build"
feature.

The MIT license below covers this tool's code. It grants you nothing over any game
you run it against — that's still whatever the original developer's terms say.

## License

MIT — see [`LICENSE`](LICENSE).
