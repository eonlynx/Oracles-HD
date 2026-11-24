# OracleAssetBuilder

Offline importer that converts a pinned `external/oracles-disasm` checkout into
the data formats consumed by Oracles-HD (which are the formats of
Links-Awakening-DX-HD v2.0.6).

This tool is **not** part of the game build. The game project excludes
`Tools/**`, and generated assets are ignored by Git.

## Status (Phase 2 core)

| Feature | Status |
|---|---|
| Tileset tables (`tilesets.s`, bit 7 mask) | Done |
| 2bpp PNG decode (dependency-free) | Done |
| Metatile mappings + CGB attributes | Done |
| VRAM tile offset (`(tile + 0x80) & 0xff`) | Done |
| Inverted shade (`3 - raw`) | Done |
| Tileset palettes 2-7 | Done |
| Common BG palette 0 (`paletteData48e0`) | Done |
| RGBA PNG writer + DX-HD `.map` writer | Done |
| Single room (10x8 / 15x11 storage) | Done |
| Ages overworld stitch (OUTDOORS rooms) | Done |
| VRAM bank 0 graphics | TODO |
| Room-specific tileset overrides | TODO |
| Collisions, objects, warps, animations | TODO (Phase 3+) |

## Commands

All numeric values are hexadecimal.

```powershell
# Render one room: .map + tileset atlas + preview + manifest
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --room 39

# Stitch every OUTDOORS room of a group into one continuous map
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages

# Print a tileset's tables
dotnet run --project Tools/OracleAssetBuilder -- inspect --game ages --tileset 00
```

Outputs:

- `Data/Maps/oracle_<game>_g<group>_r<room>.map`
- `Data/Maps/Tilesets/oracle_<game>_t<tileset>.png` (1024x64 at 16px/metatile)
- `Data/Maps/Previews/*.png` (160x128 per room, 2240x1792 for the Ages overworld)
- `*.manifest.json` with SHA-256 of every input and output

## Decode rules that are easy to get wrong

These were verified against `ooa-godot` (`OracleRoomData.RenderRoom`),
`top/engine/game/data/tileset.c` and the disassembly itself:

1. **Tileset index is the low seven bits** of the room's byte in
   `groupNTilesets.bin`. Bit 7 is a unique-gfx flag, not part of the index.
2. **Mapping tile bytes are GB tilemap numbers**: the VRAM slot is
   `(tile + 0x80) & 0xff`.
3. **The disassembly PNGs are inverted**: PNG 0 is white, 3 is black. The CGB
   palette index is `3 - png`.
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
