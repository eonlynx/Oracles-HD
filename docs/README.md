# Engineering documentation

These documents record how evidence from the Oracle disassembly moves through
the importer, the runtime and the checks. They are written for contributors and
coding agents; each document owns one topic and links to the others instead of
duplicating them.

## Start here

- Read `PROJECT_PRINCIPLES.md` before gameplay changes.
- Use the task map to find the document for your change.
- Use the code entry points to locate the owner; follow callers, not just the
  first class.
- Record a milestone in `STATUS.md` and the affected document in the same commit.

## Task map

| If you are changing... | Read |
|---|---|
| Fidelity, evidence order, definition of done | `PROJECT_PRINCIPLES.md` |
| What is done / partial / missing, map coverage | `STATUS.md` |
| Phases, acceptance criteria, next tasks | `ROADMAP.md` |
| Engine layers, runtime entry, planned contracts | `ARCHITECTURE.md` |
| Disassembly -> `.map`/atlas formats, collisions, terrain, animations | `FORMATS.md` |
| A design choice and its evidence | `DECISIONS.md` |
| Per-content inventory (enemies, NPCs, items, scripts) | `COVERAGE.md` |
| Build, SDK, publishing, troubleshooting | `BUILDING.md` |
| Licences and reference-use policy | `THIRD_PARTY.md` |
| Removing LA legacy | `CLEANUP.md` |
| WindowsDX vs multiplatform status | `PLATFORM.md` |
| Reproducible checks and recorded evidence | `VERIFICATION.md` |

## Code entry points

These are boundaries, not a complete class inventory.

| Task | Entry |
|---|---|
| Build / run | `build.cmd`, `Program.cs`, `Game1.cs` |
| Development boot into the overworld | `Game1.OracleOverworld`, `GameManager.StartNewGame`, `MapTransitionSystem.LoadMapFromFile` |
| World / camera / drawing | `InGame/Map/MapManager.cs`, `Map.cs`, `TileMap.cs`, `Camera.cs` |
| Tile collision / terrain | `SystemBody.Collision`, `Map.IsSolid`, `Map.ApplyTerrainToFieldStates` |
| Tile animation | `Map.TileAnimations`, `TileAnimation.cs`, `SaveLoadMap.LoadOracleSections` |
| Import CLI | `Tools/OracleAssetBuilder/Program.cs` |
| Overworld / rooms | `Tools/OracleAssetBuilder/OverworldBuilder.cs`, `RoomBuilder.cs` |
| Tables, overrides, aliases | `Tools/OracleAssetBuilder/OracleContent.cs`, `AsmParser.cs` |
| Tilesets / palettes / metatiles | `Tools/OracleAssetBuilder/TilesetRenderer.cs` |
| Animation decode / composition | `Tools/OracleAssetBuilder/OracleAnimation.cs`, `AnimationComposer.cs` |
| Map sections / manifests | `Tools/OracleAssetBuilder/DxMapWriter.cs`, `MetatileAtlas.cs` |

## Source hierarchy

1. Executed game / recorded trace.
2. `oracles-disasm`, corroborated by `oracles-decomp` and `top`.
3. `ooa-godot` as the default accelerator.
4. Assumptions, recorded in `DECISIONS.md`.

The canonical statement is in `PROJECT_PRINCIPLES.md`; do not repeat it in every
document.

## Documentation boundary

Keep durable contracts here: evidence rules, ownership, lifecycle, formats,
coverage, validation and contributor workflow. Keep source-specific constants
and branch details beside the importer or the runtime code. Do not add
implementation diaries, class inventories, per-feature summaries or duplicated
plans.

## Current snapshot

- Base: Links-Awakening-DX-HD v2.0.6 (`2e68849`).
- Latest implementation commit: `501fb1f` (animated tiles, tile collisions,
  Oracle overworld runtime).
- Target: `net8.0-windows` / MonoGame 3.8.1.303 WindowsDX; 0 build errors.
- Runtime entry: `Oracles-HD.exe oracle` (development switch).
- Assets: generated locally, gitignored; manifests committed.

## Update policy

Update `STATUS.md` and the affected document in the same commit as the
milestone. Keep exact counts and commit hashes. Do not mark a system done without
a check in `VERIFICATION.md`.
