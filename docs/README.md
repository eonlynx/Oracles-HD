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
| Fidelity, evidence order, definition of done, pre-commit gate | `PROJECT_PRINCIPLES.md` |
| Abstraction debt, boundary audit, go/no-go | `AUDIT.md` |
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

## Coherence audit

This is a consistency check across the documents, not a second source of truth.
When two documents disagree, the owner below wins and the other document is
corrected in the same commit.

| Topic | Owner | Must not be restated as authority in |
|---|---|---|
| Fidelity rules, evidence order, reference reuse | `PROJECT_PRINCIPLES.md` | any other document |
| What is done / partial / missing, counts | `STATUS.md` | `README.md`, `ROADMAP.md` |
| Phases, acceptance, next tasks | `ROADMAP.md` | `STATUS.md` |
| Engine layers, runtime entry, planned contracts | `ARCHITECTURE.md` | `README.md` |
| `.map`/atlas/object/audio formats | `FORMATS.md` | `ARCHITECTURE.md` |
| Chosen design and evidence | `DECISIONS.md` | `PROJECT_PRINCIPLES.md` |
| Per-content inventory | `COVERAGE.md` | `STATUS.md` |
| Commands and exact checks | `VERIFICATION.md` | `BUILDING.md` |
| SDK, publish, troubleshooting | `BUILDING.md` | `README.md` |
| Licences and reuse caveats | `THIRD_PARTY.md` | `DECISIONS.md` |
| LA legacy inventory | `CLEANUP.md` | `STATUS.md` |
| WindowsDX vs multiplatform | `PLATFORM.md` | `README.md` |

Rules:

1. Counts and commit hashes live in `STATUS.md`; `docs/README.md` only names the
   current implementation commit as a navigation aid.
2. "Not started" means no verified implementation. A working-tree slice is
   "Partial" until its check is recorded in `VERIFICATION.md`.
3. A new format gets one `FORMATS.md` entry, a new command gets `BUILDING.md` and
   `VERIFICATION.md`, and a new content system gets `COVERAGE.md` rows.
4. A design choice that changes behaviour or evidence order gets an ADR; do not
   bury it in a status table.
5. `README.md` (root) is a short entry point. It links to owners and must not
   duplicate build steps, counts, licences or limitations.

Audit result for 2026-11-25 (Phase 3/4 slice):

- The source hierarchy is stated once in `PROJECT_PRINCIPLES.md` and referenced
  by `README.md` and `THIRD_PARTY.md`.
- `STATUS.md` owns counts; `ROADMAP.md` owns next tasks. The earlier duplication
  was removed in `4caed59`.
- `FORMATS.md` is the only owner of `.map` sections, native object fields and the
  entity atlas layout. ADR-029 records the anchor/palette/warp-trigger
  decisions.
- `STATUS.md` and `COVERAGE.md` now say objects/warps are **Partial**, matching
  the verified `0:66` <-> `2:e7` warp, the animated `$87` eyes and the 38
  composed interaction ids. They do not claim enemies, scripts or
  `roomGfxChanges`.
- `README.md` (root) links to `STATUS.md`/`COVERAGE.md` for limitations instead
  of maintaining its own list.
- No document claims the Maku canopy is done; the black band is explicitly
  attributed to `roomGfxChanges` in `STATUS.md`, `FORMATS.md` and
  `VERIFICATION.md`.

Single pipeline:

```
oracles-disasm tables + ooa-godot/decomp/top algorithms
        |
        v
Tools/OracleAssetBuilder (only reader of .s/.bin/.png)
        |
        +--> oracle_*.map        tiles, collision, terrain, animations,
        |                        oracleSprite/oracleWarp native objects
        +--> oracle_*_entities   shared RGBA atlas + .atlas origins
        |
        v
DX-HD runtime: TileMap, MapTransitionSystem, ObjectManager, Camera
```

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
| Overworld / rooms / interiors | `Tools/OracleAssetBuilder/OverworldBuilder.cs`, `RoomBuilder.cs`, `InteriorBuilder.cs` |
| Tables, overrides, aliases | `Tools/OracleAssetBuilder/OracleContent.cs`, `AsmParser.cs` |
| Tilesets / palettes / metatiles | `Tools/OracleAssetBuilder/TilesetRenderer.cs` |
| Animation decode / composition | `Tools/OracleAssetBuilder/OracleAnimation.cs`, `AnimationComposer.cs` |
| Warps / object streams / interaction sprites | `Tools/OracleAssetBuilder/Warps.cs`, `RoomObjects.cs`, `InteractionSprites.cs` |
| Map sections, native objects, manifests | `Tools/OracleAssetBuilder/DxMapWriter.cs`, `MetatileAtlas.cs`, `SpriteAtlasWriter.cs` |
| Oracle warps / sprites in the runtime | `InGame/GameObjects/Things/ObjOracleWarp.cs`, `ObjOracleSprite.cs` |

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
- Latest implementation commit: `b3a0bfe` (present interiors, warps, Oracle
  object pipeline, animated Maku eyes).
- Target: `net8.0-windows` / MonoGame 3.8.1.303 WindowsDX; 0 build errors.
- Runtime entry: `Oracles-HD.exe oracle` (development switch).
- Assets: generated locally, gitignored; manifests committed.

## Update policy

Update `STATUS.md` and the affected document in the same commit as the
milestone. Keep exact counts and commit hashes. Do not mark a system done without
a check in `VERIFICATION.md`.
