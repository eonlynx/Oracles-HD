namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// A decoded Oracle tileset: 256 8x8 2bpp tiles (VRAM banks flattened) plus the
/// eight 4-colour background palettes used by its metatile attributes.
/// </summary>
public sealed class TilesetRenderer
{
    public const int TileSize = 8;
    public const int MetatileSize = 16;

    private readonly byte[]?[] _tiles = new byte[256][];
    private readonly Rgba[] _colors = new Rgba[8 * 4];
    private readonly bool[] _paletteLoaded = new bool[8];

    public int TilesetId { get; }
    public IReadOnlyList<string> SourcePngs => _sourcePngs;
    private readonly List<string> _sourcePngs = new();

    public TilesetRenderer(int tilesetId)
    {
        TilesetId = tilesetId;
        // Unloaded palettes render black instead of throwing. Phase 2 reports them.
        for (var i = 0; i < _colors.Length; i++)
            _colors[i] = Rgba.Black;
    }

    public bool IsPaletteLoaded(int palette) => palette is >= 0 and < 8 && _paletteLoaded[palette];

    public bool IsTileLoaded(int tile) => _tiles[(tile + 0x80) & 0xFF] != null;

    public int LoadedTileCount => _tiles.Count(t => t != null);

    public void LoadGraphics(IEnumerable<GfxEntry> entries, OracleRepo repo)
    {
        foreach (var entry in entries)
        {
            if (!TryGetTileOffset(entry.Destination, out var offset))
                continue;

            var pngPath = repo.FindPng(entry.Name);
            if (pngPath == null)
                continue;

            _sourcePngs.Add(pngPath);

            var image = Png.LoadIndexed2bpp(pngPath);
            var tilesX = image.Width / TileSize;
            var tilesY = image.Height / TileSize;

            for (var ty = 0; ty < tilesY; ty++)
            {
                for (var tx = 0; tx < tilesX; tx++)
                {
                    var tileIndex = offset + ty * tilesX + tx;
                    if (tileIndex is < 0 or >= 256)
                        continue;

                    var tile = new byte[TileSize * TileSize];
                    for (var y = 0; y < TileSize; y++)
                        for (var x = 0; x < TileSize; x++)
                            tile[y * TileSize + x] = image[tx * TileSize + x, ty * TileSize + y];

                    _tiles[tileIndex] = tile;
                }
            }
        }
    }

    public void ApplyPalette(int start, int count, IReadOnlyList<Rgb555> colors)
    {
        for (var palette = 0; palette < count; palette++)
        {
            var target = start + palette;
            if (target is < 0 or >= 8)
                continue;

            for (var color = 0; color < 4; color++)
            {
                var sourceIndex = palette * 4 + color;
                if (sourceIndex >= colors.Count)
                    break;

                var c = colors[sourceIndex];
                _colors[target * 4 + color] = new Rgba(Expand5(c.R), Expand5(c.G), Expand5(c.B), 255);
            }
            _paletteLoaded[target] = true;
        }
    }

    public RgbaImage RenderMetatile(byte[] mappings, int metatile)
    {
        var image = new RgbaImage(MetatileSize, MetatileSize);
        var baseIndex = metatile * 8;

        for (var part = 0; part < 4; part++)
        {
            var tileIndex = mappings[baseIndex + part];
            var attributes = mappings[baseIndex + 4 + part];
            var palette = attributes & 0x07;
            var flipX = (attributes & 0x20) != 0;
            var flipY = (attributes & 0x40) != 0;

            // The mapping stores a GB tilemap number: slots start at $8800, so the
            // VRAM slot is (tile + $80) & $ff (see ooa-godot OracleRoomData.RenderRoom).
            var slot = (tileIndex + 0x80) & 0xFF;
            var tile = _tiles[slot];
            if (tile == null)
                continue;

            var originX = (part & 1) * TileSize;
            var originY = (part >> 1) * TileSize;

            for (var y = 0; y < TileSize; y++)
            {
                for (var x = 0; x < TileSize; x++)
                {
                    var srcX = flipX ? TileSize - 1 - x : x;
                    var srcY = flipY ? TileSize - 1 - y : y;
                    // The disassembly PNGs store 0 = white .. 3 = black, while the
                    // CGB palette index is 0 = lightest. Invert the shade.
                    var shade = 3 - tile[srcY * TileSize + srcX];
                    image[originX + x, originY + y] = _colors[palette * 4 + shade];
                }
            }
        }

        return image;
    }

    private static bool TryGetTileOffset(int destination, out int offset)
    {
        // Destination low nibble is the VRAM bank; the tileset's 256 tiles start at $8800.
        var address = destination & 0xFFF0;
        if (address is < 0x8800 or > 0x97F0)
        {
            offset = -1;
            return false;
        }

        offset = (address - 0x8800) / 16;
        return true;
    }

    private static byte Expand5(byte value)
    {
        return (byte)((value << 3) | (value >> 2));
    }
}
