# Architecture audit - Phase 3/4 slice

Snapshot: **2026-11-25**, implementation `1414941`. This audit covers the
present slice, the past overworld, time portals and the plumbing that will be
reused by scripts, enemies, dungeons and Seasons. It records structural debt,
not new features.

## 1. Verdict

| Next block | Verdict | Why |
|---|---|---|
| Bytecode scripts / cutscenes | **GO after a bounded state/room refactor** | The importer can emit command tables, but `OracleFlagState` is static and there is no room-entry event hook. Both are cheap to add now and block every scripted event. |
| Enemies / combat | **NO-GO yet** | Object streams only emit interactions; enemy/PART/item placements, handler identity and a runtime construction seam are missing. |
| Dungeons | **NO-GO yet** | Metadata exists, but stairs/fall/source-transition warp modes, large rooms, floor layouts, per-room flags and camera locks remain. |
| Seasons | **NO-GO yet** | `OracleFlagState`, `InteractionBehaviors` flag parsing and the development entry still assume Ages values/paths. Needs a game profile, not more branches. |
| `roomGfxChanges` | **GO** | It is importer/render work; it does not require a new runtime abstraction and does not affect Maku `$38`, whose tiles and `$30` palette match the reference. |

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
| Room content | `IRoomContent.LoadRoom(group, room, save, runtime, tick, toggle)` + `RoomSession` own the current room, animation tick, visits, minimap, palette and room state. | One `.map` per room (or a stitched overworld) + `OracleMapInfo`; `MapManager` owns the current map; no room session. | Add a small `OracleRoomRuntime` around `MapManager` (current group/room, entry direction, room load/entry events). Do not copy the full `RoomSession`. |
| Entity lifecycle | `IRoomEntityFactory` constructs content; `RoomEntityManager` executes ordered object streams, slots, freezes, outgoing sets and capability interfaces. | `ObjectManager.ObjectList` + DX-HD `GameObject` constructors; `oracleSprite`/`oracleWarp`/`oracleTimePortal` read flat `.map` parameters. | Add an `OracleEntityFactory`/registry (id/subid -> constructor) and typed placement records before enemy AI. Use DX-HD pools, `SystemBody`, `SystemAi`; do not import the ECS. |
| Scripts | `CutsceneCommandCatalog` loads schema-validated command streams; `CutsceneCommandRunner` + host interfaces execute them; event databases bind actors/flags/text. | None. | Adapt the command-table + runner shape. Start with one room-entry event family to prove the host contract, then generalise. |
| State / save | `OracleSaveData` is the native `$550` image (0x80 global flags, per-room flag tables, visited/minimap, inventory); `OracleRuntimeState` mirrors live WRAM fields. | `OracleFlagState` is static: 256 booleans, **one** `RoomFlags` byte, no save, no runtime fields. | **Blocking refactor.** Replace with an instance `OracleSessionState` (global flags, per-group/room flags, room state, entry direction, save hooks). Keep a thin dev facade only. |
| Room-entry effects | `RoomEntryEffectDatabase` is a 7-row data table (group, room, handler, source) plus `IRoomEntryEvent` cutscene hosts; e.g. Ralph portal music/departure. | Per-object source-derived entry gate; no room-wide effects or entry-event registry. | Adapt the small table + event registry. The per-object gate remains for actor visibility; room-wide effects get the table. |
| Transitions | `RoomTransitionController`/`ScreenTransitionRenderer` with room packs, palette handoff, scroll modes. | `MapTransitionSystem` + warps/portals; warp mode is a fade only. | Keep DX-HD transitions; add `sourceTransition`/mode (walk-off, instant, fall, stairs) and floor metadata. |
| Game packs | `Games/Ages` and `Games/Seasons` content + enums; `Features` shared; pack identity validated (save/runtime/content must match). | Importer understands `--game`, but runtime/tool constants are Ages: `OracleFlagState` flags, portal behavior, `--room` `oracle_ages_g2`, Ages tileset-override parser. | Add `OracleGameProfile` (flag table, portal behavior, groups, map naming, tileset overrides) before Seasons. Pack identity check is cheap and prevents mixed state. |
| `InteractionBehaviors` | Per-feature importers validate the exact source used by each feature (Maku, Ralph, Impa, ...). | One generic parser for collision/flags/palette/warp/entry. | More general and cleaner than per-feature regexes, but it must emit a typed table and read game-specific flag tables; otherwise it silently uses Ages values for Seasons. |
| Room-entry events | `IRoomEntryEvent` + per-event databases (`RalphPortalEvent`, `EnterPastEvent`, `MakuTreeDisappearanceEvent`, ...). | None; one-shot actors are hidden by source entry gate, scripts not run. | Highest-value next adaptation: one small event registry and one event to validate. |
| Seasons readiness | Shared `Features`; game-specific `Games/*`; save/inventory/runtime carry `OracleGame`. | Ages-shaped flags/paths and one Ages-only portal object. | Not ready; fix with `OracleGameProfile` + pack identity now, content later. |

### 7.1 Is `InteractionBehaviors` better than `ooa-godot`?

Yes for **generality**: it derives collision, flags, palette, portal and entry
gate from the interaction's own code instead of a hand-written table per feature.
It is weaker in three specific ways that should be fixed now:

1. It returns ad-hoc strings/records; emit a **typed, schema-validated table**
   (`interaction_behaviors`) consumed by `EntityAtlas` and future tools.
2. It reads `constants/common/globalFlags.s` without honouring
   `.ifdef ROM_AGES/ROM_SEASONS`; Seasons would receive Ages flag numbers.
3. It mixes source facts with policy (portal activation, entry direction). Keep
   the facts in the table and the policy in game/runtime owners.

`ooa-godot`'s `RoomEntryEffectDatabase` is the one small structure clearly better
than ours: room-wide entry effects are data rows, not per-object code. Adopt it.

### 7.2 Recommendation and order

**Refactor first, but bounded.** The current base is not ready for scripts
because state is static and there is no room-entry lifecycle; those two are
cheap now and expensive after more content. Do not rewrite the importer or clone
`ooa-godot`'s ECS.

Order:

1. **State (blocking):** instance `OracleSessionState` with 0x80 global flags,
   per-group/room flag storage, room state modifier, entry direction and future
   save hooks; `OracleFlagState` becomes a dev-only facade.
2. **Room lifecycle:** `OracleRoomRuntime` with `OnRoomLoaded`/`OnRoomEntered`
   and a small `OracleRoomEntryEffects` table (adapt `RoomEntryEffectDatabase`).
   Use it first for the Ralph/past-villager events.
3. **Game profile:** `OracleGameProfile` for flag tables, portal behavior, groups
   and map naming; make `InteractionBehaviors` read the selected game's flags.
4. **Typed behavior table:** move `InteractionBehaviors` output into the
   generated table layer with a schema and manifest hash.
5. **Then scripts:** generated command streams + a `CutsceneCommandRunner`-style
   host; start with room-entry events and one script family.
6. **Then enemies:** `OracleEntityFactory` + emitted enemy/PART/item placements.
7. **Dungeons:** warp modes, floor layouts, large rooms; **Seasons** after the
   game profile and content pack boundaries exist.

The immediate next implementation block can be (1)+(2)+(3) in 2-3 commits, then
scripts. `roomGfxChanges` can interleave; it is independent importer work.

