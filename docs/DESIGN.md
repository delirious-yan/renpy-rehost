# Design — Ren'Py Rehost

A local, one-click tool that turns a shipped Ren'Py game into a browser web build and
serves it, so it plays in an ordinary browser tab instead of the native engine.

## Premise & non-goals

- **Local, single-user, offline-capable.** The tool runs on the player's machine
  against a game they already have. No hosting, no pre-converted library, no network
  except fetching Ren'Py SDKs from renpy.org.
- **Covers the realistic majority**, not literally every game. Target: Ren'Py **7.4+
  and 8.x**, unobfuscated bytecode, unkeyed archives. Everything else gets a clear
  diagnostic, not a silent failure.
- **The whole shebang:** version-matched headless rebuild + an asset-optimising
  pipeline (image downscale, audio/video transcode, original-archive streaming) so
  multi-GB games actually run, + a GUI.

## Why local hosting changes the size math

A 12 GB VN is 12 GB because of high-res art across hundreds of scenes — not because any
single scene needs 12 GB of RAM. The three cost centres, separated:

| Limit | Local hosting? | How we handle it |
| --- | --- | --- |
| **Transfer** — GBs before you can play | Gone — localhost is disk-speed | Serve assets straight off disk |
| **Working set** — what the WASM runtime holds at once (~2 GB practical, 4 GB wasm32 wall) | Not relieved — engine property | Build-time image downscale + audio recompress + memory-hook injection + progressive streaming |
| **Codecs** — H.264/MP4 movie sprites | Not relieved | Build-time transcode to VP9 WebM |

Net: with the asset pipeline, **total game size stops being the blocker.** The residual
risk is heap accumulation over a long play session — mitigated by injected
`renpy.free_memory()` hooks, and if that's not enough for the very biggest games,
chapter-split builds (P5).

## Components

```
RenpyRehost.sln
├─ RenpyRehost.Core        class library — the whole pipeline, no UI
├─ RenpyRehost.Cli         `rehost` console app — thin arg parser over Core
├─ RenpyRehost.App         WinForms GUI — drop zone, progress log, a Library tab
└─ RenpyRehost.Tests       xUnit — stage-level tests + a small conversion corpus
```

### Dependencies

| Thing | Purpose |
| --- | --- |
| `Magick.NET-Q8-AnyCPU` (NuGet) | all image work — decode/encode/resize PNG·JPG·WebP·AVIF uniformly (GDI+ needs OS codecs for WebP; System.Drawing is Windows-only anyway) |
| `ffmpeg` (to bundle, `tools/ffmpeg/`) | audio recompress, video transcode — P2 follow-up |
| Ren'Py SDK + web support | the actual build — downloaded per-version, cached (see below) |

No bundled Python — the build runs on the SDK's own runtime via `renpy.exe` /
`renpy.sh` (Python 2 for the 7.4 line, Python 3 for 8.x).

### Where working data lives

If your clone of this repo sits in a synced folder (OneDrive, Dropbox, etc.), nothing
large or transient should go in it. Defaults (overridable via CLI flags / GUI settings):

- SDK cache: `%LOCALAPPDATA%\RenpyRehost\sdk\<version>\`
- Scratch (reconstructed projects, intermediate output): `%LOCALAPPDATA%\RenpyRehost\work\`
- Final web build: `<cwd>\<name>-web\` or `--out`
- Source games: left wherever the user downloaded them

## The pipeline (Core)

Each stage is an `IPipelineStage` with `CanSkip`, `Run(ConversionContext)`, and progress
reporting. The context carries paths, the detected version, the diagnostic report, and
options.

```
1. Ingest          locate game/ inside a dropped folder / .zip / .exe
2. Detect          engine version (vc_version.py / renpy/__init__.py / log.txt / lib dirs)
3. Preflight       size profile + web-target check -> ConversionReport. May hard-stop.
4. AcquireSdk      download+cache renpy-<v>-sdk.zip (strip top folder) and
                   renpy-<v>-web.zip (keep web/); resume + SHA-256 verify
5. Reconstruct     fresh project; clone game/ (scripts copied, media/.rpa hard-linked);
                   UNPACK .rpa archives to loose files (own RPA reader, no Python)
6. AssetPipeline   (P2, opt-in per report)
     6a. images    downscale > target px, re-encode; keep originals for streaming
     6b. audio     WAV/FLAC -> Opus
     6c. video     non-WebM / H.264 -> VP9 WebM   (note: RP 7.4 web strips ALL video)
     6d. archives  decide bundle-vs-stream per .rpa; large ones stay on disk
7. Build           inject a launcher command, run `renpy.exe <sdk>/launcher rehost_web
                   <proj>` (SDL_*=dummy); RP 7.4 has no web_build CLI
8. Assemble        move/copy the web build to --out, write rehost.json, clean scratch
                   (P2: mix in streamed archives + manifest)
9. Serve           LocalWebServer (HttpListener), or emit the folder for something
                   else to host
```

Stages 1–5, 7–9 are the spine (P0). Stage 6 is the asset pipeline (P2). Preflight's
deep scans (obfuscation, keyed archives, problem APIs) are P3.

### RPA unpacking (Reconstruct)

A shipped `.rpa` is included in the web build *whole* by Ren'Py's distributor, which
defeats progressive download — the streamer can only swap out individual images/audio
when they sit loose. So Reconstruct unpacks every `.rpa` first. `Rpa/RpaArchive.cs`
reads RPA 2.0/3.0/3.2 (header line -> zlib'd pickle index -> XOR-deobfuscated
offset/length per entry); `Rpa/Pickle.cs` is a ~40-opcode mini-unpickler for exactly
the dict-of-tuples an index is. No Python, no bundled `unrpa`. A custom-key archive is
a preflight blocker.

### ConversionReport

Preflight produces a structured verdict the GUI renders and the CLI prints:

- `EngineVersion`, `WebTargetNative` (bool — false for <7.4)
- `Blockers[]` — keyed archives, obfuscated `.rpyc`, native `.pyd`/`.so`, `ctypes` use
- `Warnings[]` — H.264 video count, `subprocess`/`renpy.run` calls, achievement hooks,
  uncompressed-audio MB, estimated peak working set
- `SizeProfile` — total, per-`.rpa`, image/audio/video breakdown
- `Recommendation` — `Go` | `GoWithPipeline` | `NeedsPortForward` | `Stop`

## Key mechanisms

**SDK cache.** `%LOCALAPPDATA%\RenpyRehost\sdk\<version>\` holds the unpacked SDK;
a `web-support.ok` marker means the web files are installed. Files come from
`https://www.renpy.org/dl/<version>/renpy-<version>-sdk.zip` and
`renpy-<version>-web.zip`. Downloads are size/hash-checked against a small pinned
manifest (`Core/Resources/sdk-manifest.json`) we maintain; unknown versions fall
back to downloading with a warning.

**Headless web_build.** `renpy.py launcher web_build <basedir> --destination <dir>`.
Requires web support pre-installed (we do that in AcquireSdk by unpacking
`renpy-<v>-web.zip` into the SDK) and `progressive_download.txt` present in the project
(we write it in Reconstruct). Audio init can crash headless builds on some versions —
run with `SDL_AUDIODRIVER=dummy`.

**Original-archive streaming.** Ren'Py web progressive download range-requests assets.
For archives above a threshold we leave the `.rpa` out of the web bundle and serve the
game's real file over localhost; the manifest points the runtime at it. Files >50 MB
aren't browser-cached — locally irrelevant, they re-read from disk.

**Memory hooks.** Reconstruct injects a small `.rpy` that calls `renpy.free_memory()`
on label-callback and lowers `config.image_cache_size` for the web platform only.

**Launching a build.** `GameLauncher` opens a served build in a browser — the system
default, or a preferred one set via `RENPY_REHOST_BROWSER`, or a browser picked per
run (`rehost play --with <exe>`, or the GUI's "Choose browser" button, which reads
installed browsers from the registry's `StartMenuInternet` client list).

## Legal line

The tool is a local format converter operated by the user on their own game. It must
not gain a "publish" or "share build" feature, and ships with no game content.
