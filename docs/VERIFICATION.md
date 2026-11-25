# Verification

Everything here is reproducible from a clean checkout plus the pinned
submodule. Record the commit and the exact counts with each milestone.

## 1. Build

```powershell
# user-local .NET 8 SDK (this machine)
.\build.cmd
# or
dotnet build -c Debug
```

Expected: **0 errors**. The 48 warnings are the upstream v2.0.6 warnings
(unused fields, nullable annotations in `Game1`, manifest DPI).

## 2. Asset import

Commands: `BUILDING.md` -> Generate assets. The checks are:

- present and past overworlds report 196/196 rooms and 0 skipped;
- `interiors` reports 22 present-day maps and 0 warnings;
- a sample dungeon room reports no missing-tile/palette warning;
- `inspect-anim` shows headers `0,24,32` first for group `$00`;
- no `.ani` or extra data file appears in `Data/`.

Current results (commit `b5119ed`, script-engine block):

| Item | Value |
|---|---|
| Ages group 0 | 196/196 rooms, 0 skipped, 140x112 tiles, atlas 1.566, 3.251 animations |
| Ages group 1 | 196/196 rooms, 0 skipped, 140x112 tiles, atlas 1.646, 4.129 animations |
| Map metadata | every Oracle map starts with `#oracle-map v1`; interior `2:e7` has `group=2 kind=interior camera=follow` |
| Camera | `2:e7` and the overworld use the DX-HD smooth follow; no size-based `CameraTarget` lock |
| Ages present interiors (group 2) | 22 maps, 22 sprite objects, 11 exit warps, 0 importer warnings |
| Overworld object pass | 103 sprites + 13 interior-entry warps |
| Entity atlas | 221 frames from 37 interaction ids; Maku `$87:00` has 6 frames |
| Ages group 4 | rooms `00,01,08,10,20` render with 0 missing-tile/palette warnings |
| Seasons tileset `00` | inspect works; seasonal variants not implemented |

No `.ani` file and no extra data file is generated. Frames live in the existing
tile atlas or the shared entity atlas; the timeline is the `#oracle-anim v1`
section or the `oracleSprite` durations.

| Ages past interiors (group 3) | 16 maps from group-1 warps, 16 interactions, 4 items | 
| Ages dungeon tables | 16 dungeons; `dungeons --dungeon 05` = 45 rooms/2 floors, 28 interactions, 67 enemies, 20 parts, 37 items | 
| Ages entity placements | 1,199 typed rows across groups 0-5 (random enemies marked) |
| Dependency audit | `audit --game ages` -> 5,437 condition rows + symbol table; every WRAM/global-flag/object-stream reference has source file and line |
| Enemy atlas | `oracle_ages_enemies.png/.atlas`; Octorok `$09:00..02` four cardinal animations composed |

### 2.1 Performance

The detailed timings and root causes are in `docs/PERFORMANCE.md`. The
reproducible probes are:

```powershell
$env:ORACLE_PERF_TRACE = '1'
Oracles-HD.exe oracle --map oracle_ages_g0_overworld.map
Oracles-HD.exe oracle --map oracle_ages_g1_overworld.map
```

Observed warm startup: **2.09 s (g0)**, **2.63 s (g1)**, **2.16 s (g4 ra3)**.
Map loader: **20-57 ms (g0)**, **26-59 ms (g1)**, **5.5 ms (g4)**. The loader
runs behind the existing fade. The 530-template reflection compile that cost
~0.78 s was replaced with cached constructors; the per-frame room-presentation
scan now runs only on a room change.

## 3. Determinism

Hash the map and atlas, re-run the importer, hash again:

```powershell
$files = @(
  "Data/Maps/oracle_ages_g0_overworld.map",
  "Data/Maps/Tilesets/oracle_ages_g0_overworld.png",
  "Data/Maps/oracle_ages_g2_re7.map",
  "Data/Map Objects/oracle_ages_entities.png",
  "Data/Map Objects/oracle_ages_entities.atlas"
)
$a = (Get-FileHash $files | % Hash) -join ''
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 0
dotnet run --project Tools/OracleAssetBuilder -- interiors --game ages
$b = (Get-FileHash $files | % Hash) -join ''
"$($a -eq $b)"
```

Recorded: **group 0 = True**, **group 1 = True**, **interiors + shared entity
atlas = True**.

## 4. Runtime smoke test

```powershell
bin\Debug
et8.0-windows\Oracles-HD.exe oracle
```

Expected: the generated present overworld; Link at the centre of room `0x39`;
DX-HD camera; no exception; the Maku Tree visible to the left.

Development entries:

```powershell
# Boot a present interior directly (group 2 room id).
Oracles-HD.exe oracle --room e7
# Override the spawn point in map pixels.
Oracles-HD.exe oracle --pos 1360,470
```

The runtime switch is `Game1.OracleOverworld` / `OracleStartMap` /
`OracleStartPosition`; the normal LA intro/menu still runs without `oracle`.

### Screenshot harness

The window is captured with `GetWindowRect` + `Graphics.CopyFromScreen`, and
movement is injected with `keybd_event` (VK_LEFT `0x25`, VK_RIGHT `0x27`).
Recorded evidence (gitignored, under `bin/Debug/net8.0-windows/`):

| File | Shows |
|---|---|
| `anim3_a.png` | Maku Tree visible; Link walking; no black cell |
| `anim3_b.png` | Same camera, ~1 s later |
| `collision_left.png` | Link stopped at the water/tree edge after holding Left |

Animation check: crop the Maku Tree water area (x 0-330, y 250-850) in
`anim3_a/b`; **1.273 pixels differ**, far from Link's idle sprite. Tile
animation is running.

For script probes, set `ORACLE_SCRIPT_TRACE=1` and wait for the
`oracle-script: map ...` line before injecting keys; the normal game intro runs
first, so an early screenshot shows the intro sea rather than the loaded room.
`--entry 0..3` forces the native `wScreenTransitionDirection` for development
entry, which is how the scripts are exercised without walking a path.

Phase 3/4 evidence:

| Check | Procedure | Result |
|---|---|---|
| Enter interior | Boot `oracle --pos 1037,880`; hold Up into room `0:66`'s door | Map changes to `oracle_ages_g2_re7.map` |
| Exit interior | Boot `oracle --room e7 --pos 76,108`; hold Down into the bottom edge | Map changes to `oracle_ages_g0_overworld.map`, spawn at room `0:66`'s door |
| Maku eyes | Boot `oracle --pos 1360,470`; observe `$87` | Eyes, expressions and smile render on the upper trunk |
| Maku reference | Compare `Data/Maps/Previews/oracle_ages_g0_r38.png` with the original `Oracle_Of_Ages_-_Maku_Tree.png` | Same teal pond, blue bubble foliage, brown trunk, face and pink flowers; no black canopy/lower band |
| Maku wood palette | Boot `oracle --pos 1400,431` | Lower fence and portal-side wood use the source `PALH_8f` gold/brown palette 7, not the pink flower palette from `$30` alone |
| Maku single tile change | Boot `oracle --pos 1460,431 --intro-done --flag MAKU_TREE_SAVED --room-flags 40` | Room `$39` position `$22` is written from the base portal tile to `$d7`; the white square is gone and the wooden wall is visible |
| Eye animation | Burst of frames over 3.3 s | Frame 0 open, then half/closed frames; loop returns to open |
| Object anchor | Interior `2:e7` dog (`$54`) and person (`$53`) | Sprites centred on the native interaction positions |
| Flag gate off | Boot `oracle --pos 1360,470` | `$6b:$15` Link statue absent |
| Flag gate on | Same with `--flag FINISHEDGAME` | Statue appears right of the Maku Tree with `PALH_c7` colours |
| NPC collision | Interior `2:e7`; hold Up toward the dog (`$54`) | Link stops at the solid tile instead of passing through |
| Source-derived collision | Room `0:39`; enter normally | Ralph `$37:$0d` is hidden (source requires entry from the right); the past villager `$3a:$0d` is a different NPC |
| Time portal present->past | Boot `oracle --pos 1480,431`; step down and back up | Map becomes `oracle_ages_g1_overworld.map` at the same world position; Maku sprout |
| Time portal past->present | From the arrival portal, step off and back on | Map returns to `oracle_ages_g0_overworld.map`; Maku Tree visible |
| Past Maku colours | Space `1:$38` after the portal | Teal pond and blue bubbles; `$38` is excluded from `setPastCliffPalettesToRed` |
| Dungeon map | `oracle --map oracle_ages_g4_ra3.map --pos 80,120` | Loads the generated dungeon map with `kind=dungeon`, group 4, floor/dungeon metadata; no exception |
| Story-state scene | `oracle --pos 1400,431` (clean pre-intro) vs `--intro-done` | Pre-intro shows `$6b:$01` audience (Nayru/Ralph/bear/animals); later state hides it and uses `$5d:$02`/`$37:$0d`; `$37:$11` waits for `FINISHEDGAME` |
| Octorok visible | Same Maku approach; inspect upper room | Specific Octorok sprite appears at its source position and animates |
| Past interior | `oracle --map oracle_ages_g3_r2f.map --pos 80,120` | Loads a generated group-3 interior map |
| Room lifecycle | Enter `0:$39` from `0:$38` (cross right) vs from another edge | Ralph is presented before the crossing when approaching right; hidden for other entries; the past villager is a different interaction |
| Behaviour table | Regenerate twice | `oracle_ages_interaction_behaviors.tsv` + manifest are identical; rows include `$37:$0d` entry=1 and `$6b:$15` flag=$14/palette=$c7 |
| Script table | Regenerate twice | `oracle_ages_scripts.tsv` + `oracle_ages_script_bindings.tsv` and the manifest are identical; bindings include `$37:$0d -> ralphSubid0dScript` and `$3a:$0d -> villagerSubid0dScript` |
| Ralph cutscene | `ORACLE_SCRIPT_TRACE=1 oracle --map oracle_ages_g0_overworld.map --pos 1424,431 --entry 0 --intro-done`; wait for the overworld trace, hold Right ~1.2 s | Log shows `presentation $37:$0d visible=True prospective dir=1` before `room 0:39 dir 1`; script starts, sets global flag `$40`, deletes Ralph and ends |
| Past villager cutscene | `ORACLE_SCRIPT_TRACE=1 oracle --map oracle_ages_g1_overworld.map --pos 1480,431 --entry 0` | Script runs `jumpAndWaitUntilLanded`, skips text, moves down/right, sets flag `$41`, deletes the villager and ends |
| Completed one-shot | Same past map with `--flag ENTER_PAST_DONE` | No script start; the binding's `completed_flag=$41` suppresses presentation and activation |
| No pop-in | Boot `0:$38`, wait for map load, observe `0:$39` before crossing | Ralph is already drawn in the neighbour room; crossing does not change his visibility |

## 5. Known false positives / artifacts

- Link's idle animation changes pixels; exclude his 64x64 screen box when
  measuring water animation.
- The Maku Tree scene uses its tileset palette `$30` plus the `PALH_8f` wood
  palette loaded by `makuTree.s`; only palette 7 differs (gold/brown instead of
  pink/white). The eyes use standard sprite palette 3 (2/3 XORed). The canopy
  and lower band are static tileset `$22` graphics (header `$70`: common +
  bottom) and match the original screenshot. The face/expressions also need the
  object-gfx chain `$04`+`$05`, resolved from the OAM tile range; they are not
  `roomGfxChanges`.
- `WindowWidth/Height` include the title bar/border; map-to-screen crops must
  account for it.
- The overworld/internals are intentionally stitched or separate maps; a
  transition screenshot is only proof for the exercised door, not for every
  warp source.

## 6. Regression checklist

Run before each milestone commit:

- [ ] `dotnet build -c Debug` -> 0 errors.
- [ ] `overworld --group 0` and `--group 1` -> 196 rooms, 0 skipped.
- [ ] Determinism check -> True.
- [ ] `room --group 4 --room 01` -> no missing-tile/palette warning.
- [ ] `interiors --game ages` -> 22 maps, 0 importer warnings.
- [ ] Entity atlas deterministic; `oracle_<game>_interaction_behaviors.tsv` + manifest deterministic.
- [ ] Past `1:$38` keeps the teal Maku water; `$40..$7f` rewrite and room `$38` exclusion verified.
- [ ] `OracleRoomRuntime` load/enter hooks fire; `MapLoading` precedes object construction and `MapLoaded` follows Link.
- [ ] Continuous room tracking updates `OracleSessionState` on stitched-map crossings; room presentation shows entry-gated actors before the boundary (no pop-in).
- [ ] `ralphSubid0dScript` runs on right entry, sets `$40`, deletes Ralph and restores input; the completed guard suppresses him afterwards.
- [ ] `villagerSubid0dScript` runs on past `1:$39`, jumps/lands, moves down/right, sets `$41`, deletes the villager and restores input.
- [ ] Script command/binding TSV + manifest regenerate deterministically; no generated script TSV is staged.
- [ ] `dungeons --dungeon N` writes dungeon/layout tables + maps; no generated dungeon TSV is staged.
- [ ] `ORACLE_PERF_TRACE=1` startup and map-load marks recorded; no per-frame presentation scan remains.
- [ ] `audit --game ages` exports the exhaustive condition/symbol tables; no audit TSV staged.
- [ ] Clean pre-intro state shows `$6b:$01` audience; `--intro-done` switches to the later bear/Ralph state without cross-room exclusions.
- [ ] `--game ages --room e7` resolves the generic interior map; `--flag` names use `OracleGameProfile`.
- [ ] `--oracle` launches; Link moves; no exception in stderr.
- [ ] Enter and exit `0:66` <-> `2:e7` through the generated warps.
- [ ] Maku eyes/expressions render and blink; present Maku applies `PALH_8f` after `$30` so the fence/portal wood is gold/brown.
- [ ] `singleTileChanges` table regenerates; room `$39` mask `$40` writes tile `$d7` at `$22` only when the room flag is set.
- [ ] Time portal crosses present <-> past in both directions at the same world position.
- [ ] Source-derived collision: `objectMarkSolidPosition`/radii only; no default 12x12 for every NPC.
- [ ] `$87` composes object-gfx headers `$04`+`$05`; no missing tiles on expression frames.
- [ ] NPC collision box stays inside the sprite frame (narrow 8px actors do not crash the map).
- [ ] Tile animation crop differs between two frames.
- [ ] Collision: Link stops at water/cliff.
- [ ] Root cause checked against `ooa-godot` / `oracles-decomp` / `top` before
  applying any fix (no fix-by-trial).
- [ ] No hardcoded behaviour, position, offset, exception or rule keyed by an
  object/room id. Collision, palettes, extra graphics, flags and portals come
  from parsed source tables/behaviour, not `if (id == ...)`. Any room-specific
  branch must be backed by a source handler and recorded as data.
- [ ] Existing code swept for hardcodes/deprecated ad-hoc branches after adding
  the generic mechanism; no duplicate mechanisms for the same fact.
- [ ] New/changed system contrasted with `ooa-godot`; keep ours only when it is
  more generic/clean, otherwise adopt the missing conditions or structure, and
  record any divergence.
- [ ] Manifests updated; no generated asset staged.
- [ ] No GB conversion arithmetic added to `InGame/` (review the diff).
- [ ] No `.bak`/`.tmp`/debug log/dead branch from an experiment.
- [ ] `STATUS.md` and the affected doc updated in the same commit.
- [ ] `AUDIT.md` debt list still accurate; update it if the change moves a
  boundary or resolves a bridge.

## 7. Validation design and limits

- **Importer:** deterministic hashes are the primary regression. Add new
  checks to the importer or previews when a format rule changes.
- **Runtime:** probes are black-box (window screenshots + injected keys). Fix
  the camera, exclude Link's 64x64 box, and compare a crop; one variable per
  run.
- **No separate validation assembly yet.** Add one when content systems need
  per-update assertions; do not add validation-only state to production.
- **Performance:** not profiled. The generated overworld is 140x112 tiles with
  ~3k animated cells; measure before optimizing.
- **Coverage:** a passing check proves the exercised path, not universal
  parity. Record unsupported paths explicitly in `STATUS.md`/`COVERAGE.md`.



