# Progress and parity tracker

Snapshot: **2026-11-25**, commit `501fb1f`, base Links-Awakening-DX-HD
v2.0.6 (`2e68849`).

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
| Room-specific graphics (`roomGfxChanges`) | Not started | Some special screens/objects may show placeholders. |
| Objects / interactions | Not started | Includes Maku Tree eyes (`INTERAC_MAKU_TREE $87`). |
| Warps / interiors / doors | Not started | Interiors are not separate maps yet. |
| Dungeon floor layouts | Not started | `dungeonLayouts.s` not parsed; sample rooms only. |
| Text / dialogue | Not started | LA `.lng` files still used. |
| Audio (Oracle GBS) | Not started | DX-HD GBS player ready; Oracle `.gbs` not built. |
| Save / progression | Not started | LA save layout still active; 4 slots. |

### Ages overworld coverage

| Group | Meaning | Rooms | Map size | Atlas | Animation placements |
|---|---|---|---|---|---|
| 0 | Present overworld | 196 / 196 | 140x112 tiles | 1.566 metatiles | 3.251 |
| 1 | Past overworld | 196 / 196 | 140x112 tiles | 1.646 metatiles | 4.129 |
| 2 | Present underwater | 0 | - | - | - |
| 3 | Past underwater | 0 | - | - | - |
| 4 | Dungeons (group 4 room table) | 5 sample rooms | 10x8 / 15x11 | per room | per room |
| 5 | Dungeons (group 5) | 0 | - | - | - |

Validation artifacts (generated, gitignored): `Data/Maps/Previews/`,
`Data/Maps/*.map`, `Data/Maps/Tilesets/*.png`, `bin/.../anim3_a.png`,
`bin/.../collision_left.png`.

### Known Ages artifacts

- The Maku Tree face area uses palette 7 (white/green/pink) from
  `paletteData4f70`; this is palette-correct. The missing eyes are the
  `INTERAC_MAKU_TREE` object, not a tile.
- Special tiles whose graphics are loaded by room code may show placeholder
  colours until `roomGfxChanges` is imported.
- Bridges/rails and other special collisions are treated as free or full-solid
  conservatively.

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
| Ages asset layer | ~65 % | Maps, tilesets, palettes, collisions, terrain, animations; missing objects, room gfx, full dungeons. |
| Ages runtime | ~15 % | Walking, camera, tile collisions, terrain, tile animation; missing objects, UI, save, audio, text. |
| Ages content (enemies, bosses, items, scripts, progression) | ~0-2 % | Not started. |
| Seasons | ~5 % | Basic table parsing only. |
| Overall project | ~10-15 % | Vertical slice works; content is the long pole. |

## 5. Next milestone

See `ROADMAP.md` -> "Immediate next tasks". Acceptance: Link enters the Maku
Tree, sees its animated eyes, and exits through a real warp.

Per-content inventory (enemies, NPCs, items, scripts): `COVERAGE.md`.
