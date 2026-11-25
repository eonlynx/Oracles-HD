# Progress and parity tracker

Snapshot: **2026-11-25**, commit `6081e40`, performance + dungeon-data block (Ralph `$37:$0d`, past
villager `$3a:$0d`, continuous room presentation), base
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
| .NET 8 build | Done | `net8.0-windows`, 0 errors, 48 pre-existing warnings. |
| Runtime startup | Done | `--oracle` boots directly into the generated overworld; opt-in `ORACLE_PERF_TRACE=1` timestamps startup/map loads (`docs/PERFORMANCE.md`). |
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
| Objects / interactions (first pass) | Partial | Source-derived collision/palette/flag/portal/entry-direction behaviour for every placed interaction; extra object-gfx sheets are resolved from the OAM tile range. `INTERAC_MAKU_TREE $87` composes `$04`+`$05`, so its eyes, expressions and smile render and blink. First scripts run (`ralphSubid0dScript`, `villagerSubid0dScript`); facing and general NPC dialogue loops still pending. |
| Object flags / spawn identity | Partial | `oracleSprite` carries `interactionId/subid`, source-derived requires/forbids flags, entry-direction gate, `obj_Condition`, before/after room flags and native collision (solid tile or explicit radii). `$6b:$15` post-game statue requires `GLOBALFLAG_FINISHEDGAME` and `PALH_c7`. Entry-gated actors now use prospective-edge presentation (ADR-037), not a boundary pop. |
| NPC collision | Done (source-derived) | `objectMarkSolidPosition`/`wRoomCollisions` -> 16x16 tile box; `objectSetCollideRadius/Radii` -> explicit box; otherwise no collision. The body is centred on the interaction anchor. One-shot cutscene actors are hidden unless their source entry condition holds; per-NPC script radii/events remain pending. |
| Sprite palettes 6/7 | Partial | `PALH_c7` sprite palette 6 is applied for `$6b:$15` from the source `PALH` load; general per-room sprite palette application is still pending. |
| Warps / interiors / doors (first pass) | Partial | 22 present (group 2) + 16 past (group 3) interiors from overworld warps; tile, pointer-position and screen-edge warps trigger DX-HD transitions. Dungeon maps now have a data slot for source/destination transition modes (walk, instant, fall); stairs/large-room routing remains pending. |
| Time portals (Ages) | Done (first pass) | `$e1` objects whose source calls `interactionBeginTimewarp` become `oracleTimePortal`; group toggles at the same world position through `MapTransitionSystem`, and the arrival portal is spawned after load. |
| Room palette | Done | Rooms use their tileset `PALH` (e.g. `$30` for `$38`); the `$8f` load belongs to the disappearance cutscene, not the static room. |
| Session state (`OracleSessionState`) | Done (first pass) | 0x80 global flags, flags per `(group, room)`, room state, entry direction and capture/restore hooks; `OracleFlagState` is a dev facade only. |
| Room lifecycle (`OracleRoomRuntime`) | Done (first pass) | `MapTransitionSystem.MapLoading` (before objects) and `MapLoaded` (after Link); continuous stitched-map room tracking; `OracleRoomEntryEffects` parses `roomSpecificCode.s`. |
| Room presentation (no pop-in) | Done (first pass) | Entry-gated actors are presented for the edge Link would cross, including neighbours; scripts start only on the real crossing; completed one-shot guards suppress the actor immediately. ADR-037. |
| Story-state visibility | Done (first pass) | One `OracleVisibilityEvaluator` reads global flags, room-state bits and source-cited rules keyed by `(id,subid,var03)`. Pre-intro `$6b:$01` audience and post-intro `$5d:$02` animals are sequential states, not cross-room exclusions. `--intro-done` selects the later state. |\n| Enemy atlas / Octorok | Partial | `enemyData`, `enemyAnimations` and `enemyOamData` compose into `oracle_ages_enemies.*`; specific Octoroks are placed and animated. Runtime AI/combat is the next step; random enemies remain intentionally unplaced. |\n| Script engine (`CutsceneCommandCatalog`/Runner) | Partial | Typed command stream + manifest and source-cited `(interaction, subid)` bindings; native counter1/counter2 order, callstack, jump/gravity and Ralph helpers. Verified `$37:$0d` (flag `$40`, delete) and `$3a:$0d` (flag `$41`, jump, move, delete). Text is deliberately skipped; `setanimation` records the index but the atlas still carries only the default animation; audio pending. |
| Game profile (`OracleGameProfile`) | Partial | Ages/Seasons flags (`.ifdef`-aware), portal policy, overworld groups, map naming and `--game`; Seasons content generation is still pending. |
| Enemy/part/item placements | Partial | Native object streams now emit typed placements (1,199 rows across groups 0-5 for Ages), including condition/room-flag gates and random-enemy marking. Runtime construction/AI is not wired yet. |\n| Interaction behaviour table | Done | Typed, sorted TSV + manifest (`Data/Map Objects/oracle_<game>_interaction_behaviors.*`), emitted from `InteractionBehaviors`. |
| Script command tables | Done (first family) | `Data/Maps/Scripts/oracle_<game>_scripts.*` and `oracle_<game>_script_bindings.*`; deterministic, sorted, with source hashes and completed-flag guards. Generated TSV/JSON are gitignored; manifests are committed. |
| Dungeon floor layouts | Partial | `dungeonData.s` + `dungeonLayouts.s` parsed into typed tables; `dungeons --dungeon N` builds all rooms of a dungeon (dungeon `$05` has 45 rooms/2 floors). Same-floor edge connections use room properties; stairs/fall routing and large rooms remain pending. |
| Text / dialogue | Not started | LA `.lng` files still used. The script host deliberately skips Oracle `showtext` (it logs the id) so cutscene flow can be verified before text import. |
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


