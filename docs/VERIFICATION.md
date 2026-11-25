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

Expected: **0 errors**. The 46 warnings are the upstream v2.0.6 warnings
(unused fields, nullable annotations in `Game1`, manifest DPI).

## 2. Asset import

Commands: `BUILDING.md` -> Generate assets. The checks are:

- present and past overworlds report 196/196 rooms and 0 skipped;
- `interiors` reports 22 present-day maps and 0 warnings;
- a sample dungeon room reports no missing-tile/palette warning;
- `inspect-anim` shows headers `0,24,32` first for group `$00`;
- no `.ani` or extra data file appears in `Data/`.

Current results (commit `3540239`, Phase 3/4 slice):

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
bin\Debug\net8.0-windows\Oracles-HD.exe oracle
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

Phase 3/4 evidence:

| Check | Procedure | Result |
|---|---|---|
| Enter interior | Boot `oracle --pos 1037,880`; hold Up into room `0:66`'s door | Map changes to `oracle_ages_g2_re7.map` |
| Exit interior | Boot `oracle --room e7 --pos 76,108`; hold Down into the bottom edge | Map changes to `oracle_ages_g0_overworld.map`, spawn at room `0:66`'s door |
| Maku eyes | Boot `oracle --pos 1360,470`; observe `$87` | Eyes, expressions and smile render on the upper trunk |
| Maku reference | Compare `Data/Maps/Previews/oracle_ages_g0_r38.png` with the original `Oracle_Of_Ages_-_Maku_Tree.png` | Same teal pond, blue bubble foliage, brown trunk, face and pink flowers; no black canopy/lower band |
| Eye animation | Burst of frames over 3.3 s | Frame 0 open, then half/closed frames; loop returns to open |
| Object anchor | Interior `2:e7` dog (`$54`) and person (`$53`) | Sprites centred on the native interaction positions |
| Flag gate off | Boot `oracle --pos 1360,470` | `$6b:$15` Link statue absent |
| Flag gate on | Same with `--flag FINISHEDGAME` | Statue appears right of the Maku Tree with `PALH_c7` colours |
| NPC collision | Interior `2:e7`; hold Up toward the dog (`$54`) | Link stops at the solid tile instead of passing through |
| Source-derived collision | Room `0:39`; enter normally | Ralph `$37:$0d` is hidden (source requires entry from the right); the past villager `$3a:$0d` is a different NPC |
| Time portal present->past | Boot `oracle --pos 1480,431`; step down and back up | Map becomes `oracle_ages_g1_overworld.map` at the same world position; Maku sprout |
| Time portal past->present | From the arrival portal, step off and back on | Map returns to `oracle_ages_g0_overworld.map`; Maku Tree visible |
| Past Maku colours | Space `1:$38` after the portal | Teal pond and blue bubbles; `$38` is excluded from `setPastCliffPalettesToRed` |
| Room lifecycle | Enter `0:$39` normally vs from the right | Ralph is hidden normally and appears only for the right-entry event; the past villager is a different interaction |
| Behaviour table | Regenerate twice | `oracle_ages_interaction_behaviors.tsv` + manifest are identical; rows include `$37:$0d` entry=1 and `$6b:$15` flag=$14/palette=$c7 |

## 5. Known false positives / artifacts

- Link's idle animation changes pixels; exclude his 64x64 screen box when
  measuring water animation.
- The Maku Tree scene uses its tileset palette `$30`; the eyes use standard
  sprite palette 3 (2/3 XORed). `$8f` is a cutscene-issued load and must not be
  applied statically. The canopy and lower band are static tileset $22 graphics
  (header `$70`: common + bottom) and match the original screenshot. The
  face/expressions also need the object-gfx chain `$04`+`$05`, resolved from the
  OAM tile range; they are not `roomGfxChanges`.
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
- [ ] `--game ages --room e7` resolves the generic interior map; `--flag` names use `OracleGameProfile`.
- [ ] `--oracle` launches; Link moves; no exception in stderr.
- [ ] Enter and exit `0:66` <-> `2:e7` through the generated warps.
- [ ] Maku eyes/expressions render and blink; the room uses palette `$30` with pink flowers.
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
