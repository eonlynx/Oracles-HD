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
| Collision/terrain data | `Map.CollisionMap`, `Map.TerrainMap` | Loaded from the `.map` Oracle sections. |
| Tile animation | `Map.TileAnimations`, `TileAnimation` | Swaps atlas indices; no extra files. |
| UI | `OverlayManager`, `Pages/`, `Interface/` | Still LA-shaped; to be replaced. |
| Audio | `GbsPlayer` | Reusable; Oracle GBS pending. |

## 3. Development entry

`Program.Main` sets `Game1.OracleOverworld` when the first argument is `oracle`.

On the first update after resources finish loading, `Game1.Update`:

1. changes to `Values.ScreenNameGame`;
2. calls `GameManager.StartNewGame(0, "Oracle")`.

`StartNewGame` selects `oracle_ages_g0_overworld.map` and spawns Link at the
centre of room `0x39` (`9*160 + 80`, `3*128 + 64`). It uses the existing
`MapTransitionSystem.LoadMapFromFile`, so camera setup and the first fade are
the DX-HD paths. The switch is development-only; no menu entry was added.

## 4. Engine additions

| File | Change |
|---|---|
| `InGame/Map/Map.cs` | `CollisionMap`, `TerrainMap`, `TileAnimations`; `IsSolid`, `ApplyTerrainToFieldStates`, `UpdateTileAnimations`. |
| `InGame/Map/TileAnimation.cs` | New: per-cell frame/duration timeline. |
| `InGame/Map/MapManager.cs` | Calls `UpdateTileAnimations(Game1.DeltaTime)` each update. |
| `InGame/SaveLoad/SaveLoadMap.cs` | Parses the trailing Oracle sections after the native object list. |
| `InGame/GameObjects/Base/Systems/SystemBody.cs` | Blocks walking bodies against solid tiles before object collision. |
| `InGame/Things/GameManager.cs` | Oracle map/position selection in `StartNewGame`. |
| `Game1.cs`, `Program.cs` | `--oracle` development entry. |

Design constraints:

- No new user-facing feature, menu or shader.
- No `.ani` file and no extra data file for animations; frames live in the
  existing atlas and the timeline in the existing `.map`.
- Collision/terrain live in the map file, so no extra per-map file.
- Legacy LA code is removed only when its Oracle replacement exists
  (`CLEANUP.md`).

## 5. Importer structure

| File | Responsibility |
|---|---|
| `OracleRepo.cs` | Paths, PNG index, SHA-256, default roots. |
| `AsmParser.cs` | Header indices, gfx/palette headers, palette data, tileset data, layout-label aliases. |
| `OracleContent.cs` | Resolved tables, room tileset/overrides, layout and room files, renderer factory, tile properties. |
| `TilesetRenderer.cs` | Two VRAM banks, palettes, metatile rendering, past cliffs, animation overrides. |
| `MetatileAtlas.cs` | Packed 16px metatiles, base and animation-variant keys. |
| `OracleAnimation.cs` | Groups/tracks/headers/sheets, `(delay, gfxIndex)` parsing. |
| `AnimationComposer.cs` | Per-metatile timeline, distinct variants, durations. |
| `DxMapWriter.cs` | Native `.map` plus Oracle sections. |
| `RoomBuilder.cs` / `OverworldBuilder.cs` | Single rooms / 14x14 or 16x16 stitching, collisions, terrain, animations, manifests. |
| `Png.cs` | Dependency-free 2bpp indexed PNG reader and RGBA writer. |
| `Program.cs` | CLI: `room`, `overworld`, `inspect`, `inspect-anim`. |

## 6. Planned contracts (not implemented)

These are summarized here instead of separate speculative documents (ADR-027).

- **Save/state.** One `OracleSaveData` owner for the `0x550` file image; three
  files per game with a primary and a backup; explicit save boundaries only;
  transient state in `OracleRuntimeState`. Do not mirror save bytes. See
  `ROADMAP.md` phase 4.
- **Content systems.** Room object stream -> interactions/enemies/parts/items;
  a fixed-60 Hz script runner with actors, events and modal ownership. First
  target: `INTERAC_MAKU_TREE` ($87) and its eyes. See `ROADMAP.md` and
  `COVERAGE.md`.
- **Audio.** Reuse `GbsPlayer`; build an Oracle `.gbs` from
  `code/audio.s` + `audio/{game}`. No LA music fallback. WLA-DX is deferred
  (ADR-024).

## 7. Source hierarchy

The canonical hierarchy and fidelity rules are in `PROJECT_PRINCIPLES.md`. In
short: executed game/trace > `oracles-disasm`/`oracles-decomp`/`top` on
contradictions > `ooa-godot` as accelerator > assumptions in `DECISIONS.md`.
