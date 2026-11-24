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
- a sample dungeon room reports no missing-tile/palette warning;
- `inspect-anim` shows headers `0,24,32` first for group `$00`;
- no `.ani` or extra data file appears in `Data/`.

Current results (commit `501fb1f`):

| Item | Value |
|---|---|
| Ages group 0 | 196/196 rooms, 0 skipped, 140x112 tiles, atlas 1.566, 3.251 animations, map 297 KB |
| Ages group 1 | 196/196 rooms, 0 skipped, 140x112 tiles, atlas 1.646, 4.129 animations, map 366 KB |
| Ages group 4 | rooms `00,01,08,10,20` render with 0 missing-tile/palette warnings |
| Seasons tileset `00` | inspect works; seasonal variants not implemented |

No `.ani` file and no extra data file is generated. Frames live in the existing
atlas; the timeline is the `#oracle-anim v1` section of the existing `.map`.

## 3. Determinism

Hash the map and atlas, re-run the importer, hash again:

```powershell
$map = "Data/Maps/oracle_ages_g0_overworld.map"
$atlas = "Data/Maps/Tilesets/oracle_ages_g0_overworld.png"
$a = (Get-FileHash $map,$atlas | % Hash) -join ''
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 0
$b = (Get-FileHash $map,$atlas | % Hash) -join ''
"$($a -eq $b)"
```

Recorded: **group 0 = True**, **group 1 = True**.

## 4. Runtime smoke test

```powershell
bin\Debug\net8.0-windows\Oracles-HD.exe oracle
```

Expected: the generated present overworld; Link at the centre of room `0x39`;
DX-HD camera; no exception; the Maku Tree visible to the left.

The runtime switch is `Game1.OracleOverworld`; the normal LA intro/menu still
runs without `oracle`.

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

## 5. Known false positives / artifacts

- Link's idle animation changes pixels; exclude his 64x64 screen box when
  measuring water animation.
- The Maku Tree face area uses palette 7 (white/green/pink) from
  `paletteData4f70`; it is palette-correct. The missing eyes are the
  `INTERAC_MAKU_TREE` object, not a missing tile.
- `WindowWidth/Height` include the title bar/border; map-to-screen crops must
  account for it.

## 6. Regression checklist

Run before each milestone commit:

- [ ] `dotnet build -c Debug` -> 0 errors.
- [ ] `overworld --group 0` and `--group 1` -> 196 rooms, 0 skipped.
- [ ] Determinism check -> True.
- [ ] `room --group 4 --room 01` -> no missing-tile/palette warning.
- [ ] `--oracle` launches; Link moves; no exception in stderr.
- [ ] Tile animation crop differs between two frames.
- [ ] Collision: Link stops at water/cliff.
- [ ] Manifests updated; no generated asset staged.
- [ ] `STATUS.md` and the affected doc updated in the same commit.

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
