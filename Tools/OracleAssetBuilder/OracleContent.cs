
namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Resolved view of one game's disassembly tables. Tables are parsed once per run.
/// </summary>
public sealed class OracleContent
{
    public OracleRepo Repo { get; }
    public IReadOnlyList<TilesetInfo> Tilesets => _tilesets;

    private readonly IReadOnlyList<TilesetInfo> _tilesets;
    private readonly Dictionary<int, byte[]> _mappings = new();
    private readonly Dictionary<int, byte[]> _collisions = new();
    private readonly Dictionary<string, Rgb555[]> _paletteData;

    private readonly string _gfxHeadersPath;
    private readonly string _paletteHeadersPath;
    private readonly string _tilesetsPath;
    private readonly string _groupTilesetsPath;

    public OracleContent(OracleRepo repo, int group)
    {
        Repo = repo;
        _gfxHeadersPath = Path.Combine(repo.DataDir, "gfxHeaders.s");
        _paletteHeadersPath = Path.Combine(repo.DataDir, "paletteHeaders.s");
        _tilesetsPath = Path.Combine(repo.DataDir, "tilesets.s");
        _groupTilesetsPath = Path.Combine(repo.RoomsDir, $"group{group}Tilesets.bin");

        var paletteDataPath = Path.Combine(repo.DataDir, "paletteData.s");
        _paletteData = AsmParser.ParsePaletteData(paletteDataPath);

        var symbols = new Dictionary<string, int>(StringComparer.Ordinal);
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_gfxHeadersPath, "m_GfxHeaderStart"));
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(Path.Combine(repo.DataDir, "uniqueGfxHeaders.s"), "m_UniqueGfxHeaderStart"));
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_paletteHeadersPath, "m_PaletteHeaderStart"));

        _tilesets = AsmParser.ParseTilesetData(_tilesetsPath, symbols);
    }

    private static void AddSymbols(Dictionary<string, int> target, Dictionary<string, int> source)
    {
        foreach (var pair in source)
        {
            if (target.TryGetValue(pair.Key, out var existing) && existing != pair.Value)
                throw new InvalidDataException($"Conflicting symbol '{pair.Key}': ${existing:x2} vs ${pair.Value:x2}.");
            target[pair.Key] = pair.Value;
        }
    }

    public TilesetInfo GetTileset(int tilesetId)
    {
        if (tilesetId < 0 || tilesetId >= _tilesets.Count)
            throw new ArgumentOutOfRangeException(nameof(tilesetId), $"Tileset {tilesetId} is outside 0..{_tilesets.Count - 1}.");
        return _tilesets[tilesetId];
    }

    public int GetRoomTileset(int room)
    {
        var bytes = File.ReadAllBytes(_groupTilesetsPath);
        if (bytes.Length < 256)
            throw new InvalidDataException($"{_groupTilesetsPath}: expected 256 bytes, found {bytes.Length}.");

        // loadTilesetData_body: bit 7 is OR'd into wTilesetUniqueGfx (a flag);
        // the tileset index itself is the low seven bits.
        return bytes[room & 0xFF] & 0x7F;
    }

    public byte[] GetMappings(int layout)
    {
        if (_mappings.TryGetValue(layout, out var cached))
            return cached;

        var path = Path.Combine(Repo.TilesetLayoutsDir, $"tilesetMappings{layout:x2}.bin");
        var data = File.ReadAllBytes(path);
        if (data.Length != 2048)
            throw new InvalidDataException($"{path}: expected 2048 bytes, found {data.Length}.");
        _mappings.Add(layout, data);
        return data;
    }

    public byte[] GetCollisions(int layout)
    {
        if (_collisions.TryGetValue(layout, out var cached))
            return cached;

        var path = Path.Combine(Repo.TilesetLayoutsDir, $"tilesetCollisions{layout:x2}.bin");
        var data = File.ReadAllBytes(path);
        if (data.Length != 256)
            throw new InvalidDataException($"{path}: expected 256 bytes, found {data.Length}.");
        _collisions.Add(layout, data);
        return data;
    }

    /// <summary>
    /// A room layout with its stride removed. Small rooms are 10x8; layout groups
    /// 4/5 store 11 rows of 16 bytes, of which 10x8 (normal) or 15x11 (large) are used.
    /// </summary>
    public sealed record RoomLayout(int Width, int Height, byte[] Tiles);

    public RoomLayout GetRoomLayout(int layoutGroup, int room, bool largeRoom)
    {
        var small = Path.Combine(Repo.RoomsDir, "small", $"room{layoutGroup:x2}{room:x2}.bin");
        if (File.Exists(small))
        {
            var data = File.ReadAllBytes(small);
            if (data.Length != 80)
                throw new InvalidDataException($"{small}: expected 80 bytes, found {data.Length}.");
            return new RoomLayout(10, 8, data);
        }

        var large = Path.Combine(Repo.RoomsDir, "large", $"room{layoutGroup:x2}{room:x2}.bin");
        if (!File.Exists(large))
            throw new FileNotFoundException($"Room layout not found: {small} (or large/{Path.GetFileName(small)}).");

        var stored = File.ReadAllBytes(large);
        if (stored.Length != 176)
            throw new InvalidDataException($"{large}: expected 176 bytes, found {stored.Length}.");

        var width = largeRoom ? 15 : 10;
        var height = largeRoom ? 11 : 8;
        var tiles = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                tiles[y * width + x] = stored[y * 16 + x];

        return new RoomLayout(width, height, tiles);
    }

    public TilesetRenderer CreateRenderer(int tilesetId, int? backgroundPaletteHeader)
    {
        var info = GetTileset(tilesetId);
        var renderer = new TilesetRenderer(tilesetId);

        renderer.LoadGraphics(AsmParser.ParseGfxHeader(_gfxHeadersPath, info.Gfx), Repo);

        // Unique graphics are loaded after the main tileset and replace tiles at
        // the same VRAM destinations. They are part of the room's visible tileset.
        if (info.UniqueGfx != 0)
            renderer.LoadGraphics(AsmParser.ParseGfxHeader(Path.Combine(Repo.DataDir, "uniqueGfxHeaders.s"), info.UniqueGfx), Repo);

        // Background palette 0 is the engine's common palette (PALH_0f installs
        // paletteData48e0; see ooa-godot BackgroundPaletteState). It is not part
        // of the tileset palette header, which only loads 2-7.
        if (_paletteData.TryGetValue("paletteData48e0", out var commonBackground))
            renderer.ApplyPalette(0, 1, commonBackground);

        // Optional extra background palette (useful for experiments). The tileset
        // palette overrides 2-7 afterwards.
        if (backgroundPaletteHeader.HasValue)
            ApplyPaletteHeader(renderer, backgroundPaletteHeader.Value);

        ApplyPaletteHeader(renderer, info.Palette);
        return renderer;
    }

    private void ApplyPaletteHeader(TilesetRenderer renderer, int header)
    {
        foreach (var entry in AsmParser.ParsePaletteHeader(_paletteHeadersPath, header))
        {
            if (!_paletteData.TryGetValue(entry.DataLabel, out var colors))
                throw new InvalidDataException(
                    $"Palette header ${header:x2} references '{entry.DataLabel}', which has no m_RGB16 colours in paletteData.s.");
            renderer.ApplyPalette(entry.Start, entry.Count, colors);
        }
    }

    public RgbaImage RenderRoomPreview(RoomLayout layout, MetatileAtlas atlas, int tilesetId)
    {
        var image = new RgbaImage(layout.Width * TilesetRenderer.MetatileSize, layout.Height * TilesetRenderer.MetatileSize);

        for (var y = 0; y < layout.Height; y++)
        {
            for (var x = 0; x < layout.Width; x++)
            {
                var metatile = layout.Tiles[y * layout.Width + x];
                if (!atlas.TryGetIndex(tilesetId, metatile, out var atlasIndex))
                    continue;

                var source = atlas.GetImage(atlasIndex);
                var originX = x * TilesetRenderer.MetatileSize;
                var originY = y * TilesetRenderer.MetatileSize;

                for (var py = 0; py < TilesetRenderer.MetatileSize; py++)
                    for (var px = 0; px < TilesetRenderer.MetatileSize; px++)
                        image[originX + px, originY + py] = source[px, py];
            }
        }

        return image;
    }
}
