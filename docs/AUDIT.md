# Architecture audit - Phase 3/4 slice

Snapshot: **2026-11-25**, implementation `b5119ed`. This audit covers the
present slice, the past overworld, time portals and the plumbing that will be
reused by scripts, enemies, dungeons and Seasons. It records structural debt,
not new features.

## 1. Verdict

| Next block | Verdict | Why |
|---|---|---|
| Bytecode scripts / cutscenes | **GO** | `OracleSessionState`, `OracleRoomRuntime` (load/enter hooks + entry-effect table), `OracleGameProfile` and the typed behaviour table are in; the command runner is the next block. |
| Enemies / combat | **NO-GO yet** | Object streams only emit interactions; enemy/PART/item placements, handler identity and a runtime construction seam are missing. |
| Dungeons | **NO-GO yet** | Metadata exists, but stairs/fall/source-transition warp modes, large rooms, floor layouts, per-room flags and camera locks remain. |
| Seasons | **NO-GO for content yet** | `OracleGameProfile` now holds flags/portal/groups and `--game` is generic, but no Seasons maps/content are generated and the Ages-only events are gated by profile. |
| `roomGfxChanges` | **GO** | It is importer/render work; it does not require a new runtime abstraction. Maku `$38` uses its tileset `$30` palette plus the `PALH_8f` wood palette loaded by `makuTree.s`; `singleTileChanges` is now a separate data-driven pass. |

The current slice is coherent enough to keep; it is not yet coherent enough to
scale to scripts, enemies or floors without the small ownership refactor in
section 7.


## 2. Translation boundary (importer vs runtime)

Conversions that are correctly confined to `OracleAssetBuilder`:

- GB YX -> pixel spawn, including the group-0/1 stitched room offset.
- Oracle direction -> DX-HD direction for the destination spawn.
- OAM bounding-box origin (`rawMinX - 8, rawMinY - 16`) and interaction anchor.
- OAM frame duration ticks -> milliseconds; loop-start frame index.
- Warp source/destination indices -> map file name and spawn coordinates.
- Tile slots, VRAM banks, palette slots, metatile decode, past cliffs, Maku
  `PALH_8f`, tile animation timelines.
- Room object stream -> native `oracleSprite` / `oracleWarp` fields.

Runtime code contains **no GB pixel, tile-id, OAM or palette conversion**. The
remaining runtime logic is trigger/behaviour logic, which is acceptable, but the
following are bridges:

| Bridge | File | Risk | Minimal action |
|---|---|---|---|
| Facing-tile warp trigger | `ObjOracleWarp.Update` | A door can trigger while turning, and it is stricter/different from the GB tile check. | Keep for present doors; add a `mode` field for stairs/falls and compare with `ObjDoor` before dungeons. |
| `_armed` spawn suppression | `ObjOracleWarp` | Duplicates the native "entered warp position" concept; save/respawn may need the same rule. | Move to map metadata or a transition entry state when respawn/save is implemented. |
| Hardcoded tile size | `ObjOracleWarp` (`/16`) | Breaks if a map uses a different tile size. | Use `Values.TileSize`. |
| Always a fade transition | `ObjOracleWarp` | Source `$3` (leave screen), `$4` (instant), `$9` (fall) and dungeon stairs are not represented. | Add `sourceTransition` and implement walk/fall/instant modes. |
| Top-left depth anchor | `ObjOracleSprite` | Tall actors sort by the top of the OAM canvas, not their base. A `DrawOffset` + base anchor attempt was reverted because the DX-HD draw pool culled/misrendered it. | Keep the working visual; document the limitation. Revisit with a proper draw-pool anchor or native `ObjSprite` pattern. |
| Map name as identity | Map object fields | No group/era/room/dungeon/floor metadata at runtime. | Add a `#oracle-map v1` header. |

## 3. Alignment with DX-HD abstractions

- **Warps**: `ObjOracleWarp` routes through the native `MapTransitionSystem`; there
  is no second transition engine. It is closer to a content trigger than a
  replacement for `ObjDoor`. Debt is transition modes and spawn suppression, not
  the transition path.
- **Sprites**: `ObjOracleSprite` uses `CSprite` + `DrawCSpriteComponent` and the
  shared entity atlas. It carries the semantic interaction id/subid, source
  collision, flag/entry gates and the composed frame timeline. Facing, shadow
  and script-driven animation still belong to the future interaction runtime.
- **Continuous overworld**: the stitched map has no room boundaries in the tile
  data, but `#oracle-map v1` provides game/group/era/grid and the room can be
  derived from world coordinates. Enemies, room music, scripts, camera locks and
  respawn still need a room owner, not another coordinate rule.
- **Collisions**: `SystemBody` applies `Map.IsSolid` to every body with
  `Normal`/`NPCWall`, so enemies will inherit tile collisions. Special
  collisions (`>=0x10`) are still treated free; water/hole/lava are conservative.
  This is a known approximation, not a runtime conversion leak.
- **Camera**: the follow camera matches DX-HD in overworlds, houses and joined
  rooms. Dungeons/large rooms need clamping or an explicit source-driven lock;
  `Map.CameraTarget` remains the gameplay override, not a size rule.

## 4. Future conflicts

### Enemies

1. `oracleSprite` now carries `interactionId/subid`, but it has no AI, health,
   damage, drops or components. Enemies need a construction seam, not another
   presentation object.
2. `RoomObjects` is a text parser that treats conditions as a flag and does not
   evaluate `obj_BeforeEvent`/`obj_AfterEvent` or the native conditional skip.
   It is enough for static interactions, not for enemy placement.
3. Standard sprite palettes 0-5 only; `$6b:$15` gets `PALH_c7` from the source
   `PALH` load and the gate from `GLOBALFLAG_FINISHEDGAME`. General per-room
   sprite-palette headers are still pending.
4. `RoomObjectData` counts overworld/interior enemies; none are placed or ticked
   yet.

### Present <-> past

1. `#oracle-map v1` now carries game/group/era/kind/room, so transitions no
   longer derive era from file names. The two overworlds are separate maps and
   the portal toggles `group ^ 1` at the same world position.
2. Warp/portal targets are still map-name strings built by the importer. A map
   registry will be needed for save restoration, dungeon entrances and Seasons,
   but not for the current portals.
3. Save/room flags are not persisted yet (`OracleFlagState` is session-only),
   so portal availability and one-shot events reset on restart.

### Dungeons

1. `#oracle-map v1` carries `dungeon` and `floor`, but floor layout tables,
   multi-floor stitching and large-room selection are not wired.
2. `OracleWarpData` does not implement the Ages dungeon-stair fallback or `$9`
   fall source transitions.
3. Large-room layouts are loaded by `RoomBuilder --large` but `InteriorBuilder`
   always builds 10x8; dungeon generation will need explicit 15x11 selection.
4. Conditions, shutters, blocks, minibosses and room state are script/room
   systems, not static objects.

## 5. Cleanup performed in this audit

- Removed `InGame/Things/Values.cs.bak` (tracked backup file, unused).
- Removed the unused `Warps` facing table parser, `Destinations` accessor and
  `_facing` field.
- Removed `InteractionSprites._game`, the unused animation `Parameter`, and the
  unused `ComposedInteraction.Width/Height`.
- Removed the pointer-placeholder hack in interaction data; subid tables are now
  resolved directly.
- Fixed `RoomObjects` `obj_End`/`obj_EndPointer` control flow and removed the
  fake `obj_Part` placements; parts/enemies/items are counted, not faked as
  interactions.
- Made the enemy/part/item/conditional counts visible in the CLI summary, so
  the remaining coverage is explicit.
- Added a palette-capability filter for objects without palette context, then
  replaced it for the post-game Link statue with the source-backed
  `PALH_c7`/palette-6 override and `GLOBALFLAG_FINISHEDGAME` spawn gate.
- Reverted the `DrawOffset` depth-anchor experiment after it misrendered the
  Maku eyes; the working top-left anchor is documented as a limitation.
- Updated the tool README and the permanent quality gate.

## 6. Minimal refactor before past/dungeons

1. **Map metadata header** (`#oracle-map v1`): game, group, era, map kind
   (overworld/interior/dungeon), room size, grid, current room, dungeon/floor,
   camera mode. Importer emits it; `Map` parses it. This is the single highest
   value cleanup.
2. **Semantic object identity**: add `interactionId/subid` (or a new
   `oracleInteraction` object that owns an `oracleSprite` presentation child).
   Keep the pure visual mode for cutscene props.
3. **Typed object stream**: replace the text-only `RoomObjects` with one reader
   that handles conditions, `obj_BeforeEvent`/`obj_AfterEvent`, enemies, parts and
   item drops. Do this before enemy AI.
4. **Warp modes**: add `sourceTransition`/`mode` to `oracleWarp`; implement
   walk-off, instant, fall and dungeon stairs through the existing
   `MapTransitionSystem`/`ObjLink` state. Reuse `ObjDoor` semantics where they
   fit; do not add a second transition system.
5. **Sprite palette context**: per-room `m_PaletteHeaderSpr` remains; object
   extra graphics and source-derived collision/flags/palettes/entry gates are
   ported (`InteractionBehaviors`, OAM-driven object-gfx chain). Per-room sprite
   palettes 6/7 and script-driven one-shot removals remain.
6. **Camera policy**: lock small maps to their centre or clamp to bounds; add a
   dungeon camera mode. Use `Map.CameraTarget`.

`roomGfxChanges` can start now; it will naturally cover item 5 for static room
graphics. Items 1-4 should land before the first past/dungeon playable slice.
Legacy LA HUD/save/audio do not block map traversal, but they block progression
and must be replaced before a full Ages run.

## 7. Structural comparison with ooa-godot (2026-11-25)

`ooa-godot` is a full Oracle engine and content pack. We deliberately embed
Oracle content in DX-HD's `GameObject`/`MapTransitionSystem` runtime. The goal is
not to clone its ECS; it is to copy the **ownership seams** that keep content
importable and save-compatible as scripts, enemies, floors and Seasons arrive.

### 7.0 `ooa-godot` data map (for orientation)

- **Rooms:** `IRoomContent`/`AgesWorldData` load a group/room from room packs;
  `OracleRoomData` owns layout, collisions, mappings, palettes and animations;
  `RoomSession` owns the active room and visits.
- **Warps:** `WarpDatabase` resolves sources/destinations; the transition
  controller applies scroll/palette modes.
- **Entities:** native object streams are executable instructions
  (`RoomEntityManager.ObjectStreams`); `IRoomEntityFactory` maps stream
  instructions to typed entities; capability interfaces expose AI, body, items
  and cutscene hooks.
- **Behaviours:** per-feature import producers validate the exact source and
  write generated tables (`Import-MakuTreeData`, `Import-NpcData`,
  `Import-WorldAssets`, ...); runtime handlers read those rows.
- **Room-entry effects:** `RoomEntryEffectDatabase` (7 rows) + `IRoomEntryEvent`
  hosts for cutscenes such as Ralph/EnterPast/Maku disappearance.
- **Flags/save:** `OracleSaveData` is the native `$550` image; `GlobalFlagDatabase`
  maps names/values; `OracleRuntimeState` mirrors live WRAM fields.

| Concern | `ooa-godot` | Oracles-HD today | Verdict / action |
|---|---|---|---|
| Import products | `tools/import_oracles/{Shared,Ages,Seasons}` producers validate ROM + disasm and write per-game `assets/oracle/<game>/` tables/gfx with schemas and manifests; runtime `GeneratedTable`/databases read them. | `OracleAssetBuilder` commands (`room`, `overworld`, `interiors`) parse `.s` on demand and write `.map` + manifests; `InteractionBehaviors` scans each interaction block. | Keep the command builders. New cross-cutting facts (behaviours, scripts, room-entry effects, enemies) should become typed generated tables with schema validation, not more regex inside builders. |
| Room content | `IRoomContent.LoadRoom(group, room, save, runtime, tick, toggle)` + `RoomSession` own the current room, animation tick, visits, minimap, palette and room state. | `OracleRoomRuntime` owns current group/room/entry direction over `MapManager`; `MapTransitionSystem.MapLoading/MapLoaded` fire around object construction and Link placement. | Keep the small runtime; do not copy the full `RoomSession` until save/minimap/animations need it. |
| Entity lifecycle | `IRoomEntityFactory` constructs content; `RoomEntityManager` executes ordered object streams, slots, freezes, outgoing sets and capability interfaces. | `ObjectManager.ObjectList` + DX-HD `GameObject` constructors; `oracleSprite`/`oracleWarp`/`oracleTimePortal` read flat `.map` parameters. | Add an `OracleEntityFactory`/registry (id/subid -> constructor) and typed placement records before enemy AI. Use DX-HD pools, `SystemBody`, `SystemAi`; do not import the ECS. |
| Scripts | `CutsceneCommandCatalog` loads schema-validated command streams; `CutsceneCommandRunner` + host interfaces execute them; event databases bind actors/flags/text. | `CutsceneCommandCatalog` loads a typed TSV + binding table; `CutsceneCommandRunner` runs native counter/call semantics; `OracleScriptRuntime` binds source-cited actors. Text/audio are host no-ops. | Keep the shape; add text, animation variants and more opcodes as their owners land. |
| State / save | `OracleSaveData` is the native `$550` image (0x80 global flags, per-room flag tables, visited/minimap, inventory); `OracleRuntimeState` mirrors live WRAM fields. | `OracleSessionState` is an instance with 0x80 global flags, per-group/room flag bytes, room state, entry direction and capture/restore; save-image wiring is still pending. `OracleFlagState` is a dev facade. | Keep the session; bind it to `OracleSaveData` when the save owner lands. |
| Room-entry effects | `RoomEntryEffectDatabase` is a 7-row data table (group, room, handler, source) plus `IRoomEntryEvent` cutscene hosts; e.g. Ralph portal music/departure. | `RoomEntryEffects` parses `roomSpecificCode.s` and emits `#oracle-room-effects v1`; `OracleRoomRuntime` dispatches handlers; `OracleScriptRuntime` owns the Ralph/EnterPast script bindings and presentation. | Keep the table; add music/minimap/respawn owners as they land. |
| Transitions | `RoomTransitionController`/`ScreenTransitionRenderer` with room packs, palette handoff, scroll modes. | `MapTransitionSystem` + warps/portals; warp mode is a fade only. | Keep DX-HD transitions; add `sourceTransition`/mode (walk-off, instant, fall, stairs) and floor metadata. |
| Game packs | `Games/Ages` and `Games/Seasons` content + enums; `Features` shared; pack identity validated (save/runtime/content must match). | Importer understands `--game`, but runtime/tool constants are Ages: `OracleFlagState` flags, portal behavior, `--room` `oracle_ages_g2`, Ages tileset-override parser. | Add `OracleGameProfile` (flag table, portal behavior, groups, map naming, tileset overrides) before Seasons. Pack identity check is cheap and prevents mixed state. |
| `InteractionBehaviors` | Per-feature importers validate the exact source used by each feature (Maku, Ralph, Impa, ...). | One generic parser for collision/flags/palette/warp/entry; emits a typed, manifest-backed TSV and reads `.ifdef`-filtered game flags. | Keep it; add per-feature validations only where the generic helpers cannot express a scripted state. |
| Room-entry events | `IRoomEntryEvent` + per-event databases (`RalphPortalEvent`, `EnterPastEvent`, `MakuTreeDisappearanceEvent`, ...). | `OracleScriptRuntime` binds source-cited scripts; room presentation uses the prospective edge so neighbouring actors are composed before entry; completed guards suppress one-shots. | Keep adapting event families to bindings; add the remaining Maku Tree and NPC events as their scripts are imported. |
| Seasons readiness | Shared `Features`; game-specific `Games/*`; save/inventory/runtime carry `OracleGame`. | `OracleGameProfile` holds flags/portal/groups and `--game` is generic; no Seasons maps/content yet. | Architecture ready; content/import is the remaining gate. |

### 7.1 Is `InteractionBehaviors` better than `ooa-godot`?

Yes for **generality**: it derives collision, flags, palette, portal and entry
gate from the interaction's own code instead of a hand-written table per feature.
It is weaker in three specific ways that should be fixed now:

1. It returns ad-hoc strings/records; emit a **typed, schema-validated table**
   (`interaction_behaviors`) consumed by `EntityAtlas` and future tools.
2. It now honours `.ifdef ROM_AGES/ROM_SEASONS`; the earlier Ages-only flag
   table was fixed in this block.
3. It emits facts (collision, gates, palette, warp, entry) while portal policy
   and game flags live in `OracleGameProfile`; keep that split when scripts need
   more facts.

`ooa-godot`'s `RoomEntryEffectDatabase` is the one small structure clearly better
than ours: room-wide entry effects are data rows, not per-object code. Adopt it.

### 7.2 Recommendation and order

**Refactor first, but bounded.** The current base was not ready for scripts
because state was static and there was no room-entry lifecycle; those two are
cheap now and expensive after more content. Do not rewrite the importer or clone
`ooa-godot`'s ECS.

**Status (2026-11-25): steps 1-5 are implemented and verified; the script engine
runs the first two room-entry families. The next block is enemies/parts.**

Order:

1. **State:** instance `OracleSessionState` with 0x80 global flags,
   per-group/room flag storage, room state modifier, entry direction and future
   save hooks; `OracleFlagState` is a dev-only facade. **Done.**
2. **Room lifecycle:** `OracleRoomRuntime` with `MapLoading`/`MapLoaded` and a
   small `OracleRoomEntryEffects` table (adapted from `RoomEntryEffectDatabase`).
   Ralph/past-villager events registered. **Done (first pass).**
3. **Game profile:** `OracleGameProfile` for flag tables, portal behavior, groups
   and map naming; `InteractionBehaviors` reads `.ifdef`-filtered flags. **Done.**
4. **Typed behavior table:** sorted TSV + manifest from `InteractionBehaviors`.
   **Done.**
5. **Scripts:** generated command stream + binding table, `CutsceneCommandRunner`
   with native counters/callstack, and room presentation split from activation.
   **Done for `ralphSubid0dScript` and `villagerSubid0dScript`; text/audio and
   animation variants are the remaining host gaps.**
6. **Then enemies:** `OracleEntityFactory` + emitted enemy/PART/item placements.
7. **Dungeons:** warp modes, floor layouts, large rooms; **Seasons** after the
   content pack boundaries exist.

`roomGfxChanges` can interleave; it is independent importer work.


## 8. Reference audit: `top`, `oracles-decomp`, `epoch-equinox` (2026-11-25)

This closes the four-repository map before the script/enemy/dungeon work. The
question for each repository was narrow: is there a reader, table or runtime
contract that solves one of the next four systems better than our current plan?

### 8.1 `top`

- **Scripts:** none. `engine/game/data` has no bytecode reader and
  `engine/neighbours` never runs a script; the emulated game owns cutscenes.
  There is nothing here to replace `ScriptImporter`/`CutsceneCommandRunner`.
- **Collisions/interactions:** `layout.c` decodes small and large room layouts
  (including expanded tilesets); `tileset.c` decodes metatile mappings,
  collisions and `dungeon_index`/`layout_group`; `ROM_DATA_FORMATS.md` sections
  3.3, 5.1 and 6 document the native collision, object-stream and warp formats.
  These are format references, not a reusable runtime: `top` observes the
  emulated game and does not reimplement collision or interaction logic.
- **Enemies/parts:** `engine/neighbours/objects.c` keeps records for the three
  native kinds (interactions, enemies, parts), including slot number, creation
  PC, id/subid/var03, visible/drawn bytes and animation state. `object_sprites.c`
  composes OAM frames; `object_animation.c` advances the game's own animation
  tables. Its `static` neighbour mode shows only objects whose creation state is
  deterministic and excludes random placements and objects created later by
  other objects; enemies stay frozen, interactions may animate in place.
- **Dungeons/warps:** `tileset.c` exposes a room's `dungeon_index` and
  `layout_group`; `engine/enhanced/view_map.c` reads the live `w2DungeonLayout`
  (0x40 bytes per floor) for the map view and resolves an adjacent dungeon room
  from the floor's 8x8 grid, not from the room index. It deliberately samples
  the floor that belongs to the observed position, because the game changes the
  live floor before a fall's or a staircase's fade. The warp symbols are in
  `gen_tables.py`/`oracles_tables_*.c`, but there is no `dungeonLayoutDataStart`
  or `warpDestTable` parser; those are documented in `ROM_DATA_FORMATS.md`
  sections 6-7 and still run inside the emulator.
- **Verdict:** do not port a data reader. Adopt the neighbour contract
  (prospective-edge composition, deterministic objects only, animated
  interactions, frozen enemies) as ADR-037. Use `ROM_DATA_FORMATS.md` sections
  5-7 as a second specification when importing object streams, warps and
  dungeon floors.

### 8.2 `oracles-decomp`

- **Scripts:** `src/game/scripting.c` is the authoritative check for our runner.
  It confirms `disableinput` (`$81` to `wDisabledObjects`, menu disabled),
  `jumpifmemoryset` (`mem & mask`), `jumpifglobalflagset`, `callscript`/`ret`,
  `scriptCmd_moveNpc_*` (angle + angle-derived animation + `counter2`), and the
  `interactionRunScript` order: `counter1` first, then `counter2`; while
  `counter2` is non-zero the object moves and the command stream does not
  advance. `objectMovementScript.c` confirms the `move*`/`wait` byte layout and
  the `storePointer` behavior. `bank3.objectSpeedTable` is signed 8.8 Y/X;
  `SPEED_100` (`$28`) is one pixel/frame on a cardinal axis and the angle circle
  is 0 up / 8 right / 16 down / 24 left. The native interpreter is not a generic
  bytecode table: each opcode is a hook with its own carry/yield result. Our
  `CutsceneCommandCatalog` is only the translation layer; the runner must keep
  those results, which is why the counter/call/block cases are explicit.
- **Collisions/interactions:** the common `ecom_*` helpers
  (`ecom_checkHazards`, `ecom_updateKnockbackAndCheckHazards`,
  `objectCheckCollidedWithLink_*`) and each interaction's state switch show that
  collision is part of the object's own runtime state, not a separate generic
  solver. Our tile collision plus per-object boxes are the right first pass;
  special collisions (`>= $10`, bridges/rails/side-scroll) remain gaps.
- **Enemies/parts:** 71 enemy files under `src/game/common/enemies/` and 45
  part files under `src/game/common/parts/`, each with per-object state jump
  tables and shared `ecom_*`/`partCommon` helpers. There is no ECS or generic
  behavior table to import. This reinforces ADR-035: keep DX-HD `GameObject`
  objects and add typed placement records plus per-family state hosts.
- **Dungeons/warps:** `bank1.c` owns the screen-transition states 0-5 and the
  directional scroll (`screenTransitionState2__transitionUp/Down/Left/Right`),
  plus `screenTransitionStandard`, `screenTransitionForestScrambler` (Ages'
  Lost Woods) and `screenTransitionEyePuzzle`. `bank0.c` owns
  `setWarpDestVariables`/`applyWarpDest`, and `bank1.c` has
  `initiateFallDownHoleWarp` with `wWarpDestGroup/Room/Pos`,
  `wWarpTransition`/`wWarpTransition2`.
- **Verdict:** authoritative runtime reference, not an importer. We already
  matched the script counter/call/jump semantics and the 8.8 movement contract.
  The real remaining divergence is presentation: our `MapTransitionSystem`
  still fades every source, so walk-off, edge scroll, fall-hole, Lost-Woods
  scrambling, eye-puzzle routing and stairs need explicit modes.

### 8.3 `epoch-equinox`

- **What it is:** an emulator shell and launcher, not a game-data importer. It
  has no bytecode reader, no dungeon/warp table and no object/enemy database.
- **Useful utilities for later:**
  - `epoch_rewind` snapshots the core and keeps a rewind/checkpoint ring; it
    validates the shape and lifetime of our future `OracleSessionState`
    capture/restore and save-state integration.
  - `epoch_handoff` and `epoch_secrets` drive the linked-game transfer secret
    (the `$c6xx` block, `wSecretType`, the symbol grid). This is the contract to
    reuse when secrets/save import lands; `epoch_secrets.h` documents the
    encoder split (`ES_TYPE_GAME`, `ES_TYPE_RING`, `ES_TYPE_SHORT`).
  - `epoch_splits`/`epoch_achievements` evaluate conditions against a 0x2000
    WRAM snapshot; useful as a model for future save-condition checks, not for
    gameplay tables.
  - `epoch_stream` exposes live `group/room`, essences, hearts, rings, deaths
    and play time; it confirms which live fields a future diagnostics overlay
    should read.
  - `voxel_oracle.c` reads `wActiveGroup`, `wRoomStateModifier`,
    `wRoomCollisions` and `wRoomLayout` to classify the current room; it is a
    live-field cross-check, not a source of room data. `voxel_edit.c` writes
    per-room override files for its renderer only.
  - `mod_loader` applies IPS/BPS patches; irrelevant to content import.
- **Verdict:** nothing to adopt for scripts, warps or dungeon layouts. Record
  rewind/snapshot and secret-handoff as future save/secret contracts; do not
  look here for gameplay tables.

### 8.4 Decision table

| System | `top` | `oracles-decomp` | `epoch-equinox` | Decision for Oracles-HD |
|---|---|---|---|---|
| Scripts | No reader; emulated game | Exact opcode/counter semantics in `scripting.c` | None | Keep the typed importer + runner; use decomp for each new opcode/helper |
| Collisions/interactions | Format docs + live observation | `ecom_*` inside object state machines | `voxel_oracle` reads live collision bytes | Keep tile + object boxes; add special collision modes when their objects land |
| Enemies/parts | Neighbour capture shows creation state; enemies frozen | 71 enemy + 45 part state machines, shared helpers | None | Typed placements + per-family DX-HD hosts; no ECS |
| Dungeons | `dungeon_index`/`layout_group`, live `w2DungeonLayout`, format docs | Runtime large-room scroll/transition behavior | None | Parse `dungeonDataTable`/`dungeonLayoutDataStart`; render large rooms continuously |
| Warps/transitions | `warpSourcesTable`/`warpDestTable` docs only | States 0-5, standard/forest/eye-puzzle, fall-hole, `wWarpDest*` | None | Add explicit source transition modes instead of one fade |
| Save/rewind | Not applicable | Native save/WRAM behavior | Rewind/handoff/splits/stream utilities | Reuse snapshot/secret contracts when save lands |

### 8.5 Resulting order

1. Script runner and source-cited bindings (this block). **Done for
   `ralphSubid0dScript` and `villagerSubid0dScript`.**
2. Presentation/activation split for continuous rooms (ADR-037). **Done.**
3. Import text payloads and animation variants (`setanimation`) so cutscenes
   keep their dialogue and poses; sound/music tables next.
4. Enemy/part placements and their per-family state hosts, reusing the
   neighbour rule: deterministic creation state only, no random/child pop-in.
5. Warp modes, dungeon floors and large rooms; Seasons content after the pack
   boundary.
## 9. Comparative audit against the original DX-HD engine (2026-11-25)

This addendum compares the current Oracle runtime with the neighbouring base
checkout at `..\Links-Awakening-DX-HD`. It is a read-only audit: no entity,
lighting or map behaviour is changed by this section. Its purpose is to stop
building a parallel presentation/physics layer and reuse the components that
already ship with DX-HD.

### 9.1 Method and baseline

The comparison used the actual base files, not a summary:

- `InGame/GameObjects/Base/Components/*`
- `InGame/GameObjects/Base/Systems/SystemBody.cs`
- `InGame/GameObjects/Things/ObjSprite.cs`
- `InGame/GameObjects/NPCs/ObjPersonNew.cs`, `ObjMonkey.cs`, `ObjMarin.cs`
- `InGame/GameObjects/Enemies/EnemyOctorok.cs`
- `InGame/GameObjects/ObjLink.cs`
- `InGame/Map/Map.cs`, `MapManager.cs`, `ObjectManager.cs`, `TileMap.cs`
- `InGame/GameObjects/Dungeon/ObjDungeonBlacker.cs`,
  `ObjDungeonBlackRoom.cs`, `ObjRoomDarkener.cs`
- `InGame/GameObjects/Things/ObjLight.cs`, `ObjLightSprite.cs`, `ObjLamp.cs`,
  `ObjShadowSetter.cs`, `ObjShadowDisabler.cs`, `ObjDoor.cs`,
  `ObjOverworldTeleporter.cs`, `ObjChest.cs`
- `InGame/SaveLoad/SaveLoadMap.cs`, `MapData.cs`
- `InGame/Things/GameManager.cs`

The base and Oracle `InGame/GameObjects/Base/Components` trees are
**byte-identical**: only `SystemBody.cs` has an Oracle addition (the tile-solid
hook). `Map.cs` adds Oracle collision/terrain/metadata; `MapManager.cs` adds
tile-animation updates; `ObjectManager.cs` only adds perf timers and a cast.
The component vocabulary is therefore intact and available; the divergence is
how Oracle objects and maps *use* it.

### 9.2 Verdict

| Area | Current Oracle state | Base DX-HD mechanism | Severity |
|---|---|---|---|
| Actor shadows | `oracleSprite` adds no shadow component | `BodyDrawShadowComponent`, `DrawShadowCSpriteComponent`, `DrawShadowSpriteComponent`; `Map.UseShadows`; `ObjectManager.DrawShadow` + `GameManager` blur pass | **Critical** |
| Tall-actor Y-sort | `oracleSprite` anchors `EntityPosition` at the OAM top-left; comment in code admits the depth limitation | Actors put `EntityPosition` at their feet and use `BodyDrawComponent`/negative `EntitySize`; `DrawComponent.CompareTo` sorts by `Layer` then `Position.Y` | **Critical** |
| Physics / body | Oracle actors have no `BodyComponent`; scripts write `EntityPosition` directly | `BodyComponent` + `SystemBody` gravity, velocity, field state, collision, Z, water/grass | **Critical** |
| NPC interaction | `oracleSprite` has no `InteractComponent`/`KeyChangeListener` | `ObjPersonNew`/`ObjMonkey` attach body, collision, interact, animation and shadow | High |
| Enemy combat | `EnemyAtlas.AddPlacements` emits `oracleSprite` only | `EnemyOctorok` uses Body, Ai, DamageField, Hittable, Pushable, BaseAnimation, BodyDraw and DrawShadow | **Critical** |
| Room light/atmosphere | Oracle maps never emit light objects and never set `Map.UseLight`; `OracleRoomRuntime` only dispatches music/minimap no-ops | `ObjDungeonBlacker` sets `UseLight/LightColor`; `ObjRoomDarkener`/`ObjDungeonBlackRoom` + `ObjLamp`/`ObjLight` drive the native light pass | **Critical** |
| Shadow tuning | no `shadowSetter`/`shadowDisabler` placements | `ObjShadowSetter`/`ObjShadowDisabler` mutate `Map.ShadowHeight`/`ShadowRotation`/`UseShadows` | High |
| Doors/chests/warps | `oracleWarp` and `oracleTimePortal` poll Link every Update | `ObjDoor`, `ObjOverworldTeleporter`, `ObjChest` use native `ObjectCollisionComponent`/`InteractComponent` and transition modes | High |
| Map format | native `.map` v3 + appended `#oracle-*` sections; `.map.data` not generated | native `.map` v3, `.map.data` (dig map), object templates | Low/medium |
| Room lifecycle | custom `OracleRoomRuntime` tracks stitched-map rooms and dispatch | native load = `LoadMapFile` -> `Objects.LoadObjects` -> `FinishLoadingMap`; room-sized maps | Medium (continuous map needs a room host) |
| Tile collision | `Map.CollisionMap` is consumed by `SystemBody.Collision` (good), but only actors with a body benefit | base uses collider objects; our tile hook is a native-facing extension | Medium |
| Palette/visual data | tiles are baked into the atlas at import time | native `BackgroundPaletteState`/palette offset fades; map light shader | Medium |

The current `oracleSprite` is not an NPC/enemy host. It is a **composed
animation presenter** with an optional static collision box. That is useful for
simple scenery and cutscene-only actors, but it cannot inherit native shadows,
body physics, combat, interaction or foot outlines.

### 9.3 Native mechanisms we should inherit

**Shadows.** `DrawComponent.CompareTo` orders by `Layer` and then
`Position.Y`; `ComponentDrawPoolNew` partitions and sorts the visible pool.
`BodyDrawShadowComponent` uses the body's Z and the map's
`ShadowHeight`/`ShadowRotation`; `DrawShadowCSpriteComponent` draws a silhouette
of a `CSprite`; `DrawShadowSpriteComponent` draws an explicit shadow sprite.
`GameManager.DrawGame` renders the shadow mask into a render target, blurs it
and assigns `ObjectManager.ShadowTexture`; `ObjectManager.Draw` multiplies the
shadow over the map. This is why native NPCs and enemies have contact shadows
without per-object shadow assets.

**Lighting and atmosphere.** `Map` owns `UseLight`, `LightColor`, `LightState`,
`UseShadows`, `ShadowHeight` and `ShadowRotation`.
`ObjDungeonBlacker`/`houseBlacker`/`caveBlacker` set `UseLight` and
`LightColor`. `ObjLight` and `ObjLightSprite` add a `LightDrawComponent`; `ObjLamp`
does the same and tags itself `Lamp`. `ObjRoomDarkener` counts tagged lamps in
its room and blends a black light overlay; `ObjDungeonBlackRoom` animates a
black/blur overlay and can save its state through a key. `GameManager.DrawGame`
runs `MapManager.DrawLight` and the native `LightShader` when `UseLight` is set.
`ObjShadowSetter`/`ObjShadowDisabler` tune the shadow pass. `ObjFog`,
`ObjBackgroundColor`, `ObjTransition` and `ObjColorShift` cover other native
screen/atmosphere effects.

**Bodies and interaction.** `ObjPersonNew` adds `BodyComponent`,
`BodyCollisionComponent`, `InteractComponent`, `AnimationComponent`,
`BodyDrawComponent` and `BodyDrawShadowComponent`. `EnemyOctorok` adds
`BodyComponent`, `AiComponent`, `DamageFieldComponent`, `HittableComponent`,
`PushableComponent`, `BaseAnimationComponent`, `BodyDrawComponent` and
`DrawShadowComponent`, plus `AiDamageState`/`AiFallState`. `ObjDoor` uses
`ObjectCollisionComponent`; `ObjChest` uses `InteractComponent` or
`HittableComponent`; `ObjOverworldTeleporter` uses `ObjectCollisionComponent`
plus a hole collision.

**Map data.** The base `.map` is version 3: offset, tile map, object-type table,
object rows. The optional `.map.data` is the **dig map**, not lighting or room
metadata. Native object templates are already registered in our
`GameObjectTemplates.cs` (`door`, `ddoor`, `chest`, `item`, `teleporter`,
`overworldTeleporter`, `light`, `spriteLight`, `roomDarkener`,
`dungeonBlackRoom`, `dungeonBlacker`, `shadowSetter`, `shadowDisabler`, ...).
Our importer never emits them.

### 9.4 Why Oracle objects do not inherit this today

1. **No body, therefore no native shadow/physics/field state.** `ObjOracleSprite`
   attaches only `DrawComponent`, optional `UpdateComponent` and an optional
   `BoxCollisionComponent`. `BodyDrawShadowComponent` requires a `BodyComponent`;
   `BodyDrawComponent` requires a body; `SystemBody` only moves bodies.
2. **Wrong anchor.** `ObjOracleSprite` does
   `EntityPosition = pos + OAM offset`, `EntitySize = (0,0,w,h)`. The draw pool
   therefore sorts the object by the top of its composed OAM, not by its feet.
   Tall NPCs overlap the player incorrectly. The code comment explicitly calls
   this a limitation.
3. **All Oracle actors share `Values.LayerPlayer`.** The importer always emits
   layer `2`; it does not parse the interaction OAM priority/foreground bits.
   Ground/overhead props cannot sort into `LayerBottom`/`LayerTop`.
4. **Enemies are presentation only.** `EnemyAtlas.AddPlacements` emits
   `oracleSprite` rows: no `AiComponent`, `HittableComponent`, `DamageField`,
   `Pushable`, or `BodyComponent`. The Octorok can animate but cannot move, be
   hit, push or leave a shadow.
5. **Room light never turns on.** Oracle maps contain no `dungeonBlacker`,
   `roomDarkener`, `light` or `shadowSetter` objects. `Map.UseLight` remains
   false and no `LightDrawComponent` exists. `PART_DARK_ROOM_HANDLER $08` and
   the dungeon `DARK_BIT` are imported as audit data at most, not mapped to
   native light components. `roomDarkener` already implements the torch-count
   blend; we are not using it.
6. **Warps/portals use custom polling.** `ObjOracleWarp.Update` and
   `ObjOracleTimePortal.Update` inspect Link's feet/facing every frame. Native
   `ObjDoor`/`ObjOverworldTeleporter` receive `ObjectCollisionComponent` events
   and have source transition modes. The custom code duplicates trigger
   semantics and does not participate in native collision filters.
7. **Room effects are a switch, not objects.** `OracleRoomRuntime` dispatches
   `roomSpecificCode` handlers through `OracleRoomEntryEffects`; most are music
   or minimap no-ops. A room cannot set `UseLight`, spawn a light, or tune
   shadows because the effect model has no such owner.
8. **Tile changes are a parallel load pass.** `OracleTileChangeCatalog` writes
   `TileMap.ArrayTileMap` and collision/terrain from a side table. It is
   source-cited and useful, but it is a second room-load path. The native
   alternative is a map variant or a tiny map object that performs the write;
   in a continuous map the catalog is acceptable only as an explicit bridge.

### 9.5 Re-alignment plan (bounded, in order)

**P0.1 - Actor host: body, anchor, shadow, animation.**

- Refactor `ObjOracleSprite` (or introduce `ObjOracleActor`) so the anchor is
  the **feet**: `EntityPosition` at the native interaction anchor, negative
  `EntitySize` for culling, `CSprite` positioned at the anchor with per-frame
  draw offset.
- Add `BodyComponent` (or a minimal body for stationary NPCs),
  `BodyDrawComponent`, `BodyDrawShadowComponent` and
  `AnimationComponent`/`BaseAnimationComponent`. Keep the imported frame
  timeline; drive `CSprite` through the native animation component rather than
  a private frame counter.
- Add `BodyCollisionComponent`/`InteractComponent` for talkable NPCs and
  `KeyChangeListenerComponent` for save-key-driven variants. Keep
  `OracleVisibilityEvaluator` as a spawn gate only.
- Move script `applySpeed`/jump/Z writes from `EntityPosition` to
  `Body.VelocityTarget` / `Body.Velocity.Z`; let `SystemBody` apply gravity,
  collision and field state. `SetActorZ` becomes body Z, not a negative sprite
  offset.
- Acceptance: Maku audience/bear/rabbit and Ralph show contact shadows; a tall
  NPC correctly overlaps Link; no culling regressions; scripts still complete.

**P0.2 - Native room atmosphere and lights.**

- Import Oracle darkness/light data into map objects:
  - `PART_DARK_ROOM_HANDLER $08` and `wNumTorchesLit` -> `roomDarkener` plus
    `ObjLamp`-tagged torches, or `dungeonBlackRoom` for the one-shot reveal.
  - dungeon `DUNGEONROOMPROPERTY_DARK_BIT` / room dark flag -> `dungeonBlacker`
    or `caveBlacker` with source color/alpha.
  - lightable torches -> native `ObjLight`/`ObjLightSprite`/`ObjLamp` with
    `LightDrawComponent` and `Tags = Lamp`.
  - source shadow overrides -> `shadowSetter`/`shadowDisabler`.
- Emit these as normal map objects through `DxMapWriter`; do not add a second
  room-light runtime. `Map.UseLight`, `LightColor` and `LightState` then flow
  through the existing `GameManager` shader.
- Acceptance: a known dark room (e.g. `5:$a8` / `5:$ed` from the imported dark
  room table) darkens; lighting torches raises the native lamp count; the light
  pass visibly changes; Maku daytime remains unchanged.

**P1.1 - Enemy hosts.**

- Replace enemy `oracleSprite` rows with per-family native hosts. First:
  `ObjOracleOctorok` using `BodyComponent`, `AiComponent`, `AiDamageState`,
  `HittableComponent`, `DamageFieldComponent`, `PushableComponent`,
  `BaseAnimationComponent`, `BodyDrawComponent` and `DrawShadowComponent`.
- Keep `EnemyAtlas` as the frame source; the host owns state/AI/collision.
- Do not reuse `EnemyOctorok` AI blindly: it is LA behavior/animations. Reuse
  its component wiring and port Oracle's source state machine.
- Acceptance: Octorok walks, shoots, takes damage/knockback, uses tile
  collision, and casts a shadow.

**P1.2 - Doors, chests, teleporters.**

- Map Oracle warp transition bytes to the existing `ObjDoor` modes (`0..7`)
  and use `ObjectCollisionComponent`; use `overworldTeleporter` where the
  native template matches.
- Map Oracle item/chest placements to `chest`/`item` native templates instead
  of leaving `RoomItem` as a count.
- Keep a thin `ObjOracle*` adapter only where the source behavior is truly
  Oracle-specific; the adapter must attach native components, not poll the
  player itself.

**P2.1 - Map/room ownership.**

- Keep native `.map` v3 and `.map.data`; keep `#oracle-map v1` only for
  identity (game/group/era/kind/grid/room/floor/dungeon). Move behaviour out of
  `#oracle-room-effects` and the custom effect switch into native objects.
- Keep `OracleRoomRuntime` as the continuous-map room host (base maps are
  room-sized; our stitched overworld still needs a room identity/session).
  Narrow it to: resolve room, publish entry, dispatch scripts/visibility, and
  apply explicit bridge data. It must not own sprite presentation, physics,
  light or shadows.
- Keep `Map.CollisionMap` only because `SystemBody` consumes it; it is a native
  map extension, not a second collider layer. Every new Oracle actor must have a
  body so it inherits this path.
- `singleTileChanges`: prefer baked per-state map variants for persistent room
  flags; if a runtime write is unavoidable in the continuous map, make it a
  small map object/room-load owner and retire the global catalog once variants
  exist.
- Do not replace DX-HD `GameObject`/`ObjectManager` with an ECS.

**P2.2 - Verification matrix.**

Add before/after captures for: Maku present, Maku past, a cave/dark room with
torches, a dungeon floor, Octorok, a tall NPC overlapping Link, a portal, and a
door transition. Measure shadow/light passes with `ORACLE_PERF_TRACE=1`; do not
accept a visual gain that adds an unmeasured full-screen pass per room.

### 9.6 What not to do

- Do not add another sprite/entity abstraction beside `GameObject`.
- Do not add a second light/shadow shader or a custom room-colour table when
  `LightShader`/`LightDrawComponent`/`Map.UseLight` already exist.
- Do not keep `oracleSprite` as the enemy/chest/door host once a native host
  exists.
- Do not encode room IDs/coordinates/colours in runtime branches.
- Do not port LA enemy AI as if it were Oracle AI; port the component wiring and
  the Oracle source state machine.
- Do not remove the continuous map to copy base's room-sized map lifecycle;
  keep the room host, but make it thin.

### 9.7 Audit status

This section is a **NO-GO gate** for new enemy/dungeon features until P0.1 and
P0.2 are green. The current Maku palette/tile-change fixes remain valid; they
are data fixes, not a substitute for native actor/room integration.
