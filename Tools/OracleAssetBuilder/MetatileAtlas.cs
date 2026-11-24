namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Packs 16x16 metatiles into a single PNG that DX-HD can consume as a tileset.
/// One atlas index maps to one (tileset, metatile) pair.
/// </summary>
public sealed class MetatileAtlas
{
    private readonly int _columns;
    private readonly Dictionary<long, int> _lookup = new();
    private readonly List<RgbaImage> _images = new();

    public int Count => _images.Count;

    public MetatileAtlas(int columns = 64)
    {
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));
        _columns = columns;
    }

    public static long Key(int tilesetId, int metatile) => ((long)tilesetId << 8) | (uint)(metatile & 0xFF);

    public int GetOrAdd(int tilesetId, int metatile, Func<RgbaImage> render)
    {
        var key = Key(tilesetId, metatile);
        if (_lookup.TryGetValue(key, out var existing))
            return existing;

        var index = _images.Count;
        _images.Add(render());
        _lookup.Add(key, index);
        return index;
    }

    public bool TryGetIndex(int tilesetId, int metatile, out int index)
    {
        return _lookup.TryGetValue(Key(tilesetId, metatile), out index);
    }

    public RgbaImage GetImage(int index) => _images[index];

    public RgbaImage Pack()
    {
        var rows = (_images.Count + _columns - 1) / _columns;
        var packed = new RgbaImage(_columns * TilesetRenderer.MetatileSize, Math.Max(1, rows) * TilesetRenderer.MetatileSize);

        for (var i = 0; i < _images.Count; i++)
        {
            var originX = (i % _columns) * TilesetRenderer.MetatileSize;
            var originY = (i / _columns) * TilesetRenderer.MetatileSize;
            packed.CopyFrom(_images[i], originX, originY);
        }

        return packed;
    }

    public void SavePng(string path)
    {
        Png.SaveRgba(path, Pack());
    }
}
