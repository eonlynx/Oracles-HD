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
  the room is not `$38`, every attribute palette 6 in metatiles `$40..$ff`
  becomes 0 (`setPastCliffPalettesToRed`).

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

## 6. Atlas and manifest

- Atlas PNG: one 16x16 metatile per cell, 64 columns for generated overworlds.
  Base metatiles are appended first; animation variants use a separate key
  namespace (`0x4000_0000_0000 | tileset<<32 | metatile<<16 | variant`).
- Manifest (`*.manifest.json`): tool version, game/group/room, tileset metadata,
  room IDs, skipped IDs, input SHA-256s (relative to the disasm root) and output
  SHA-256s. Manifests are committed; maps/atlases/previews are gitignored.

## 7. Room inclusion in the overworld grid

Grid position is the native room ID: `x = id & 0x0f`, `y = id >> 4`. Ages uses
14x14, Seasons 16x16. A room is included when its tileset has
`TILESETFLAG_OUTDOORS ($01)` **or** `TILESETFLAG_MAKU ($02)`. The Maku Tree
room `$38` is MAKU-only but is part of the overworld map
(`top`/`ooa-godot` map MAKU to room `$38`).

## 8. Pending formats

- Room object stream (`objects/{game}/pointers.s`, `mainData.s`): interactions,
  enemies, parts, static dungeon objects.
- `roomGfxChanges` and `checkLoadPastSignAndChestGfx`.
- `dungeonLayouts.s` and large-room size selection.
- Seasonal tilesets (`m_SeasonalTileset`, `wRoomStateModifier`) and four
  layouts per room.
- Text (`text/{game}/*.yaml`), audio (`audio/`, `code/audio.s`) and save SRAM.
