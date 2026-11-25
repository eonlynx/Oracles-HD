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
| Room lifecycle | `MapTransitionSystem.MapLoading/MapLoaded`, `OracleRoomRuntime` | `MapLoading` runs after metadata and before objects; `MapLoaded` runs after Link is placed. Room-entry effects come from `roomSpecificCode.s`. |
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
development flags `--game ages|seasons`, `--room e7`, `--pos x,y` and
`--map <file>.map` override the game, map and position. It uses the existing
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
| `InGame/Map/OracleRoomRuntime.cs` | Room load/enter lifecycle, `roomSpecificCode` effects and code-hosted events (Ralph/EnterPast). |
| `InGame/Map/OracleGameProfile.cs` | Ages/Seasons flags, portal policy, groups and map naming. |
| `InGame/GameSystems/MapTransitionSystem.cs` | `MapLoading`/`MapLoaded` hooks and `PostLoadActions` (portal arrival). |
| `Game1.cs`, `Program.cs` | `--oracle`, `--game`, `--room`, `--pos`, `--map` development entry. |
| `InGame/GameObjects/Things/ObjOracleWarp.cs` | Tile/facing and edge warp trigger; routes through `MapTransitionSystem`. |
| `InGame/GameObjects/Things/ObjOracleSprite.cs` | Draws composed OAM frames; source-derived collision, flag/entry gates. |
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

## 5. Importer structure

| File | Responsibility |
|---|---|
| `OracleRepo.cs` | Paths, PNG index, SHA-256, default roots. |
| `AsmParser.cs` | Header indices, gfx/palette headers, palette data, tileset data, layout-label aliases. |
| `OracleContent.cs` | Resolved tables, room tileset/overrides, layout and room files, renderer factory, tile properties. |
| `OracleGameConfig.cs` | Per-game overworld groups/interior group (importer counterpart of `OracleGameProfile`). |
| `RoomEntryEffects.cs` | `roomSpecificCode.s` group tables -> `#oracle-room-effects` rows. |
| `InteractionBehaviors.cs` | Source-derived collision/flag/palette/warp/entry facts + typed TSV/manifest. |
| `TilesetRenderer.cs` | Two VRAM banks, palettes, metatile rendering, past cliffs, animation overrides. |
| `MetatileAtlas.cs` | Packed 16px metatiles, base and animation-variant keys. |
| `OracleAnimation.cs` | Groups/tracks/headers/sheets, `(delay, gfxIndex)` parsing. |
| `AnimationComposer.cs` | Per-metatile timeline, distinct variants, durations. |
| `DxMapWriter.cs` | Native `.map` plus Oracle sections and native object placements. |
| `RoomBuilder.cs` / `OverworldBuilder.cs` / `InteriorBuilder.cs` | Single rooms / stitched overworld / present interiors, collisions, terrain, animations, objects, manifests. |
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
