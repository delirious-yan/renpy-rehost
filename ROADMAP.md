# Roadmap — Ren'Py Rehost

Full-scope build: local one-click Ren'Py-to-web with an asset-optimising pipeline and
a GUI. See `docs/DESIGN.md` for architecture.

Stack: .NET 8 (`RenpyRehost.Core` lib + `rehost` CLI + WinForms `App`), bundled ffmpeg,
per-version downloaded Ren'Py SDK.

---

## P0 — the spine (CLI, happy path)

**Goal:** `rehost convert <folder> --serve` takes a modern (8.x), unobfuscated, unkeyed,
sub-2-GB Ren'Py game to a running localhost web build with no manual steps.

- [x] Solution skeleton: Core / Cli / App / Tests
- [x] `Ingest` — find `game/` in a dropped folder (prefers real `game/` subfolder, one level down, loose game); `.zip` extract with path-traversal guard; dropped `.exe` -> its folder
- [x] `Detect` — `VersionDetector` (vc_version.py / renpy/__init__.py / log.txt banner / lib-dir era hint), walks up from a `game/` folder; `--renpy-version` override
- [x] `Preflight` (minimal) — web-target check, size profile (image/audio/video/archive split), Go/GoWithPipeline/NeedsPortForward verdict; deep scans deferred to P3
- [x] Pipeline plumbing — `IPipelineStage`, `PipelineRunner`, `IProgressSink`, `ConversionContext/Options/Report`, `RehostException`
- [x] CLI: `detect`, `preflight`, `convert` with `--out/--serve/--port/--emit/--renpy-version/--sdk-cache/--work/--offline/-v`
- [x] Tests: 31 passing — RenpyVersion, VersionDetector, IngestStage, ZipUtil, LocalWebServer, RpaArchive (synthetic RPA-3.0 round-trip)
- [x] `AcquireSdk` — downloads `renpy-<v>-sdk.zip` + `renpy-<v>-web.zip` from renpy.org/dl, caches (resumable, hash-checked), unpacks SDK (strip top folder) + web support (keep `web/`), verifies `web/hash.txt`+`index.html`
- [x] `sdk-manifest.json` — embedded; 7.4.11 sizes+SHA-256 pinned from first verified download; unknown versions download with a warning
- [x] `Reconstruct` — fresh project in `%LOCALAPPDATA%\RenpyRehost\work\proj\<name>`, `game/` cloned (scripts copied, media/`.rpa` **hard-linked** — 5.5 GB linked / 76 MB copied on a 5.9 GB reference game); **unpacks `.rpa` archives to loose files** (`Rpa/RpaArchive.cs` + `Rpa/Pickle.cs` — RPA 2.0/3.0/3.2, no Python); lets Ren'Py 7.4 write its own default `progressive_download.txt`
- [x] `Build` — no `web_build` CLI on 7.4; injects `<sdk>/launcher/game/zzrehost.rpy` registering a `rehost_web` command that calls the launcher's `build_web(p, gui=False)` with the dev server stubbed; runs `renpy.exe <sdk>/launcher rehost_web <proj>` (`SDL_*=dummy`), streams output, finds the `*-web` folder
- [x] `Serve` — `LocalWebServer` (HttpListener): `application/wasm` MIME, HTTP Range, COOP/COEP/CORP isolation headers; `rehost serve <folder>` command; `convert --serve` opens a browser and holds until Ctrl+C
- [x] `Assemble` (P0 form) — moves/copies the web build to `--out`, writes the sidecar, records the port size, and (unless `--keep-work`/`--reuse-project`) deletes the reconstructed project + Ren'Py's `-dists` scratch. `rehost clean` + GUI "Clean up" sweep leftovers (`Housekeeping`).
- [x] `Build` auto-selects native `web_build` (8.2+) vs the injected command (7.4)
- [x] **End-to-end on a real game: SUCCESS** — 7.4.11 game (5.9 GB) → 7 GB progressive web
  build on an 8.3.7 SDK, served + HTTP-verified (wasm MIME, range, COOP). ~15 min.
  In-browser render not yet eyeballed. (7.4.11 SDK hits a Python-2 zip64 wall at 4 GB;
  8.x is the path for large games.)
- [ ] Eyeball a served build in a real browser
- [x] Small clean 8.x VN smoke test — Ren'Py's bundled `the_question`, **18.8 s**, 52 MB
- [x] Stage tests: 62 passing (RenpyVersion, VersionDetector, Ingest, ZipUtil, LocalWebServer, RpaArchive, ImageOps, PreflightScanner, AssembleStage, Library, Housekeeping, AppSettings, Log)

**P0 done + P2 core + P1 GUI.** A real Ren'Py game converts and serves via one command
*with playable video*. Second run on that reference game: 75 `.mkv` cutscenes → WebM
(VP9 remux, no re-encode), webp → PNG. Remaining: browser eyeball, a small-VN smoke
test, webp→JPEG size win (in the definitive run), audio recompress.

## P1 — GUI

**Goal:** drop a game folder on a window, watch it convert, click through to play.

- [x] `RenpyRehost.App` — **Convert** tab (drop zone + browse, options, 9-stage ListView + live status, dark log pane, cancel) + **Library** tab
- [x] Library — persisted `library.json`; converts auto-register (with source path); Add existing… / Remove / Show folder / Move (→ `%LOCALAPPDATA%`, → next to the original game (asks + remembers `SourcePath` when unknown), → a picked folder; same-volume rename, cross-volume buffered copy+delete with a progress bar); Play / Choose browser; CLI `rehost library [add|remove|move]` + `rehost play <folder|number>`
- [x] Result bar: opens the build in a browser when a convert finishes; "Show folder"
- [x] `GameLauncher` — opens a served URL in the system default browser, or a remembered preference (`AppSettings.PreferredBrowserExe`, set via `rehost browser <exe>` / GUI "Choose browser", `RENPY_REHOST_BROWSER` env var overrides it); one-off picks via `--with <exe>` (`--save` to also remember it) or the registry-backed browser picker
- [x] `--emit` writes `rehost.json` (schema 1: title, sourceVersion, builtWith, entry, sizeBytes, notes)
- [x] `publish.cmd` — portable single-file `rehost.exe` (~90 MB, Magick native libs self-extract; verified: full convert of `the_question` in 20.8 s) + `RenpyRehost.exe`; `install.cmd` — PATH + Start Menu shortcut
- [ ] Eyeball the GUI on a real desktop; icon + signing

**Done when:** folder-drop -> playing in a browser tab, no terminal.

## P2 — the asset pipeline

**Goal:** a 12-GB-class game builds and runs.

- [x] `AssetPipelineStage` between Reconstruct and Build; `--assets off|auto|force`, default auto
- [x] `6a images` — `ImageOps` on **Magick.NET**: WebP/AVIF → JPEG (opaque) / PNG (alpha) with per-file `.rpy` ref rewrite; PNG/JPG over `--image-width` downscaled (write-new + atomic swap, never in-place — the clone hard-links source assets), parallel, skips `gui/`; unreadable images → 2×2 transparent PNG
- [x] `ffmpeg` — `Media/Ffmpeg.cs` auto-downloads a BtbN static build (~40 MB, GitHub CDN, ~12 s) to the tools cache; probe + remux + transcode helpers
- [x] `6c video` — probe codec; VP8/VP9-in-MKV → **remux** to WebM (no re-encode); anything else → VP9+Opus WebM; ref rewrite `.mkv`→`.webm`
- [x] `6b audio` — lossless (WAV/FLAC/AIFF) → Opus 128k; MP3/OGG left alone (already lossy)
- [x] pass-completion cache (`.rehost-pipeline.json`) — `--reuse-project` re-runs skip finished passes and go straight to Build
- [ ] `6a` polish — per-file content-hash so a changed image re-processes; a size budget rather than a fixed width
- [ ] `6d archives` — per-`.rpa` bundle-vs-stream decision (threshold + total-budget); assemble a serve tree that mixes bundled + on-disk
- [ ] Manifest rewrite so the web runtime resolves streamed archives over localhost
- [ ] Memory-hook `.rpy`: `renpy.free_memory()` on label callback, web-only `config.image_cache_size`
- [ ] Stress harness: scripted long auto-advance run, sample JS heap, flag OOM trend

**Done when:** a 10+ GB, HD-art VN converts and survives a multi-hour auto-advance run
under the heap ceiling (or the report tells you it needs P5).

## P3 — diagnostics / preflight

**Goal:** never a silent failure; the user knows before they wait.

- [x] `Preflight` → `ConversionReport` (blockers / warnings / size profile / recommendation); accurate size breakdown incl. archived contents
- [x] `PreflightScanner` — keyed/unreadable `.rpa` (index-sanity), obfuscated `.rpyc` (RENPY magic vs source coverage), native `.pyd`/`.so`, and `subprocess`/`os.system`/`renpy.run`/`ctypes`/network/file-write in `.rpy`
- [x] Blockers hard-stop `convert` (with the list); `--force` overrides; `preflight` subcommand always just reports
- [x] `Log`/`LoggingProgressSink` — every convert/move/clean writes a full-detail, timestamped log to `%LOCALAPPDATA%\RenpyRehost\logs\` regardless of `-v`; auto-pruned (newest 50); `rehost logs [open]` / GUI "Logs…" button; a failure always names its log. GUI crash handlers (`Application.ThreadException` + `AppDomain.UnhandledException`) log + message-box instead of vanishing; CLI top-level catch broadened to log any unhandled exception, not just `RehostException`.
- [ ] Peak-working-set estimate from largest-scene asset heuristic
- [ ] GUI renders the report as a go/-caution/-stop card

**Mostly done.** Verified on the reference game (Go w/ pipeline, flags its
`subprocess`/`renpy.run` fallbacks) and synthetic keyed/obfuscated fixtures (Stop). A
real DRM'd game would now fail in seconds, not 15 minutes.

## P4 — port-forward path (Ren'Py 6 / early 7)

**Goal:** best-effort automation for games with no native web target.

- [ ] Bundle/pin `unrpyc`; decompile `.rpyc` -> `.rpy`
- [ ] Drop source into current SDK skeleton; attempt build
- [ ] Parse build errors -> known-deprecation fixups (scripted rewrites for the common ones)
- [ ] Guided-diff UI for the fixups it can't do automatically
- [ ] Loud about the legal/consent line — decompiling someone's scripts

**Done when:** a representative Ren'Py 7.2 game converts with only reviewed auto-fixups.

## P5 — chapter-split builds

**Goal:** the last resort for games that bust the heap even after P2.

- [ ] Detect chapter/act boundaries (label naming, `renpy.call`, scene structure) — likely needs a per-game hint file
- [ ] Emit N web builds sharing a persistent-storage save origin
- [ ] In-app library: one entry, chapter picker, shared saves

**Done when:** a game that OOM'd in P2 plays start-to-finish as split builds.

---

## Sequencing notes

- P0 is the spine; nothing else works without it. Build it against the smallest real
  game you can find.
- P1 and P2 are independent after P0 — do P1 first for a usable tool, or P2 first if
  a specific huge game is the whole point.
- P3 should land before you share the tool with anyone else.
- P4 and P5 are demand-driven — build when a game you actually want forces them.
