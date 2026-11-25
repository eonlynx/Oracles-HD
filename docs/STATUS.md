# Progress and parity tracker

Snapshot: **2026-11-25**, commit `3540239`, Phase 3/4 vertical slice
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
| Camera policy | Done | DX-HD smooth follow everywhere, including houses; `Map.CameraTarget` only for explicit gameplay/puzzle locks. |
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
| Past cliff rewrite (6 -> 0) | Done | `setPastCliffPalettesToRed`: metatiles `$40..$7f`, collision mode 0, room byte `$38` excluded (group prefix masked). |
| Overrides (`checkTilesetOverride`) | Done | Room `$38` and past-cliff exceptions parsed from `loadTilesetData.s`; companion layout 1/3, Maku saved +2, Jabu +1 (CLI flags). |
| Animated tiles | Done | Frames in the existing atlas; timeline in `.map`; no `.ani`. |
| Tile collisions | Partial | Quadrant bits + water/hole/lava solid; special collisions (`>=0x10`, bridges/rails) not yet exact. |
| Terrain (`FieldStates`) | Partial | Water, deep water, lava, grass; swimming/falling not implemented. |
| Room-specific graphics (`roomGfxChanges`) | Not started | Shops, gasha spots and sign/tile rewrites are still pending. The Maku Tree room `$38` needs none: its tiles and palette match the original screenshot. |
| Objects / interactions (first pass) | Partial | Source-derived collision/palette/flag/portal/entry-direction behaviour for every placed interaction; extra object-gfx sheets are resolved from the OAM tile range. `INTERAC_MAKU_TREE $87` composes `$04`+`$05`, so its eyes, expressions and smile render and blink. Scripts/facing still pending. |
| Object flags / spawn identity | Partial | `oracleSprite` carries `interactionId/subid`, source-derived requires/forbids flags, entry-direction gate, `obj_Condition`, before/after room flags and native collision (solid tile or explicit radii). `$6b:$15` post-game statue requires `GLOBALFLAG_FINISHEDGAME` and `PALH_c7`. |
| NPC collision | Done (source-derived) | `objectMarkSolidPosition`/`wRoomCollisions` -> 16x16 tile box; `objectSetCollideRadius/Radii` -> explicit box; otherwise no collision. The body is centred on the interaction anchor. One-shot cutscene actors are hidden unless their source entry condition holds; per-NPC script radii/events remain pending. |
| Sprite palettes 6/7 | Partial | `PALH_c7` sprite palette 6 is applied for `$6b:$15` from the source `PALH` load; general per-room sprite palette application is still pending. |
| Warps / interiors / doors (first pass) | Partial | 22 present interiors (group 2) from group-0 warps; tile, pointer-position and screen-edge warps trigger DX-HD transitions. Group 1 overworld and time portals are in; past group 3 interiors and dungeons pending. |
| Time portals (Ages) | Done (first pass) | `$e1` objects whose source calls `interactionBeginTimewarp` become `oracleTimePortal`; group toggles at the same world position through `MapTransitionSystem`, and the arrival portal is spawned after load. |
| Room palette | Done | Rooms use their tileset `PALH` (e.g. `$30` for `$38`); the `$8f` load belongs to the disappearance cutscene, not the static room. |
| Session state (`OracleSessionState`) | Done (first pass) | 0x80 global flags, flags per `(group, room)`, room state, entry direction and capture/restore hooks; `OracleFlagState` is a dev facade only. |
| Room lifecycle (`OracleRoomRuntime`) | Done (first pass) | `MapTransitionSystem.MapLoading` (before objects) and `MapLoaded` (after Link); `OracleRoomEntryEffects` parses `roomSpecificCode.s`; Ralph/EnterPast events registered for Ages. |
| Game profile (`OracleGameProfile`) | Partial | Ages/Seasons flags (`.ifdef`-aware), portal policy, overworld groups, map naming and `--game`; Seasons content generation is still pending. |
| Interaction behaviour table | Done | Typed, sorted TSV + manifest (`Data/Map Objects/oracle_<game>_interaction_behaviors.*`), emitted from `InteractionBehaviors`. |
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

- Present overworld: 103 sprite objects, 13 interior-entry warps.
- Present interiors: 22 maps, 22 sprite objects, 11 exit warps.
- Entity atlas: 244 frame entries from 36 interaction ids.
- Maku Tree: 6 composed OAM frames (`$87:00`), blink timeline
  `3000,66,66,66,66,66` ms, palette header `$8f`. The importer composes the
  object-gfx chain `$04`+`$05` (`spr_makuadultsprites_1/2`), matching
  `interactionLoadExtraGraphics` and ooa-godot `Import-MakuTreeData.ps1`.
- Verified: enter room `0:66` -> interior `2:e7` through the door; exit `2:e7`
  through the bottom edge -> room `0:66`; eyes animate.

### Known Ages artifacts

- The Maku Tree canopy and lower band are **not** black in `0:$38`. The static
  tileset $22 (header `$70`: common + bottom) plus its tileset palette `$30`
  reproduce the original screenshot (`Oracle_Of_Ages_-_Maku_Tree.png`): teal
  pond, blue bubble foliage, brown trunk, face and pink flowers. The earlier
  black reading came from a missing object-gfx continuation sheet (`$05`, now
  composed from the OAM tile range) and from the incorrect static `$8f` palette
  override (now removed; `$8f` belongs to the disappearance cutscene).
- The four treetop cutscene rooms (`$e0`,`$e1`,`$e2`,`$f1`, tileset `$25`,
  header `$71`: common + top) render with their red-and-green canopy outline;
  background palette 0 is the shared `paletteData48e0` from `PALH_0f`.
- The past Maku room `1:$38` keeps its Maku-tree palette: `setPastCliffPalettesToRed`
  only rewrites metatiles `$40..$7f` and skips room byte `$38`. The teal pond and
  blue bubbles match the in-game reference; treating `ROOM_AGES_138` as `$138`
  or rewriting up to `$ff` turned them red.
- One-shot room-entry cutscene actors (e.g. Ralph `$37:$0d`) are evaluated by
  `OracleRoomRuntime`; they stay hidden unless their source entry condition
  holds. Their scripts/removal flags (e.g. the past villager `$3a:$0d` after flag
  `$41`) still need the command runner.
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
the present-day slice. Infrastructure for scripts is in: session state, room
load/enter hooks, room-entry effect table, game profile and typed interaction
behaviour table. Next: the script engine (`CutsceneCommandCatalog` +
`CutsceneCommandRunner`), then enemies/parts, dungeons and Seasons content.

Per-content inventory (enemies, NPCs, items, scripts): `COVERAGE.md`.
