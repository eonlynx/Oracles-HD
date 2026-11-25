# Progress and parity tracker

Snapshot: **2026-11-25**, commit `76659a6`, Phase 3/4 vertical slice
(interiors, warps, Maku Tree eyes, first generic objects), base
Links-Awakening-DX-HD v2.0.6 (`2e68849`).

Status legend:

- **Done** - implemented and verified for the stated scope.
- **Partial** - implemented for a subset, or verified only visually.
- **Not started** - no implementation.
- **Blocked** - needs a decision or missing source data.

## 1. Engine and platform

| System | Status | Notes |
|---|---|---|
| DX-HD engine fork (render, camera, entities, overlays, save, UI) | Done | v2.0.6 base imported unchanged where possible. |
| .NET 8 build | Done | `net8.0-windows`, 0 errors, 46 pre-existing warnings. |
| Runtime startup | Done | `--oracle` boots directly into the generated overworld. |
| Map metadata (`#oracle-map v1`) | Done | Group/era/kind/room size/grid/room/floor/dungeon/camera; parsed into `Map.OracleInfo`. |
| Camera policy | Done | DX-HD smooth follow by default; `Map.CameraTarget` only for isolated one-screen rooms. |
| Multiplatform (Linux/macOS/OpenGL/Vulkan/DX12) | Blocked | Upstream v2.0.6 is WindowsDX-only; sources not in the repository. See `PLATFORM.md`. |
| Legacy LA cleanup | Not started | Policy in `CLEANUP.md`; LA data still backs the fallback boot/menu. |

## 2. Oracle of Ages - assets

| Asset area | Status | Coverage / evidence |
|---|---|---|
| Tileset tables (bit 7 mask) | Done | `tilesets.s`, layout-label aliases from `tilesetHeaders.s`. |
| 2bpp PNG decode (dependency-free) | Done | `Tools/OracleAssetBuilder/Png.cs`. |
| Metatiles + CGB attributes | Done | 4 indices + 4 attributes; tile slot `(tile+0x80)&0xff`. |
| VRAM bank 0 + bank 1 | Partial | Both pools load; Ages room layouts tested use bank 1. |
| Tileset palettes 2-7 | Done | `paletteHeaders.s` + `paletteData.s`. |
| BG palette 0 | Done | `paletteData48e0` common palette. |
| Past cliff rewrite (6 -> 0) | Done | `setPastCliffPalettesToRed`, room `$38` excluded. |
| Overrides (`checkTilesetOverride`) | Done | Companion layout 1/3, Maku saved +2, Jabu +1 (CLI flags). |
| Animated tiles | Done | Frames in the existing atlas; timeline in `.map`; no `.ani`. |
| Tile collisions | Partial | Quadrant bits + water/hole/lava solid; special collisions (`>=0x10`, bridges/rails) not yet exact. |
| Terrain (`FieldStates`) | Partial | Water, deep water, lava, grass; swimming/falling not implemented. |
| Room-specific graphics (`roomGfxChanges`) | Not started | Maku Tree canopy/bottom still render black until this is imported. |
| Objects / interactions (first pass) | Partial | 36 interaction ids composed into one atlas; positioned interactions only, default animation. `INTERAC_MAKU_TREE $87` eyes render and blink. |
| Warps / interiors / doors (first pass) | Partial | 22 present interiors (group 2) from group-0 warps; tile, pointer-position and screen-edge warps trigger DX-HD transitions. Past group 3 and dungeons pending. |
| Maku Tree palette (`PALH_8f`) | Done | Room `$38` uses palette header `$8f` per `makuTree.s`, not tileset `$30`. |
| Dungeon floor layouts | Not started | `dungeonLayouts.s` not parsed; sample rooms only. |
| Text / dialogue | Not started | LA `.lng` files still used. |
| Audio (Oracle GBS) | Not started | DX-HD GBS player ready; Oracle `.gbs` not built. |
| Save / progression | Not started | LA save layout still active; 4 slots. |

### Ages overworld coverage

| Group | Meaning | Rooms | Map size | Atlas | Animation placements |
|---|---|---|---|---|---|
| 0 | Present overworld | 196 / 196 | 140x112 tiles | 1.566 metatiles | 3.251 |
| 1 | Past overworld | 196 / 196 | 140x112 tiles | 1.646 metatiles | 4.129 |
| 2 | Present interiors / underwater | 22 interiors | 10x8 each | per room | per room |
| 3 | Past interiors / underwater | 0 | - | - | - |
| 4 | Dungeons (group 4 room table) | 5 sample rooms | 10x8 / 15x11 | per room | per room |
| 5 | Dungeons (group 5) | 0 | - | - | - |

Validation artifacts (generated, gitignored): `Data/Maps/Previews/`,
`Data/Maps/*.map`, `Data/Maps/Tilesets/*.png`,
`Data/Map Objects/oracle_ages_entities.png/.atlas`, `bin/.../anim3_a.png`,
`bin/.../collision_left.png`.

### Phase 3/4 vertical slice

- Present overworld: 101 sprite objects, 13 interior-entry warps.
- Present interiors: 22 maps, 22 sprite objects, 11 exit warps.
- Entity atlas: 244 frame entries from 36 interaction ids.
- Maku Tree: 6 composed OAM frames (`$87:00`), blink timeline
  `3000,66,66,66,66,66` ms, palette header `$8f`.
- Verified: enter room `0:66` -> interior `2:e7` through the door; exit `2:e7`
  through the bottom edge -> room `0:66`; eyes animate.

### Known Ages artifacts

- The Maku Tree canopy and lower band are black until `roomGfxChanges` is
  imported. The eyes themselves are an object, not a tile.
- The right-side sign/flower are room graphics; only the palette is correct
  today.
- Special tiles whose graphics are loaded by room code may show placeholder
  colours until `roomGfxChanges` is imported.
- Bridges/rails and other special collisions are treated as free or full-solid
  conservatively.
- NPCs use their default animation; facing/script animations, enemies, parts and
  items are not loaded yet.

## 3. Oracle of Seasons

| Area | Status | Notes |
|---|---|---|
| Basic tables | Partial | Tileset/sample inspect works (`--game seasons --tileset 00`). |
| Seasonal tilesets (`m_SeasonalTileset`) | Not started | Needs the four seasonal entries and `wRoomStateModifier`. |
| Seasonal room layouts | Not started | Four layouts per room. |
| Subrosia (group 1, 11x8) | Not started | Different grid and tilesets. |
| Overworld / runtime | Not started | 0 rooms generated. |

## 4. Completion estimate

Estimates are phase-weighted, not line counts:

| Track | Estimate | Basis |
|---|---|---|
| Ages asset layer | ~70 % | Maps, tilesets, palettes, collisions, terrain, animations, first object/atlas pipeline; missing room gfx, enemies, full dungeons. |
| Ages runtime | ~20 % | Walking, camera, tile collisions, terrain, tile animation, first warps/objects; missing enemy AI, UI, save, audio, text. |
| Ages content (enemies, bosses, items, scripts, progression) | ~1-3 % | First static NPCs and Maku eyes only. |
| Seasons | ~5 % | Basic table parsing only. |
| Overall project | ~12-17 % | Vertical slice works; content is the long pole. |

## 5. Next milestone

See `ROADMAP.md` -> "Immediate next tasks". The Phase 3/4 acceptance (enter an
interior through a warp, see the animated Maku eyes, load first NPCs) is met for
the present-day slice. Next: `roomGfxChanges` (Maku canopy/sign graphics),
enemies/parts, and past/dungeon interiors.

Per-content inventory (enemies, NPCs, items, scripts): `COVERAGE.md`.
