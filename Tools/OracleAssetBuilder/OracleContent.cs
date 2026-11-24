namespace Oracles.Tools.AssetBuilder;

/// <summary>Save-dependent switches used by checkTilesetOverride and companion layouts.</summary>
public sealed record OracleOptions(
    int? Companion = null,
    bool MakuSaved = false,
    bool JabuUnderwater = false)
{
    public const int Ricky = 0x0B;
    public const int Dimitri = 0x0C;
    public const int Moosh = 0x0D;
}

/// <summary>
/// Resolved view of one game's disassembly tables. Tables are parsed once per run.
/// </summary>
public sealed class OracleContent
{
    public OracleRepo Repo { get; }
    public int Group { get; }
    public IReadOnlyList<TilesetInfo> Tilesets => _tilesets;

    private readonly IReadOnlyList<TilesetInfo> _tilesets;
    private readonly Dictionary<int, byte[]> _mappings = new();
    private readonly Dictionary<int, byte[]> _collisions = new();
    private readonly Dictionary<string, Rgb555[]> _paletteData;

    private readonly string _gfxHeadersPath;
    private readonly string _uniqueGfxHeadersPath;
    private readonly string _paletteHeadersPath;
    private readonly string _groupTilesetsPath;
    private readonly byte[]? _roomPacks;
    private readonly Dictionary<int, (string Mappings, string Collisions)> _layoutHeaders;

    public OracleContent(OracleRepo repo, int group)
    {
        Repo = repo;
        Group = group;
        _gfxHeadersPath = Path.Combine(repo.DataDir, "gfxHeaders.s");
        _uniqueGfxHeadersPath = Path.Combine(repo.DataDir, "uniqueGfxHeaders.s");
        _paletteHeadersPath = Path.Combine(repo.DataDir, "paletteHeaders.s");
        _groupTilesetsPath = Path.Combine(repo.RoomsDir, $"group{group}Tilesets.bin");
        _roomPacks = ReadOptionalRoomPacks(group);
        _layoutHeaders = AsmParser.ParseLayoutHeaders(Path.Combine(repo.DataDir, "tilesetHeaders.s"));

        var paletteDataPath = Path.Combine(repo.DataDir, "paletteData.s");
        _paletteData = AsmParser.ParsePaletteData(paletteDataPath);

        var symbols = new Dictionary<string, int>(StringComparer.Ordinal);
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_gfxHeadersPath, "m_GfxHeaderStart"));
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_uniqueGfxHeadersPath, "m_UniqueGfxHeaderStart"));
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_paletteHeadersPath, "m_PaletteHeaderStart"));

        _tilesets = AsmParser.ParseTilesetData(Path.Combine(repo.DataDir, "tilesets.s"), symbols);
    }

    private byte[]? ReadOptionalRoomPacks(int group)
    {
        var name = group switch
        {
            0 => "roomPacksPresent.bin",
            1 => "roomPacksPast.bin",
            _ => "roomPacks.bin",
        };
        var path = Path.Combine(Repo.RoomsDir, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
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

    /// <summary>
    /// loadTilesetData_body: bit 7 is OR'd into wTilesetUniqueGfx (a flag); the
    /// tileset index is the low seven bits. checkTilesetOverride then adjusts it
    /// for a few save-dependent cases.
    /// </summary>
    public int GetRoomTileset(int room, OracleOptions options)
    {
        var bytes = File.ReadAllBytes(_groupTilesetsPath);
        if (bytes.Length < 256)
            throw new InvalidDataException($"{_groupTilesetsPath}: expected 256 bytes, found {bytes.Length}.");

        var tileset = bytes[room & 0xFF] & 0x7F;

        // checkTilesetOverride@checkMakuTreeSaved: saved Maku Tree room uses $24.
        if (Group == 0 && (room & 0xFF) == 0x38 && options.MakuSaved)
            tileset += 2;

        // checkTilesetOverride@checkJabuFlooded: flooded Jabu floor uses $3f.
        if (options.JabuUnderwater && tileset == 0x3E)
            tileset += 1;

        if (tileset >= _tilesets.Count)
            throw new InvalidDataException($"Room 0x{room:x2}: resolved tileset 0x{tileset:x2} is outside the tileset table.");

        return tileset;
    }

    /// <summary>
    /// checkTilesetOverride's companion branch only changes the layout group:
    /// Ricky keeps the base layout, Dimitri uses group 1, Moosh/other group 3.
    /// </summary>
    public int GetLayoutGroup(int room, TilesetInfo info, OracleOptions options)
    {
        if (Group == 0 &&
            _roomPacks is { Length: >= 256 } &&
            _roomPacks[room & 0xFF] == 0x7F &&
            options.Companion is { } companion &&
            companion != OracleOptions.Ricky)
        {
            return companion == OracleOptions.Dimitri ? 1 : 3;
        }

        return info.LayoutGroup;
    }

    public string GetMappingPath(int layout)
    {
        var label = _layoutHeaders.TryGetValue(layout, out var header) ? header.Mappings : $"tilesetMappings{layout:x2}";
        return Path.Combine(Repo.TilesetLayoutsDir, label + ".bin");
    }

    public string GetCollisionPath(int layout)
    {
        var label = _layoutHeaders.TryGetValue(layout, out var header) ? header.Collisions : $"tilesetCollisions{layout:x2}";
        return Path.Combine(Repo.TilesetLayoutsDir, label + ".bin");
    }

    public byte[] GetMappings(int layout)
    {
        if (_mappings.TryGetValue(layout, out var cached))
            return cached;

        var path = GetMappingPath(layout);
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

        var path = GetCollisionPath(layout);
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
    public sealed record RoomLayout(int Width, int Height, byte[] Tiles, string SourcePath);

    public RoomLayout GetRoomLayout(int layoutGroup, int room, bool largeRoom)
    {
        var small = Path.Combine(Repo.RoomsDir, "small", $"room{layoutGroup:x2}{room:x2}.bin");
        if (File.Exists(small))
        {
            var data = File.ReadAllBytes(small);
            if (data.Length != 80)
                throw new InvalidDataException($"{small}: expected 80 bytes, found {data.Length}.");
            return new RoomLayout(10, 8, data, small);
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

        return new RoomLayout(width, height, tiles, large);
    }

    public TilesetRenderer CreateRenderer(int tilesetId, int? backgroundPaletteHeader, int room, OracleOptions options)
    {
        var info = GetTileset(tilesetId);
        var renderer = new TilesetRenderer(tilesetId)
        {
            // setPastCliffPalettesToRed: overworld collisions, past flag, room != $38.
            PastCliffPalettes = info.IsPastOrSubrosia &&
                                (info.CollisionModeAndDungeon >> 4) == 0 &&
                                (room & 0xFF) != 0x38,
        };

        renderer.LoadGraphics(AsmParser.ParseGfxHeader(_gfxHeadersPath, info.Gfx), Repo);

        // Unique graphics load after the main header and replace tiles at the
        // same VRAM destinations.
        if (info.UniqueGfx != 0)
            renderer.LoadGraphics(AsmParser.ParseGfxHeader(_uniqueGfxHeadersPath, info.UniqueGfx), Repo);

        // Background palette 0 is the engine's common palette (paletteData48e0).
        if (_paletteData.TryGetValue("paletteData48e0", out var commonBackground))
            renderer.ApplyPalette(0, 1, commonBackground);

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
