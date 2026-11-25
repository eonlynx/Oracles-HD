# OracleAssetBuilder

Offline importer that converts a pinned `external/oracles-disasm` checkout into
the data formats consumed by Oracles-HD (which are the formats of
Links-Awakening-DX-HD v2.0.6).

This tool is **not** part of the game build. The game project excludes
`Tools/**`, and generated assets are ignored by Git.

## Status (Phase 3/4 slice)

| Feature | Status |
|---|---|
| Tileset tables (`tilesets.s`, bit 7 mask) | Done |
| 2bpp PNG decode (dependency-free) | Done |
| Metatile mappings + CGB attributes | Done |
| VRAM tile offset (`(tile + 0x80) & 0xff`) | Done |
| PLTE shade order (0 = white; no inversion) | Done |
| Tileset palettes 2-7 | Done |
| Common BG palette 0 (`paletteData48e0`) | Done |
| RGBA PNG writer + DX-HD `.map` writer | Done |
| Single room (10x8 / 15x11 storage) | Done |
| Ages overworld stitch (OUTDOORS rooms) | Done |
| VRAM bank 0 + bank 1 tiles | Done (Ages room layouts use bank 1) |
| Layout-label aliases (`tilesetLayoutGroupXX`) | Done (e.g. $20 -> `tilesetMappings1b`) |
| Past cliff palette rewrite (6 -> 0) | Done (`setPastCliffPalettesToRed`) |
| Maku / Jabu / companion overrides | Done (save-dependent flags) |
| Animated tiles (atlas + `.map` timeline) | Done |
| Tile collisions + terrain (`.map` sections) | Done |
| Runtime overworld entry (`--oracle`) | Done |
| Present interiors (group 2) + warps | Done for the 22 group-0-reachable rooms |\n| Past interiors (group 3) + warps | Done for the 16 group-1-reachable rooms |\n| Dungeon data + floor layouts | Done: typed tables + `dungeons` maps |\n| Enemy/part/item placements | Done: typed table; runtime AI/art pending |
| Room object stream (positioned interactions) | Partial (default animation; enemies/parts/items counted only) |
| Interaction OAM atlas + shared entity plan | Done |
| Maku Tree `$87` eyes + blink | Done |
| `roomGfxChanges`, enemy AI, stairs/large-room routing | TODO |

## Animated tiles: resolved

`oracles-disasm/tools/dump/dumpAnimationData.py` names the pair explicitly:

```python
counter = rom[address]
gfxIndex = rom[address + 1]
```

and writes `.db counter gfxIndex`. The order is **`(delay, gfxIndex)`**. This
matches `ooa-godot`'s importer/regressions, which pin waterfall headers
`0, 24, 32`; those are the second bytes of `animationDataWaterfall`
(`.db $03 $00`, `.db $01 $18`, `.db $04 $20`).

Frames are rendered into the **existing atlas** as metatile variants; the
timeline is embedded in the **existing `.map`** under `#oracle-anim v1`. No
`.ani` or extra data file is generated. `Map.UpdateTileAnimations` swaps the
visual atlas index; collision and terrain are separate sections and never
change with the animation.

## Commands

All numeric values are hexadecimal.

```powershell
# Render one room: .map + tileset atlas + preview + manifest
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --room 39

# Stitch every OUTDOORS room of a group into one continuous map
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages

# Build present-day interiors reachable from group 0, with warps and objects
dotnet run --project Tools/OracleAssetBuilder -- interiors --game ages
dotnet run --project Tools/OracleAssetBuilder -- interiors --game ages --room e7

# Print a tileset's tables
dotnet run --project Tools/OracleAssetBuilder -- inspect --game ages --tileset 00

# Save-dependent tileset/layout overrides (checkTilesetOverride)
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 1
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --group 4 --room 01
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --room 39 --companion dimitri
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --room 38 --maku-saved
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --group 4 --room 3e --jabu-underwater
```

Outputs:

- `Data/Maps/oracle_<game>_g<group>_r<room>.map`
- `Data/Maps/Tilesets/oracle_<game>_t<tileset>.png` (1024x64 at 16px/metatile)
- `Data/Maps/Previews/*.png` (160x128 per room, 2240x1792 for the Ages overworld)
- `Data/Map Objects/oracle_<game>_entities.png` + `.atlas` (shared interaction atlas)
- `*.manifest.json` with SHA-256 of every input and output

## Decode rules that are easy to get wrong

These were verified against `ooa-godot` (`OracleRoomData.RenderRoom`),
`top/engine/game/data/tileset.c` and the disassembly itself:

1. **Tileset index is the low seven bits** of the room's byte in
   `groupNTilesets.bin`. Bit 7 is a unique-gfx flag, not part of the index.
2. **Mapping tile bytes are GB tilemap numbers**: the VRAM slot is
   `(tile + 0x80) & 0xff`.
3. **The disassembly PNG palette is `[255,170,85,0]`**: PNG index 0 is white
   and already equals CGB shade 0. Do **not** invert the shade. (`ooa-godot`
   reads RGBA and computes `(255 - red + 42) / 85`, which yields the same
   index.)
4. **Mapping row layout**: bytes 0-3 are the four tile indices (top-left,
   top-right, bottom-left, bottom-right); bytes 4-7 are the four attributes
   (palette 0-2, VRAM bank bit 3, flip X bit 5, flip Y bit 6, priority bit 7).
5. **Tileset palettes load slots 2-7 only.** BG palette 0 is the engine's
   common palette `paletteData48e0` (see `ooa-godot` `BackgroundPaletteState`).
   Slot 1 is the textbox palette.
6. **Unique graphics load after the main header** and overwrite tiles at the
   same VRAM destinations.
7. **Layout groups 4/5 store 176 bytes** (11 rows of 16). A normal room uses
   the first 10x8; `--large` uses 15x11.

## Known gaps

- VRAM bank 0 (attribute bit 3 clear): no room in the tested Ages outdoor
  layouts uses it, but dungeons may. Needs the bank-0 source image.
- `checkTilesetOverride` (room/cutscene-specific tilesets) is not implemented.
- Animated tiles (water, flowers, torches) use the static frame.
- Collisions, terrain, objects and warps are not emitted yet.
- Previews show raw RGB555 colours; the game applies the GBC colour-correction
  shader at runtime, so in-game colours are less saturated.

