## Game Stats (FPS, frametime & graphics API)

TuringMonitor can show live game telemetry — game title, FPS, frametime and
graphics API (Vulkan, OpenGL, D3D9/10/11 via DXVK, D3D12 via VKD3D) — sourced
from [MangoHud](https://github.com/flightlessmango/Mangohud).

### How it works

1. The daemon watches a shared CSV log folder (`/var/lib/turing-monitor/game`).
2. A game launched with the TuringMonitor MangoHud profile writes one CSV file
   per session (one line per second) into that folder.
3. The daemon tails the newest file incrementally, extracts the game title from
   the file name, and locates the fps / frametime / api columns by header name
   (so it survives MangoHud version/config changes).
4. When no CSV has grown recently, the session is treated as over and the
   telemetry reports idle values (`-`, 0 fps).

The HUD itself is invisible in the game: it renders fully transparent
(`alpha=0.0` + `background_alpha=0.0`) — the LCD panel is the only surface
showing the data. (Do NOT use `no_display`: in MangoHud 0.8.4 it also kills the
CSV autostart, because the autostart check runs on the HUD update path.)

### Setup (once, after running install.sh)

`install.sh` installs a dedicated MangoHud profile to
`/etc/TuringMonitor/turingmonitor.conf` and creates the shared log folder. It
never overwrites the profile on upgrades, so local customizations survive.

### Enabling per game

Add this to the game's Steam launch options (Properties → Launch Options):

```
MANGOHUD_CONFIGFILE=/etc/TuringMonitor/turingmonitor.conf mangohud %command%
```

MangoHud must be injected at process start (it hooks the graphics API), so it
cannot attach to an already-running game — this launch option is required for
each game you want on the display.

For Lutris/Bottles: use the same `MANGOHUD_CONFIGFILE` env var with the
`mangohud` command prefix. For a Vulkan-only global default you can also export
`MANGOHUD=1` in your shell profile.

### Theme sources

Add these elements to your theme to render game stats:

| Source          | Type  | Value                                             |
|-----------------|-------|---------------------------------------------------|
| `GameName`      | Text  | Game title from the CSV file name, `-` when idle |
| `GameFps`       | Text  | Frames per second (float, e.g. `{0:0} fps`)       |
| `GameFrametime` | Text  | Last frame time in ms (float, `{0:F1} ms`)        |
| `GameApi`       | Text  | `Vulkan`, `OpenGL`, `DX9/10/11 (DXVK)`, `D3D12 (VKD3D)` or `-` |

Notes:

- The graphics API comes from the CSV `api` column when present; otherwise the
  daemon resolves it by scanning `/proc` for the process with the MangoHud
  layer injected.
- The MangoHud profile can be customized (`/etc/TuringMonitor/turingmonitor.conf`),
  e.g. adding `benchmark_percentiles` for 1%-low / 0.1%-low columns.