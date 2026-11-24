namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// A decoded Oracle tileset: two VRAM banks of 2bpp tiles plus the eight
/// four-colour background palettes used by its metatile attributes.
/// </summary>
public sealed class TilesetRenderer
{
    public const int TileSize = 8;
    public const int MetatileSize = 16;

    private readonly byte[]?[] _tilesBank0 = new byte[256][];
    private readonly byte[]?[] _tilesBank1 = new byte[256][];
    private readonly Rgba[] _colors = new Rgba[8 * 4];
    private readonly bool[] _paletteLoaded = new bool[8];

    public int TilesetId { get; }
    public IReadOnlyList<string> SourcePngs => _sourcePngs;
    private readonly List<string> _sourcePngs = new();

    /// <summary>
    /// Ages draws past-overworld cliffs with the present palette by rewriting
    /// attribute palette 6 to palette 0 for metatiles $40..$ff
    /// (code/ages/loadTilesetData.s:setPastCliffPalettesToRed).
    /// </summary>
    public bool PastCliffPalettes { get; set; }

    public TilesetRenderer(int tilesetId)
    {
        TilesetId = tilesetId;
        for (var i = 0; i < _colors.Length; i++)
            _colors[i] = Rgba.Black;
    }

    public bool IsPaletteLoaded(int palette) => palette is >= 0 and < 8 && _paletteLoaded[palette];

    public bool IsTileLoaded(int tile, int bank)
    {
        var bankTiles = bank == 0 ? _tilesBank0 : _tilesBank1;
        return bankTiles[SlotFor(tile, bank)] != null;
    }

    public int LoadedTileCountBank0 => _tilesBank0.Count(t => t != null);
    public int LoadedTileCountBank1 => _tilesBank1.Count(t => t != null);

    public void LoadGraphics(IEnumerable<GfxEntry> entries, OracleRepo repo)
    {
        foreach (var entry in entries)
        {
            if (!TryGetTileLocation(entry.Destination, out var bank, out var offset))
                continue;

            var pngPath = repo.FindPng(entry.Name);
            if (pngPath == null)
                continue;

            _sourcePngs.Add(pngPath);

            var image = Png.LoadIndexed2bpp(pngPath);
            var tilesX = image.Width / TileSize;
            var tilesY = image.Height / TileSize;
            var target = bank == 0 ? _tilesBank0 : _tilesBank1;

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

                    target[tileIndex] = tile;
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
            var bank = (attributes & 0x08) != 0 ? 1 : 0;
            var palette = attributes & 0x07;
            var flipX = (attributes & 0x20) != 0;
            var flipY = (attributes & 0x40) != 0;

            if (PastCliffPalettes && metatile >= 0x40 && palette == 6)
                palette = 0;

            var bankTiles = bank == 0 ? _tilesBank0 : _tilesBank1;
            var tile = bankTiles[SlotFor(tileIndex, bank)];
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
                    // PNG PLTE is [255,170,85,0]: index 0 is white and already CGB shade 0.
                    var shade = tile[srcY * TileSize + srcX];
                    image[originX + x, originY + y] = _colors[palette * 4 + shade];
                }
            }
        }

        return image;
    }

    /// <summary>
    /// GB tilemap tile numbers map to VRAM slots at $8800 with a +$80 wrap.
    /// Bank 0 holds $8000-$87ff (128 tiles); the mapping byte's low seven bits
    /// address it.
    /// </summary>
    private static int SlotFor(int tile, int bank)
    {
        return bank == 0 ? tile & 0x7F : (tile + 0x80) & 0xFF;
    }

    private static bool TryGetTileLocation(int destination, out int bank, out int offset)
    {
        bank = destination & 0x0F;
        var address = destination & 0xFFF0;

        if (bank == 0 && address is >= 0x8000 and <= 0x87F0)
        {
            offset = (address - 0x8000) / 16;
            return true;
        }

        if (bank == 1 && address is >= 0x8800 and <= 0x97F0)
        {
            offset = (address - 0x8800) / 16;
            return true;
        }

        offset = -1;
        return false;
    }

    private static byte Expand5(byte value)
    {
        return (byte)((value << 3) | (value >> 2));
    }
}
