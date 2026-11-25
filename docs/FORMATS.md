# Data formats

This document describes the exact mapping from `oracles-disasm` sources to the
data the DX-HD engine reads. Evidence paths are relative to the pinned
submodule `external/oracles-disasm` (`21c924af`).

## 1. Disassembly tables consumed

| Source | Used for |
|---|---|
| `data/{game}/tilesets.s` | 8 bytes per tileset: collisions/dungeon, flags, unique gfx, gfx, palette, layout, layout group, animation. |
| `data/{game}/gfxHeaders.s` | Main tileset graphics entries (`m_GfxHeader file, $dest`). |
| `data/{game}/uniqueGfxHeaders.s` | Per-tileset unique graphics, loaded after the main header. |
| `data/{game}/paletteHeaders.s` | Palette slots 2-7 per tileset. |
| `data/{game}/paletteData.s` | RGB555 colours (`m_RGB16 r g b`). |
| `data/{game}/tilesetHeaders.s` | `tilesetLayoutGroupXX` -> mapping/collision labels (aliases). |
| `tileset_layouts/{game}/tilesetMappingsXX.bin` | 2048 bytes: 256 metatiles x (4 indices + 4 attributes). |
| `tileset_layouts/{game}/tilesetCollisionsXX.bin` | 256 collision bytes. |
| `rooms/{game}/groupNTilesets.bin` | Room -> raw tileset byte (bit 7 is a flag). |
| `rooms/{game}/small/roomXXYY.bin` | 10x8 layout (`XX` = layout group). |
| `rooms/{game}/large/roomXXYY.bin` | 176-byte layout: 11 rows x 16, 15x11 playable. |
| `rooms/{game}/roomPacks{Present,Past}.bin` | Companion-region detection (`$7f`). |
| `data/{game}/animationGroups.s` | Animation group: initial state + track pointers. |
| `data/{game}/animationData.s` | `(delay, gfxIndex)` frames + loop marker. |
| `data/{game}/animationGfxHeaders.s` | 6-byte DMA entries: sheet, source, destination, size. |
| `data/{game}/tile_properties/hazards.s` | Water/hole/lava per collision mode. |
| `data/{game}/tile_properties/tileTypeMappings.s` | Link tile types (grass, water, sea, hole, lava, ...). |
| `constants/common/tileTypes.s` | `TILETYPE_*` values. |

Not yet consumed: `objects/`, `data/*/dungeonLayouts.s`, `data/*/text`,
`audio/`, `data/*/roomGfxChanges`, seasonal tilesets.

## 2. Metatile decode

A mapping row is 8 bytes:

```
[tile TL][tile TR][tile BL][tile BR][attr TL][attr TR][attr BL][attr BR]
```

Attributes:

| Bits | Meaning |
|---|---|
| 0-2 | BG palette 0-7 |
| 3 | VRAM bank (0/1) |
| 5 | flip X |
| 6 | flip Y |
| 7 | BG priority (ignored for the room layer) |

Tile slot:

```
bank == 1: slot = (tile + 0x80) & 0xff
bank == 0: slot = tile & 0x7f
```

The disassembly PNGs are indexed, bit depth 2, palette `[255,170,85,0]`.
**PNG index 0 is white and equals CGB shade 0; do not invert.**

## 3. Tileset selection

`loadTilesetData_body`:

1. `roomTilesetsGroupTable[adjustedGroup][room]` -> byte.
2. Bit 7 is OR'd into `wTilesetUniqueGfx` (a flag). The tileset index is
   `byte & 0x7f`.
3. `tilesetData[index * 8]`.

Overrides (`checkTilesetOverride`):

- Maku Tree saved: group 0, room `$38` -> tileset + 2.
- Jabu flooded: tileset `$3e` -> +1.
- Companion region (`roomPacks[room] == $7f`): the layout group changes;
  Ricky keeps it, Dimitri uses 1, Moosh/other uses 3.

Layout aliases are resolved through `tilesetLayoutGroupXX`: layout `$20` uses
`tilesetMappings1b`, not a non-existent `tilesetMappings20`.

## 4. Palettes

- Tileset palette header loads slots **2-7**.
- BG palette **0** is the engine common palette `paletteData48e0`
  (see `ooa-godot` `BackgroundPaletteState`). Slot 1 is the textbox palette.
- Past cliffs: when the tileset has `TILESETFLAG_PAST`, collision mode 0 and
  the room is not `$38`, every attribute palette 6 in metatiles `$40..$7f`
  becomes 0 (`setPastCliffPalettesToRed`).
- Maku Tree scene: rooms use their tileset palette (`$30` for `0:$38`, including
  the pink flowers). `makuTree.s` loads `PALH_8f` only for the disappearance
  cutscene; it must not be applied statically.

## 5. `.map` format

Native DX-HD part (unchanged):

```
3
<MapOffsetX>
<MapOffsetY>
<tileset.png>
<width>
<height>
<depth>
<depth x height rows of comma-separated tile indices>
<template count>
<template names...>
<object count>
<object lines...>
```

Oracle sections are appended after the native object list. The DX-HD loader
ignores trailing lines; Oracles-HD parses them in `SaveLoadMap.LoadOracleSections`.

### 5.0 Map metadata

```
#oracle-map v1
game=ages
group=0
era=present|past
kind=overworld|interior|dungeon|room
roomWidth=10
roomHeight=8
gridWidth=14
gridHeight=14
room=38
floor=-1
dungeon=-1
camera=follow|fixed
```

`room=-1` for a stitched overworld. `camera=follow` is the default everywhere,
including houses/caves of 160x128 and joined dungeon floors, matching DX-HD.
`camera=fixed` is reserved for an explicit puzzle/gameplay override; it is not
derived from map size. `Map.CameraTarget` is the existing DX-HD mechanism for
that explicit case. The importer is the only writer of this header; the runtime
only reads the already-converted values.

### 5.0.1 Room-entry effects

```
#oracle-room-effects v1
<count>
<group>;<room>;<handler> x count
```

Rows come from `roomSpecificCode.s` group tables. `handler` is the native jump
index (0x04/0x05/0x08/0x09/0x0a/0x0b/0x0c are the supported room-wide effects;
others are explicit no-ops until their owners land). `OracleRoomRuntime` applies
the rows for the current `(group, room)` on `MapLoaded`; code-hosted events
(e.g. Ralph/EnterPast) are registered separately.

### 5.1 Collision

```
#oracle-collision v1
<width>
<height>
<hex row widthx2 chars> x height
```

Byte meaning:

- `0x00` free.
- `0x01..0x0f` solid quadrants: bit 3 TL, 2 TR, 1 BL, 0 BR.
- `0xff` generated full-solid tile (water, hole, lava, map border).
- `0x10..0xfe` special collision (treated as free today).

### 5.2 Terrain

```
#oracle-terrain v1
<width>
<height>
<hex row> x height
```

Terrain byte:

| Value | Engine `FieldStates` |
|---|---|
| 1 | Water |
| 2 | DeepWater |
| 3 | Lava |
| 4 | Grass |
| 5 | Hole (no field flag; collision is full-solid) |

Source: `hazards.s` and `tileTypeMappings.s`, selected by the tileset's
collision mode (`tileset[0] >> 4`; 0 overworld, 1 indoors, 2 dungeons, 3
sidescrolling, 4 underwater, 5 five).

### 5.3 Tile animation timeline

```
#oracle-anim v1
<count>
<frames csv>;<durations ms csv>;<tileX>;<tileY>
```

`frames` are atlas metatile indices (base atlas plus baked variants).
`durations` are milliseconds. `tileX/tileY` are map tile coordinates. The
runtime `Map.UpdateTileAnimations` swaps `ArrayTileMap[x, y, 0]`; collision and
terrain do not change.

Animation source order (`dumpAnimationData.py`):

```
counter = rom[address]      # delay
gfxIndex = rom[address + 1] # header index
.db counter gfxIndex
```

So each pair is **`(delay, gfxIndex)`**; a delay of `$ff` is the loop marker
followed by a signed offset. This is pinned by the `ooa-godot` regression that
expects waterfall headers `0, 24, 32`.

Animation header (`m_GfxHeaderAnim`):

| Field | Meaning |
|---|---|
| sheet | `gfx_animations_1..3` |
| destination | VRAM address; low nibble bank; tile = `(addr-0x8800)/16` (or 128 + `(addr-0x9000)/16`) |
| size | tile count - 1 |
| source offset | byte offset in the sheet; tile = offset / 16 |

The composer:
1. finds the tracks whose frames cover a metatile slot;
2. advances all relevant tracks segment by segment (minimum remaining delay);
3. renders each distinct metatile variant;
4. merges identical consecutive frames; rejects static metatiles;
5. emits atlas variants plus the timeline above.

The three initial forced writes of `initializeAnimations` are ignored in the
steady-state timeline; they only affect the first three updates.

### 5.4 Native objects emitted by the importer

Oracle entities and warps reuse the native DX-HD object list; there is **no**
`#oracle-objects` section.

`oracleSprite` fields, after the native `posX;posY`:

```
frames        comma-separated atlas entry ids
durations     comma-separated milliseconds per frame
offsetX       OAM bounding-box X relative to the interaction anchor
offsetY       OAM bounding-box Y relative to the interaction anchor
layer         DX-HD layer (2 = player)
loopStart     frame index used after the last frame (native m_AnimationLoop)
interactionId native interaction id
subId         native subid (semantic identity)
requiresFlag  global flag that must be set, or -1
forbiddenFlag global flag that must be clear, or -1
roomStateMask native obj_Condition mask (skip if this bit is set in wRoomStateModifier)
roomFlagMask  native event gate mask (bit 7 for obj_BeforeEvent/obj_AfterEvent)
roomFlagValue required room-flag value
collisionRadius blocking radius around the interaction anchor (0 = none)
```

`oracleWarp` fields, after `posX;posY`:

```
nextMap    map file name (for example oracle_ages_g2_re7.map)
spawnX     spawn pixel X in the destination map space
spawnY     spawn pixel Y
direction  DX-HD direction (0 left, 1 up, 2 right, 3 down)
width      trigger width
height     trigger height
```

Warp resolution follows `ooa-godot` `WarpDatabase` / `Import-Warps.ps1`:

- `m_StandardWarp`: edge mask, room, destination index, destination group,
  source transition.
- `m_PointerWarp` expands the target label's `m_PositionWarp` rows; the YX
  position selects the exact tile.
- `warpTiles.s` marks the metatiles that trigger a warp. Stacked `@overworld:`
  and `@underwater:` labels share the same rows.
- Destination `YX` is `(y << 4) | x`; spawn = `x*16+8, y*16+8`. Transition `$3`
  with parameter `$9` enters from the bottom walking up; `$f` enters from the
  top walking down.
- Group-0/1 destinations add the stitched room offset (`roomX*160`,
  `roomY*128`); group-2/3 interiors are standalone and stay local.

The runtime checks both the tile Link stands on and the tile he faces. The
second check is needed because DX-HD's 8x10 body collision is stricter than the
GB tile check: a door tile is often only half-walkable.

### 5.5 Oracle flags and spawn conditions

`Tools/OracleAssetBuilder/InteractionRules.cs` carries the interaction-level
conditions that live in object code rather than in the room stream. The first
case is `$6b:$15` (post-game Link statue): `requiresFlag=$14`
(`GLOBALFLAG_FINISHEDGAME`) and sprite palette header `$c7` (palette 6 from
`paletteData44f0`).

`obj_Condition` masks and `obj_BeforeEvent`/`obj_AfterEvent` pointers are also
carried on `oracleSprite`:

- `roomStateMask`: hide the object when
  `(mask & (1 << OracleFlagState.RoomStateModifier)) != 0`.
- `roomFlagMask=0x80`, `roomFlagValue=0`: `obj_BeforeEvent` branch.
- `roomFlagMask=0x80`, `roomFlagValue=0x80`: `obj_AfterEvent` branch.

The runtime evaluates these in `OracleSessionState.IsVisible` (through the
`OracleFlagState` dev facade). The dev entry can set flags with `--flag
FINISHEDGAME`, `--room-state N` and `--room-flags N`; `--room-flags` applies to
the next entered room only.

`solidTile`, `radiusY` and `radiusX` are source-derived per interaction:
`objectMarkSolidPosition`/`wRoomCollisions` mark a 16x16 tile box;
`objectSetCollideRadius/Radii` set explicit Y/X radii; otherwise there is no
collision. The box is centred on the interaction anchor and clamped to
`EntitySize`.

## 6. Atlas and manifest

- Atlas PNG: one 16x16 metatile per cell, 64 columns for generated overworlds.
  Base metatiles are appended first; animation variants use a separate key
  namespace (`0x4000_0000_0000 | tileset<<32 | metatile<<16 | variant`).
- Manifest (`*.manifest.json`): tool version, game/group/room, tileset metadata,
  room IDs, skipped IDs, input SHA-256s (relative to the disasm root) and output
  SHA-256s. Manifests are committed; maps/atlases/previews are gitignored.

### 6.1 Entity atlas

`Tools/OracleAssetBuilder` writes one shared pair:

```
Data/Map Objects/oracle_<game>_entities.png
Data/Map Objects/oracle_<game>_entities.atlas
```

- Entry id = `oracle_<game>_i<interaction:x2>_<subid:x2>_<frame:00>`.
- Each entry is the composed OAM frame, not a raw sheet cell. OAM blocks are
  drawn in reverse order (lower index wins), with X/Y flips, 8x16 cells,
  `tileBase + tile`, and palette `oamFlags XOR (flags & 7)`.
- The interaction anchor is the centre of a 16x16 object cell
  (`ooa-godot` `SpriteBounds`). The atlas offset is
  `rawOamMinX - 8, rawOamMinY - 16`; the map object stores that offset.
- The same plan (positioned interactions for every configured overworld group
  plus the interiors reachable from a group-0 warp) is used by both `overworld`
  and `interiors`, so either command produces the same atlas.

### 6.2 Interaction behaviour table

`Data/Map Objects/oracle_<game>_interaction_behaviors.tsv` is a sorted,
schema-stable projection of `InteractionBehaviors`:

```
id  subid  solid  radius_y  radius_x  requires_flag  forbidden_flag
palette_header  time_warp  entry_direction
```

`oracle_<game>_interaction_behaviors.manifest.json` records the parsed source
files (SHA-256) and the table hash. The TSV is generated/gitignored; the manifest
is committed. The runtime still consumes the per-object `.map` fields, so this
table is the audit/tooling contract for scripts/enemies.
- Entity atlases are gitignored; their SHA-256 is in the manifest.

## 7. Room inclusion in the overworld grid

Grid position is the native room ID: `x = id & 0x0f`, `y = id >> 4`. Ages uses
14x14, Seasons 16x16. A room is included when its tileset has
`TILESETFLAG_OUTDOORS ($01)` **or** `TILESETFLAG_MAKU ($02)`. The Maku Tree
room `$38` is MAKU-only but is part of the overworld map
(`top`/`ooa-godot` map MAKU to room `$38`).

## 8. Pending formats

- Room object stream (`objects/{game}/pointers.s`, `mainData.s`): positioned
  interactions are read; enemies, parts, item drops and static dungeon objects
  still need placement and behaviour.
- `roomGfxChanges` and `checkLoadPastSignAndChestGfx`; these supply the Maku
  canopy/lower band and other room-specific graphics that currently render
  black or placeholder.
- `dungeonLayouts.s` and large-room size selection.
- Seasonal tilesets (`m_SeasonalTileset`, `wRoomStateModifier`) and four
  layouts per room.
- Text (`text/{game}/*.yaml`), audio (`audio/`, `code/audio.s`) and save SRAM.
