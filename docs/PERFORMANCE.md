# Performance audit

Snapshot: **2026-11-25**. Measurements on the development machine (Windows,
.NET 8, Debug, warm NuGet cache). `ORACLE_PERF_TRACE=1` emits `oracle-perf`
marks from process start; the same marks were used for every run below.

## 1. Build time

`dotnet build -t:Rebuild -c Debug`, wall clock:

| Project | Cold (first restore/disk) | Warm rebuilds |
|---|---:|---:|
| `Oracles-HD.csproj` | 25.5 s | 22.0-25.5 s |
| `OracleAssetBuilder.csproj` | 4.0 s | 2.2-4.0 s |
| Baseline `Links-Awakening-DX-HD/ProjectZ.csproj` | 76.3 s | 17.6-19.3 s |

The Oracle game project is **~4-6 s slower than the baseline warm rebuild**
because it compiles the Oracle importer/script/runtime sources in addition to
the unchanged DX-HD engine. There is no new build-time dependency; the delta is
source volume, not generated code or analyzers.

## 2. Startup to the first playable map

Measured from `Program.Main` to `MapTransitionSystem.FinishLoading` end. The
game intro and resource loader run first; these are the points that matter for
the `--oracle` development entry.

| Run | Total | `LoadContentThreaded` | `LoadTextures` | `GameObjectTemplates` | Map load |
|---|---:|---:|---:|---:|---:|
| `g0`, warm | **2.09 s** | 1.46 s | 1.22 s | 0.08 s | 0.028 s |
| `g1`, warm | **2.63 s** | 1.80 s | 1.50 s | 0.10 s | 0.059 s |
| `g4` dungeon `ra3`, warm | **2.16 s** | 1.54 s | 1.32 s | 0.08 s | 0.008 s |
| `g0`, cold disk | 6.8-13.5 s | 3.4-10.5 s | 1.3-2.2 s | 0.08 s | 0.03-0.10 s |

The cold runs are dominated by the OS file cache and by loading the baseline
DX-HD texture/sound set; the map itself is always a few tens of milliseconds.

### Root cause found and fixed

`GameObjectTemplates.SetUpGameObjects` compiled an expression-tree delegate for
**530 object templates** at startup. That cost **~0.78 s** per run and is pure
engine setup (the baseline pays it too). We replaced the compiled delegates with
the cached `ConstructorInfo` and `ConstructorInfo.Invoke` at spawn time. The
same map spawns only a few hundred objects, so the trade is strongly positive:

- before: `GameObjectTemplates` 0.78 s, hot startup 4.5 s;
- after: `GameObjectTemplates` 0.08-0.15 s, hot startup 2.1-2.6 s.

The per-frame `OracleRoomRuntime.UpdatePresentation` scan was also removed. Room
presentation only depends on the current room and entry direction, so it now
runs on `MapLoaded` and on an actual room crossing, not every frame. Script
updates still run per frame (only active runners).

### Baseline comparison

Measured the time from `Start-Process` to the first main window for both builds:
baseline **0.76-0.81 s** (one cold 2.19 s), Oracle **0.88-0.98 s**. The Oracle
entry adds roughly **0.1-0.2 s** before the window appears; the rest of the
startup remains the shared DX-HD resource pipeline.

## 3. Map load and transition

`SaveLoadMap.LoadMap` and object construction for the 196-room stitched
overworld:

| Map | Total loader | Tile map | Object rows | Oracle sections | Objects | `FinishLoading` |
|---|---:|---:|---:|---:|---:|---:|
| `g0` (present) | 20-57 ms | 3-17 ms | 2-6 ms | 9-13 ms | 5-10 ms | 12-27 ms |
| `g1` (past) | 26-59 ms | 2-10 ms | 2-2 ms | 11-22 ms | 9-30 ms | 17-27 ms |
| `g4` `ra3` | 5.5 ms | 0.6 ms | 0.3 ms | 2.2 ms | 0.8 ms | 12.8 ms |

The load runs on the existing `MapTransitionSystem` loader thread behind the
350 ms fade, so the visible transition is not extended by the loader. The
largest remaining loader component is the Oracle section parser (collision,
terrain and **3,251 animation rows** for `g0`), at 9-22 ms.

No table is parsed per frame. Script tables are loaded once when the session
starts; the entity placement and dungeon tables are build-time data.

## 4. What was not optimized

- `Resources.LoadTextures` is the largest warm-startup component (~1.2-1.5 s):
  it is the baseline DX-HD texture set plus the generated Oracle atlases, and it
  is a GPU upload cost. It is shared with the baseline; deferring it would break
  the existing intro and editor paths.
- `LoadOracleSections` still materialises the remaining map lines before
  parsing. At 9-22 ms it is below the fade budget; it is a candidate for a
  streaming parser only if a future map grows much larger.
- The baseline's own 17-19 s rebuild remains the floor for compile time.

## 5. Reproduce

```powershell
$env:ORACLE_PERF_TRACE = '1'
Oracles-HD.exe oracle --map oracle_ages_g0_overworld.map
# read the oracle-perf lines from stdout
```

Record build times with `dotnet build -t:Rebuild -c Debug` around the same
commands. The trace is opt-in and has no effect when the variable is unset.
