# Playbook — native Ren'Py game to Web build

Five moves, one direction.

## 1. Identify the Ren'Py version

Run the game once, then open `log.txt` in its folder — the first lines print something
like `Ren'Py 8.2.3`. Or read `renpy/vc_version.py` inside the install directory.

Helper: `tools/Get-RenpyVersion.ps1 -Path <game folder or .exe>`

**Why it matters:** the exporter has to match. `.rpyc` bytecode is version-bound — the
wrong SDK either refuses to load it or loads it wrong.

## 2. Get the matching SDK

Download that exact version from https://www.renpy.org/latest.html (older builds are
kept under `/dl/<version>/`). Unzip into `sdk/<version>/` and launch the Ren'Py launcher
(`renpy.exe` or `renpy.sh`).

## 3. Reconstruct the project

Create a new project in the launcher, then replace its `game/` folder with the shipped
one — the folder holding the `.rpyc` and `.rpa` files. No source is needed; Ren'Py runs
bytecode directly.

**If the build chokes on the archives:** unpack them with `unrpa` so the assets sit
loose in `game/`, then rebuild.

## 4. Build the Web distribution

Launcher → your project → **Build Distributions** → tick **Web** → Build.

On 8.2+, enable **progressive download** so the browser streams assets on demand
instead of pulling the whole game before the menu.

Output lands in `<project>-dists/` — copy the web folder to `builds/<game>/`.

## 5. Serve it, open it

The launcher's "open in browser" starts a local server. Or from the build folder:

```
python -m http.server 8000
```

Then open `http://localhost:8000` in a browser. Must be HTTP, not `file://` — the web
runtime uses a service worker and `fetch`.

---

## When it fights back

- **Version mismatch you can't resolve.** Decompile the `.rpyc` back to `.rpy` with
  [unrpyc](https://github.com/CensoredUsername/unrpyc), drop the source into a current
  SDK, fix the deprecation errors by hand, then build. Slower, but always works for
  open (non-obfuscated) games.
- **Too big for a tab.** Progressive download, strip unused assets, drop bonus HD
  packs, or split into per-chapter builds.
- **A feature the web runtime lacks.** See the gap list — often a one-line script edit
  clears it (swap a movie call for a still, drop a subprocess call).

## What the web runtime can't do

- **Video** — VP8/VP9 in WebM plays; MP4/H.264 movie sprites and cutscenes often don't.
- **No OS reach** — no `subprocess`, no arbitrary file writes, no `renpy.run`, no
  auto-updater, some achievement hooks dead.
- **Saves live in browser storage**, scoped to the origin. No roaming; gone if site
  data is cleared.
- **One heap** — the whole game shares the tab's memory; very large games crash on load.
- **Fonts must be bundled** — system fallbacks won't match.
- **Cold first load** — without progressive download, everything downloads before the
  title screen.

## Feasibility at a glance

| Game profile | Verdict | Reason |
| --- | --- | --- |
| Ren'Py 8.x VN, under ~500 MB, dialogue-forward | **Go** | Clean rebuild, web target is first-class on 8.x. An afternoon. |
| Ren'Py 7.4–7.8, loose or unkeyed assets | **Go** | Web export works; watch for deprecated calls. |
| Ren'Py 6.x / early 7.x | **Rework** | No web support on that SDK; decompile and port forward. |
| Multi-GB assets — HD art, full voice | **Rework** | Progressive download + asset trimming, or split by chapter. |
| Heavy full-motion video / animated cutscenes | **Stop** | Web codec support is narrow; playback unreliable. |
| Obfuscated scripts, keyed archives, custom launcher | **Stop** | Resists repackaging; usually a commercial DRM layer. |
