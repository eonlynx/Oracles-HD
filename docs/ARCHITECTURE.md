# Architecture

## 1. Layers

```
oracles-disasm (pinned submodule, read-only)
        |
        v
Tools/OracleAssetBuilder (offline C# importer, no runtime dependency)
        |  emits DX-HD-native data + Oracle sections
        v
Data/Maps/*.map            Data/Maps/Tilesets/*.png
Data/Maps/*.manifest.json  Data/Maps/Previews/*.png   (generated, gitignored)
        |
        v
Oracles-HD runtime (C# / MonoGame, DX-HD engine)
```

The runtime never opens assembly files, `.bin` tables or a ROM. Generated
assets are reproducible from the pinned disassembly; the manifests record the
SHA-256 of inputs and outputs.

## 2. Runtime composition

| Layer | Owner | Notes |
|---|---|---|
| Application loop / screens | `Game1.cs`, `ScreenManager`, `Screens/` | v2.0.6 flow, unchanged except the development entry. |
| Game state | `GameManager` | Equipment, health, overlays, systems, save manager. |
| World | `MapManager`, `Map`, `TileMap`, `ObjectManager` | DX-HD large-map model; Oracle data is a normal map. |
| Physics | `SystemBody`, `ObjectManager.Collision` | Object collisions plus the new tile collision test. |
| Oracle state | `OracleSessionState`, `OracleRoomRuntime`, `OracleGameProfile` | Per-session 0x80 global flags, per-group/room flag bytes, room state, entry direction, capture/restore. The static `OracleFlagState` is a dev facade. |
| Room lifecycle | `MapTransitionSystem.MapLoading/MapLoaded`, `OracleRoomRuntime` | `MapLoading` runs after metadata and before objects; `MapLoaded` runs after Link is placed. Room-entry effects come from `roomSpecificCode.s`; stitched-map room crossings are tracked every frame. |
| Room presentation | `OracleScriptRuntime.UpdatePresentation` | Entry-gated actors are presented for the edge Link would use from the current room, including neighbours, so they do not pop in at the boundary. Scripts still start only on the real crossing. |
| Scripts | `InGame/Scripting/CutsceneCommandCatalog`, `CutsceneCommandRunner`, `OracleScriptRuntime` | Typed command stream + per-actor runner; native counter1/counter2 ordering; source-cited `(interaction, subid) -> script` bindings; `asm15` helpers for the imported families. |
| Collision/terrain data | `Map.CollisionMap`, `Map.TerrainMap` | Loaded from the `.map` Oracle sections. |
| Tile animation | `Map.TileAnimations`, `TileAnimation` | Swaps atlas indices; no extra files. |
| UI | `OverlayManager`, `Pages/`, `Interface/` | Still LA-shaped; to be replaced. |
| Audio | `GbsPlayer` | Reusable; Oracle GBS pending. |

## 3. Development entry

`Program.Main` sets `Game1.OracleOverworld` when the first argument is `oracle`.

On the first update after resources finish loading, `Game1.Update`:

1. changes to `Values.ScreenNameGame`;
2. calls `GameManager.StartNewGame(0, "Oracle")`.

`StartNewGame` selects `Game1.OracleStartMap` (default
`oracle_ages_g0_overworld.map`, or the selected profile's overworld) and spawns
Link at `Game1.OracleStartPosition` (default the centre of room `0x39`). The
development flags `--game ages|seasons`, `--room e7`, `--pos x,y`,
`--entry 0..3` and `--map <file>.map` override the game, map, position and the
forced native entry direction used by development entry. It uses the existing
`MapTransitionSystem.LoadMapFromFile`, so camera setup and the first fade are the
DX-HD paths. The switch is development-only; no menu entry was added.

## 4. Engine additions

| File | Change |
|---|---|
| `InGame/Map/Map.cs` | `CollisionMap`, `TerrainMap`, `TileAnimations`, `OracleInfo`; `IsSolid`, `ApplyTerrainToFieldStates`, `UpdateTileAnimations`. |
| `InGame/Map/OracleMapInfo.cs` | Reads group/era/kind/room/floor/camera metadata; `CameraTarget` remains an explicit gameplay override. |
| `InGame/Map/TileAnimation.cs` | New: per-cell frame/duration timeline. |
| `InGame/Map/MapManager.cs` | Calls `UpdateTileAnimations(Game1.DeltaTime)` each update. |
| `InGame/SaveLoad/SaveLoadMap.cs` | Parses the trailing Oracle sections after the native object list. |
| `InGame/GameObjects/Base/Systems/SystemBody.cs` | Blocks walking bodies against solid tiles before object collision. |
| `InGame/Things/GameManager.cs` | Oracle map/position selection in `StartNewGame`. |
| `InGame/Things/OracleSessionState.cs` | Per-session global/room flags, room state, entry direction and save snapshot. |
| `InGame/Map/OracleRoomRuntime.cs` | Room load/enter lifecycle, `roomSpecificCode` effects, continuous stitched-map room tracking and presentation hand-off. |
| `InGame/Map/OracleScriptRuntime.cs` | Active runners, source-cited actor bindings, neighbour presentation pass, `asm15` helpers and completion guards. |
| `InGame/Map/OracleVisibilityEvaluator.cs` | Single save/progress gate for all actors: global flags, room-state bits and per-entity story rules; no per-room exclusion. |
| `InGame/Map/OracleTileChangeCatalog.cs` | Applies `singleTileChanges.s` writes on map load/room entry; baked per-group rows carry room mask, atlas indices and collision/terrain. |
| `InGame/Scripting/CutsceneCommandCatalog.cs` | Validates and loads `oracle_<game>_scripts.tsv` and `oracle_<game>_script_bindings.tsv`. |
| `InGame/Scripting/CutsceneCommandRunner.cs` | Native-order interpreter (counter1/counter2, callstack, yield/block/end). |
| `InGame/Map/OracleGameProfile.cs` | Ages/Seasons flags, portal policy, groups and map naming. |
| `InGame/GameSystems/MapTransitionSystem.cs` | `MapLoading`/`MapLoaded` hooks and `PostLoadActions` (portal arrival). |
| `Game1.cs`, `Program.cs` | `--oracle`, `--game`, `--room`, `--pos`, `--entry`, `--map` development entry. |
| `InGame/GameObjects/Things/ObjOracleWarp.cs` | Tile/facing and edge warp trigger; carries the native source/destination transition byte and maps walk/instant/fall to existing `MapTransitionSystem` modes. |
| `Base/OraclePerf.cs` | Opt-in startup/map-load timestamps; no cost when `ORACLE_PERF_TRACE` is unset. |
| `InGame/GameObjects/Things/ObjOracleSprite.cs` | Draws composed OAM frames; source-derived collision, flag/entry gates and script visibility state. |
| `InGame/GameObjects/Things/ObjOracleTimePortal.cs` | `interactionBeginTimewarp` portals; toggles group through `MapTransitionSystem` and spawns the arrival portal. |
| `InGame/GameObjects/Things/OracleSpriteFrames.cs` | Shared frame/duration parsing for Oracle sprites. |
| `InGame/GameObjects/GameObjectTemplates.cs` | Registers `oracleWarp`, `oracleSprite`, `oracleTimePortal`; no parallel object system. |

Design constraints:

- No new user-facing feature, menu or shader.
- No `.ani` file and no extra data file for animations; tile frames live in the
  tile atlas and the timeline in the existing `.map`; object frames live in the
  shared entity atlas and the timeline in the `oracleSprite` object parameters.
- Collision/terrain live in the map file, so no extra per-map file.
- Legacy LA code is removed only when its Oracle replacement exists
  (`CLEANUP.md`).

### 4.1 Room presentation vs script activation

Native screen-at-a-time rooms create one-shot actors only for the direction of
the incoming scroll. A stitched DX-HD map crosses rooms without reloading, so
the runtime must not use "create it at the boundary" as its visibility model.

`OracleScriptRuntime.UpdatePresentation` runs every frame before the script
tick. For each entry-gated actor it computes the direction Link would use to
cross from the current room into the actor's room (the room-grid edge), and
shows the actor exactly when that prospective direction matches. Actors in
neighbouring rooms are therefore already composed before Link reaches them.
The actual script still starts only in `RoomEntered`, when the real crossing
happens and the real direction matches. A script that owns its actor (flicker,
temporary hide, delete) suppresses presentation until it ends.

Completed one-shot events are recognised at import time from the root guard
`jumpifglobalflagset FLAG, stubScript` and emitted as `completed_flag` in the
binding table. Once the flag is set, presentation and activation are both
suppressed, so revisiting the room cannot flash the actor for a frame.

This mirrors two references: `top` captures a neighbour in the state the game
creates for the edge the player would cross (its ghost), and `ooa-godot` refuses
an incoming entity that has not resolved its presentation during the scroll
preload. We keep DX-HD's continuous map and apply the same invariant without a
second emulator instance.

## 5. Importer structure

| File | Responsibility |
|---|---|
| `OracleRepo.cs` | Paths, PNG index, SHA-256, default roots. |
| `AsmParser.cs` | Header indices, gfx/palette headers, palette data, tileset data, layout-label aliases. |
| `OracleContent.cs` | Resolved tables, room tileset/overrides, layout and room files, renderer factory, tile properties, present Maku `PALH_8f` override parsed from `makuTree.s`. |
| `OracleGameConfig.cs` | Per-game overworld groups/interior group (importer counterpart of `OracleGameProfile`). |
| `RoomEntryEffects.cs` | `roomSpecificCode.s` group tables -> `#oracle-room-effects` rows. |
| `InteractionBehaviors.cs` | Source-derived collision/flag/palette/warp/entry facts + typed TSV/manifest. |
| `ScriptImporter.cs` | Resolves event script closures, encodes typed command indices/targets and emits source-cited actor bindings + manifest. |
| `DependencyAudit.cs` | Scans `object_code/*`, `objects/*`, `scripts/*`, `code/*` and exports every WRAM/global-flag/room-flag/object-stream condition with entity scope and source line. |
| `InteractionVisibility.cs` | Emits source-cited story-state rules and child-object groups for room actors (e.g. `$6b:$01` pre-intro audience, `$5d:$02` post-intro bear). |
| `EnemyAtlas.cs` | Composes enemy animations/OAM into `oracle_<game>_enemies.png/.atlas`; Octorok is the first species. |
| `TileChanges.cs` | Parses `singleTileChanges.s` into the exhaustive raw table and source citations; `OverworldBuilder` bakes per-group atlas/collision rows. |
| `TilesetRenderer.cs` | Two VRAM banks, palettes, metatile rendering, past cliffs, animation overrides. |
| `MetatileAtlas.cs` | Packed 16px metatiles, base and animation-variant keys. |
| `OracleAnimation.cs` | Groups/tracks/headers/sheets, `(delay, gfxIndex)` parsing. |
| `AnimationComposer.cs` | Per-metatile timeline, distinct variants, durations. |
| `DxMapWriter.cs` | Native `.map` plus Oracle sections and native object placements. |
| `RoomBuilder.cs` / `OverworldBuilder.cs` / `InteriorBuilder.cs` | Single rooms / stitched overworld / present and past interiors, collisions, terrain, animations, objects, manifests. |
| `Dungeons.cs` / `DungeonBuilder.cs` | `dungeonData.s` + `dungeonLayouts.s` -> typed dungeon tables and per-dungeon room maps. |
| `EntityPlacements.cs` | Native enemy/part/item placements -> typed TSV + manifest (random enemies marked, not invented). |
| `Warps.cs` | `warpSources.s`, `warpDestinations.s`, `warpTiles.s`, pointer expansion. |
| `RoomObjects.cs` | Native room object stream (`pointers.s`, `mainData.s`, enemy/extra data); positioned interactions and counts; group-aware. |
| `InteractionBehaviors.cs` | Source-derived collision/flag/palette/warp/entry-direction behaviour per interaction/subid; no id table. |
| `InteractionSprites.cs` | Interaction data, animation tables, contiguous OAM pointer stream, OAM-driven object-gfx continuation, OAM composition. |
| `EntityAtlas.cs` / `SpriteAtlasWriter.cs` | Shared entity plan (groups 0, 1 and present interiors), deterministic RGBA atlas + `.atlas`. |
| `Png.cs` | Dependency-free 2bpp indexed PNG reader and RGBA writer. |
| `Program.cs` | CLI: `room`, `overworld`, `interiors`, `inspect`, `inspect-anim`. |

## 6. Planned contracts (not implemented)

These are summarized here instead of separate speculative documents (ADR-027).

- **Save/state.** One `OracleSaveData` owner for the `0x550` file image; three
  files per game with a primary and a backup; explicit save boundaries only;
  transient state in `OracleRuntimeState`. Do not mirror save bytes. See
  `ROADMAP.md` phase 4.
- **Content systems (first pass implemented).** The importer reads positioned
  interactions and emits `oracleSprite`/`oracleTimePortal`/`oracleWarp` objects
  with source-derived behaviour. Still planned: the instance state/room-event
  refactor (AUDIT section 7), enemies/parts/item drops, a fixed-60 Hz script
  runner with actors/events/modal ownership, and scripted animation/facing.
  See `ROADMAP.md` and `COVERAGE.md`.
- **Scaling seams.** `docs/AUDIT.md` section 7 compares this runtime with
  `ooa-godot`. The agreed order is: instance `OracleSessionState`, room lifecycle
  + entry-effect table, `OracleGameProfile`, typed behaviour table, then scripts,
  enemies and dungeons. Do not replace DX-HD's `GameObject`/`ObjectManager`
  model with `ooa-godot`'s ECS.
- **Audio.** Reuse `GbsPlayer`; build an Oracle `.gbs` from
  `code/audio.s` + `audio/{game}`. No LA music fallback. WLA-DX is deferred
  (ADR-024).

## 7. Source hierarchy

The canonical hierarchy and fidelity rules are in `PROJECT_PRINCIPLES.md`. In
short: executed game/trace > `oracles-disasm`/`oracles-decomp`/`top` on
contradictions > `ooa-godot` as accelerator > assumptions in `DECISIONS.md`.



## 8. DX-HD native alignment audit (2026-11-25)

Status: **audit complete, refactor not started.** Detailed evidence and the
bounded plan live in `AUDIT.md` section 9. This section records the target
ownership so new work does not extend the parallel presentation layer.

### 8.1 What is already native

The base checkout `..\Links-Awakening-DX-HD` and this repository share the same
`InGame/GameObjects/Base` component tree byte-for-byte, except for the tile
solidity hook added to `Base/Systems/SystemBody.cs`. `Map.cs` adds
`CollisionMap`, `TerrainMap`, `TileAnimations` and `OracleInfo`; `MapManager.cs`
adds tile-animation updates; `ObjectManager.cs` only adds perf timers. The
native object templates for doors, chests, items, lights, darkeners, shadow
setters and teleporters are still registered in `GameObjectTemplates.cs`.

The problem is therefore not a missing engine capability. It is that Oracle
objects and maps do not use the capabilities:

- `ObjOracleSprite` only adds `DrawComponent` + optional `UpdateComponent` +
  optional collision. It has no body, shadow, AI, interact, pushable or light.
- `EnemyAtlas.AddPlacements` emits `oracleSprite` for enemies, so Octorok is an
  animation, not an enemy host.
- Oracle maps emit only `oracleSprite`, `oracleWarp` and `oracleTimePortal`;
  they never emit `dungeonBlacker`, `roomDarkener`, `light`, `spriteLight`,
  `shadowSetter`, `shadowDisabler`, `door`, `chest` or `item`.
- `Map.UseLight` stays false and no Oracle actor has `DrawShadowComponent`, so
  the native light and shadow passes have nothing to draw.
- `ObjOracleSprite` anchors `EntityPosition` at the OAM top-left. Native actors
  anchor at their feet and use `BodyDrawComponent`; `DrawComponent` sorts by
  `Layer` then `Position.Y`, so tall Oracle actors currently sort by their head.
- `OracleScriptRuntime` moves `EntityPosition`/Z directly instead of using
  `BodyComponent`/`SystemBody`.

`Map.CollisionMap` is not a parallel collision system: it is consumed by the
native `SystemBody.Collision` hook. The missing half is that Oracle actors have
no bodies, so only Link currently benefits from it.

### 8.2 Target ownership

| Concern | Owner after re-alignment | Must not own it |
|---|---|---|
| Sprite frames/timeline | Imported atlas + native `BaseAnimationComponent`/`AnimationComponent` | Private frame counters in `ObjOracleSprite` |
| Feet anchor / Y-sort | `EntityPosition` at feet + `BodyDrawComponent`/negative `EntitySize` | Top-left OAM position used for sorting |
| Gravity, collision, water/grass | `BodyComponent` + native `SystemBody`; `Map.IsSolid` tile hook | Script writes to `EntityPosition` |
| Contact shadow | `BodyDrawShadowComponent`/`DrawShadowCSpriteComponent`/`DrawShadowSpriteComponent` | Per-object shadow hacks |
| NPC interaction | `InteractComponent`; `KeyChangeListenerComponent` for save keys | Script runtime polling |
| Enemy combat | `AiComponent`, `HittableComponent`, `DamageFieldComponent`, `PushableComponent`, `AiDamageState` | `oracleSprite` |
| Room darkening | Native `dungeonBlacker`/`caveBlacker`, `roomDarkener`, `dungeonBlackRoom`, `ObjLamp`, `ObjLight` | Custom room-light switch |
| Shadow tuning | `shadowSetter`/`shadowDisabler`, `Map.ShadowHeight`/`Rotation` | Hardcoded defaults per room |
| Doors/chests/teleporters | `ObjDoor`, `ObjOverworldTeleporter`, `ObjChest`, `ObjItem` + native collision/interact | Custom per-frame trigger polling |
| Room identity in stitched map | Thin `OracleRoomRuntime` (resolve/publish/dispatch) | Sprite/physics/light ownership |
| Room tile-load writes | Map variants; a small load-time object only while continuous-map variants are unavailable | Global catalog as a second lifecycle |
| Save/progress | `OracleSessionState` until native save integration | Ad-hoc flags in objects |

### 8.3 Refactor phases

1. **P0 actor host + shadows (blocking).** Refactor `ObjOracleSprite` or add
   `ObjOracleActor`: feet anchor, `BodyComponent`, `BodyDrawComponent`,
   `BodyDrawShadowComponent`, `AnimationComponent`; attach collision/interact
   only where the source interaction demands it. Move script motion/jump/Z to
   the body. Verify Maku audience/bear/rabbit and Ralph first.
2. **P0 room atmosphere (blocking).** Emit native darkeners, lamps/lights and
   shadow setters from imported Oracle data. `PART_DARK_ROOM_HANDLER $08`,
   `wNumTorchesLit` and the dungeon `DARK_BIT` map to `roomDarkener` +
   `ObjLamp`/`ObjLight` and `dungeonBlacker`/`caveBlacker`. The existing
   `GameManager` shadow/light shader path is reused unchanged.
3. **P1 enemies.** First `ObjOracleOctorok` using native body/AI/hittable/
   damage/pushable/shadow components; `EnemyAtlas` stays the frame source.
4. **P1 doors/chests/teleporters.** Map warp/item data to native templates and
   collision components; keep only source-specific adapters.
5. **P2 room/map ownership.** Keep `.map` v3 + `.map.data`; reduce
   `#oracle-map` to identity; move room effects to native objects; keep the thin
   continuous-map room host. Retire `OracleTileChangeCatalog` into map variants
   or a native load-time object where possible.
6. **P2 verification.** Add shadow/light/Y-sort screenshots and perf traces for
   Maku present/past, cave/dark room, dungeon, Octorok, tall NPC overlap, portal
   and door. No new enemy/dungeon feature before phases 1-2 are green.

### 8.4 Non-goals

- No second `GameObject`/entity system or ECS.
- No new shader or per-room colour table while native `LightShader`,
  `LightDrawComponent` and `Map.UseLight/LightColor/LightState` are available.
- No room/NPC coordinate or colour hardcodes in runtime code.
- No room-sized map migration that breaks the DX-HD continuous camera.
- No LA enemy AI reused as Oracle AI; reuse the component wiring and port the
  Oracle source state machine.
