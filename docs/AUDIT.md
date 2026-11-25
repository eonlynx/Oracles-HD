# Architecture audit - Phase 3/4 slice

Snapshot: **2026-11-25**, implementation `0af0f0b` plus the cleanup working
tree. This is a preventive audit before `roomGfxChanges`, the past and
dungeons. It records abstraction debt, not new features.

## 1. Verdict

| Next block | Verdict | Why |
|---|---|---|
| `roomGfxChanges` | **GO** | It is importer/render work; it does not require a new runtime abstraction. It will replace the temporary Maku `$8f` branch. |
| Present -> past transitions | **NO-GO yet** | The runtime has no map/era/room metadata. Warps are file-name strings; room logic cannot identify era or room from a world position. |
| Dungeons | **NO-GO yet** | Same metadata gap, plus stairs/fall/source-transition warp modes, large rooms and camera locking. |

The present slice is coherent enough to keep; it is not yet coherent enough to
scale to enemies, era changes or floors without a small refactor.

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
  shared entity atlas. It is a presentation object only: it has no semantic
  interaction id/subid, collision, shadow, facing or script animation. That is
  acceptable for the first visual pass, but enemies/NPC scripts need identity.
- **Continuous overworld**: the stitched map has no room boundaries in data.
  Deriving `room = (worldX/160, worldY/128)` is possible, but the era/group is
  absent. Enemies, room music, scripts, camera locks and respawn will each need a
  different ad-hoc string or coordinate rule unless a metadata header exists.
- **Collisions**: `SystemBody` applies `Map.IsSolid` to every body with
  `Normal`/`NPCWall`, so enemies will inherit tile collisions. Special
  collisions (`>=0x10`) are still treated free; water/hole/lava are conservative.
  This is a known approximation, not a runtime conversion leak.
- **Camera**: small interior maps are exactly 160x128; the follow camera shows
  black around them. Dungeons and large rooms need a locked/clamped camera using
  the existing `Map.CameraTarget`.

## 4. Future conflicts

### Enemies

1. `oracleSprite` has no interaction id/subid, so an enemy cannot attach AI,
   damage, drops or scripts to it.
2. `RoomObjects` is a text parser that treats conditions as a flag and does not
   evaluate `obj_BeforeEvent`/`obj_AfterEvent` or the native conditional skip.
   It is enough for static interactions, not for enemy placement.
3. Standard sprite palettes 0-5 only. The post-game Link statue `$6b:$15` is
   now the first object-level exception: it carries `PALH_c7` sprite palette 6
   and is gated by `GLOBALFLAG_FINISHEDGAME`. General room sprite-palette
   headers are still pending.
4. `RoomObjectData` counts 82 overworld enemies and 3 interior enemies; none are
   placed or ticked yet.

### Present <-> past

1. The two overworlds are separate map files, but the runtime has no era field;
   saves/respawn/music/palette changes cannot choose consistently.
2. Warp targets are map-name strings. A map registry or metadata header is
   needed for era changes, dungeon entrances and save restoration.
3. `PALH_8f` is a room-specific branch in `OracleContent`; past and dungeon room
   graphics will multiply these branches until `roomGfxChanges` exists.

### Dungeons

1. Floors need `dungeonIndex`, `floor`, room size (10x8 or 15x11) and a camera
   mode; none are in the map.
2. `WarpDatabase` text parsing does not implement the Ages dungeon-stair fallback
   or `$9` fall source transitions.
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
5. **Sprite palette context**: importer applies room `m_PaletteHeaderSpr` and
   object extra graphics. This is also the right place to finish the Maku
   sign/flower and post-game statue presentation.
6. **Camera policy**: lock small maps to their centre or clamp to bounds; add a
   dungeon camera mode. Use `Map.CameraTarget`.

`roomGfxChanges` can start now; it will naturally cover item 5 for static room
graphics. Items 1-4 should land before the first past/dungeon playable slice.
Legacy LA HUD/save/audio do not block map traversal, but they block progression
and must be replaced before a full Ages run.
