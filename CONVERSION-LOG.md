# Conversion log

One entry per game. Record every place it broke and what fixed it — this log *is* the
deliverable as much as any single build.

---

## Template

### <game name>

- **Source:** where it came from, licence, download size
- **Ren'Py version:** (from log.txt / vc_version.py)
- **SDK used:** sdk/<version>
- **Assets:** loose / .rpa unkeyed / .rpa keyed
- **Verdict going in:** Go / Rework / Stop
- **Time spent:**
- **Result:** clean web build / needed rework / abandoned

**Breaks & fixes:**
| Step | What broke | Fix |
| --- | --- | --- |
| | | |

**Runtime notes:** (video, fonts, saves, memory, first-load time in the browser)

---

## Conversions

### Game A  (P0 spine guinea pig — a large commercial VN, name withheld)

- **Source:** a shipped native build, user-supplied. Left in place — not copied into this repo.
- **Ren'Py version:** 7.4.11 — confirmed by `game/script_version.txt` `(7, 4, 11)` and `renpy/__init__.py`. Detector: correct, from `renpy/__init__.py`.
- **Assets:** `game/archive.rpa` **5.0 GB** (unkeyed), + loose: 312 `.mp3` (553 MB, in `game/audio/`), 75 `.mkv` (125 MB), fonts. Ships full `.rpy` source (785 files).
- **Total:** 5.9 GB.
- **Verdict going in:** GoWithPipeline. Hard target — see below.
- **Status:** first automated run in progress.

**Known issues before we start:**
| Issue | Detail |
| --- | --- |
| 7.4.11 web strips ALL video | `web.rpy repack_for_progressive_download` drops `.ogv/.webm/.mp4/.mkv/.avi` unconditionally. Game A's 75 cutscenes will be missing until we port it to an 8.x SDK (P4). |
| 5 GB archive | `noarchive=True` unpacks `archive.rpa` at build time and classifies every file. Default 7.4 progressive rules (`+ image game/**`, `+ music game/audio/**`) push images + audio to progressive download — good, but the build has to touch all 5 GB and generate a 1/32 placeholder per image. Slow. |
| py2 build | 7.4.11 SDK is Python 2. Version-matched, so `.rpyc` load fine. |

**Breaks & fixes:**
| Step | What broke | Fix |
| --- | --- | --- |
| AcquireSdk | web.zip has a top-level `web/` folder; the flatten logic stripped it and dumped the wasm runtime into the SDK root, so `WEB_PATH` check failed | `ZipUtil.Extract` gained a `stripTopFolder` flag; SDK zip strips `renpy-<v>-sdk/`, web zip doesn't |
| AcquireSdk | assumed the SDK bundled a py3 runtime to run `web_build`; 7.4.x is py2-only and has no `web_build` CLI command at all | invoke `renpy.exe <sdk>/launcher rehost_web <proj>` via an injected `zzrehost.rpy` that registers a command calling the launcher's own `build_web(p, gui=False)` |
| Build (1st run) | with `archive.rpa` present, the distributor deflated the whole 5 GB archive into `game.zip` as one entry — non-progressive, game.zip already 1.4 GB and climbing when killed | `Reconstruct` now unpacks every `.rpa` into loose files first (own `Rpa/RpaArchive.cs` + `Rpa/Pickle.cs` mini-unpickler — no Python needed). Verified on Game A's real 4.9 GB RPA-3.0. |
| Build (2nd run) | full pipeline ran: scan → compile → wrote `game.zip` **5.69 GB** → `Preparing progressive download` → `repack_for_progressive_download` opened it → **`BadZipfile: Bad magic number for central directory`** | Ren'Py 7.4.x builds the *full* `game.zip` first, then slims it. On the Python-2 7.x line, a `game.zip` past ~4 GB has a broken/unreadable zip64 central directory. Game A's assets (5 GB pre-downscale) blow that limit. `BuildStage` now detects this error and explains it. |
| AssetPipeline (3rd run) | downscale pass ran on 7795 images — **only 20 downscaled, 20 MB saved**. Game A's art is 1920×1080-native (`gui.init(1920, 1080)`), already at target width. Downscaling alone won't get `game.zip` under 4 GB. | Image downscale is validated but insufficient for a 1920-native game. **Pivoted to building on 8.x** (Python 3 = working zip64) via `--renpy-version 8.3.7 --reuse-project`. |
| Build on 8.3.7 (4th run) | **Game A's 7.4.11 py2 source compiled clean on 8.3.7** (no deprecation errors!). Native `web_build` used. `game.zip` written past 4 GB with no zip64 error. Then failed in the progressive repack: `pygame_sdl2.image.load` → **`error: Unsupported image format`**. | Game A ships **1268 `.webp`** images. The web build's placeholder generator (`pygame_sdl2`) can't decode WebP/AVIF. GDI+ can't either (no OS codec). Added `Imaging/ImageOps.cs` on **Magick.NET** (swapped out System.Drawing): AssetPipeline pass 1 transcodes webp/avif → PNG and rewrites `"*.webp"` refs in `.rpy` to `.png`. |
| Transcode (5th run) | 1267/1268 webp transcoded fine; **1 failed** — one shipped `.webp` was **179 880 bytes of zeros**. The RPA index points at a zeroed region; verified 9349/9350 entries extract clean, so it's a corrupt entry in Game A's *shipped* archive, not our RPA reader. | AssetPipeline now replaces any unreadable image with a 2×2 transparent PNG so one bad shipped asset can't kill the build. |
| **6th run — SUCCESS ✅** | full pipeline, 902 s (~15 min): reuse project → transcode (1 blank) → downscale (0, already 1920) → compile on 8.3.7 → web build → progressive repack → package. Exit 0. | — |

## Result — first working conversion (2026-09-02)

**`--renpy-version 8.3.7 --reuse-project --assets auto`** → a **7.01 GB** web build at
`%LOCALAPPDATA%\RenpyRehost\out\<name>`:

- `index.html` (6 KB), `game.zip` **122 MB** (scripts + placeholders only — progressive
  download is working), `renpy.wasm` 19 MB, `renpy.data` 16 MB, `service-worker.js`,
  PWA `manifest.json` + `icons/`.
- `game/` holds the loose remote-file tree (`audio/ chapters/ images/ tl/ videos/`) —
  streamed on demand.

**Served via `rehost serve` and HTTP-verified:**
- `index.html` → 200, `Cross-Origin-Opener-Policy: same-origin` ✓
- `renpy.wasm` → 200, `application/wasm` ✓  `game.zip` → 200, `application/zip` ✓
- `renpy.data` range request → **206 Partial Content** ✓ (progressive download OK)

In-browser WASM boot / render **not yet eyeballed** (needs a real browser).

### Definitive build (2026-09-02, ~20 min) — video + webp→JPEG

`--renpy-version 8.3.7 --assets auto` → **5.65 GB** (down from 7.01):

| Pass | Result |
| --- | --- |
| webp/avif → jpg/png | 1268 images: opaque → JPEG q90, transparent → PNG; 18 explicit `.rpy` refs remapped |
| downscale | 22 images >1920px shrunk, −29 MB |
| **video** | **75 `.mkv` (all VP9) → WebM by remux** (`-c copy`, no re-encode, seconds) |
| build | 8.3.7 native `web_build`, `game.zip` 123 MB, progressive |

Final: `game/` = 8006 jpg · 1078 png · 168 webm · 0 webp · 0 mkv. Served + verified:
`index.html`, `renpy.wasm` (`application/wasm`), `game.zip`, `rehost.json`, a `.webm`
cutscene — all HTTP 200, correct MIME.

**Still:** in-browser boot not eyeballed; the 1 blanked image (`supermachoman-03`) shows
blank in one chapter-2 scene; ~553 MB of MP3 left as-is (already lossy — streams fine).

---

## The Question (Ren'Py's bundled demo) — happy-path smoke test

`rehost convert <sdk>\8.3.7\the_question --renpy-version 8.3.7 --emit`
→ **18.8 seconds**, 52 MB web build, native `web_build`, no asset changes needed.
Served: index.html / renpy.wasm / game.zip all 200. The clean-small-8.x case is fast.
