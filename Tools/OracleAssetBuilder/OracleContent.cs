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
    public OracleAnimationData Animation { get; }

    private readonly IReadOnlyList<TilesetInfo> _tilesets;
    private readonly Dictionary<int, byte[]> _mappings = new();
    private readonly Dictionary<int, byte[]> _collisions = new();
    private readonly Dictionary<string, Rgb555[]> _paletteData;

    private readonly string _gfxHeadersPath;
    private readonly string _uniqueGfxHeadersPath;
    private readonly string _paletteHeadersPath;
    private readonly Dictionary<string, int> _paletteHeaderSymbols;
    private readonly string _groupTilesetsPath;
    private readonly string _tilePropertiesDir;
    private readonly Dictionary<int, Dictionary<int, byte>> _hazards = new();
    private readonly Dictionary<int, Dictionary<int, byte>> _tileTypes = new();
    private readonly Dictionary<string, int> _tileTypeConstants = new(StringComparer.Ordinal);
    private readonly byte[]? _roomPacks;
    private readonly Dictionary<int, (string Mappings, string Collisions)> _layoutHeaders;
    private readonly int _makuOverrideRoom;
    private readonly int _makuOverrideIncrement;
    private readonly int _pastCliffExcludedRoom;
    private readonly int _makuTreePaletteHeader;
    private readonly int _makuTreeTilesetPaletteHeader;
    private readonly IReadOnlyList<RoomEntryEffect> _roomEntryEffects;

    /// <summary>
    /// The native Maku Tree interaction loads PALH_8f before its disappearance
    /// script. That header changes only BG palette 7 (the wooden wall/portal
    /// spot) from the flower palette to the wood palette; palettes 2-6 are
    /// identical. Present-room rendering applies it after the tileset header so
    /// the generated atlas matches the stable in-game palette.
    /// </summary>
    public int MakuTreePaletteHeader => _makuTreePaletteHeader;

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

        Animation = new OracleAnimationData(repo);
        _tilePropertiesDir = Path.Combine(repo.DataDir, "tile_properties");
        LoadTileProperties();
        var paletteDataPath = Path.Combine(repo.DataDir, "paletteData.s");
        _paletteData = AsmParser.ParsePaletteData(paletteDataPath);

        var symbols = new Dictionary<string, int>(StringComparer.Ordinal);
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_gfxHeadersPath, "m_GfxHeaderStart"));
        AddSymbols(symbols, AsmParser.ParseHeaderStartIndices(_uniqueGfxHeadersPath, "m_UniqueGfxHeaderStart"));
        _paletteHeaderSymbols = AsmParser.ParseHeaderStartIndices(_paletteHeadersPath, "m_PaletteHeaderStart");
        AddSymbols(symbols, _paletteHeaderSymbols);

        _tilesets = AsmParser.ParseTilesetData(Path.Combine(repo.DataDir, "tilesets.s"), symbols);

        var overrideRules = repo.Game == "ages"
            ? ParseTilesetOverrideRules(Path.Combine(repo.Root, "code", "ages", "loadTilesetData.s"))
            : (-1, 0, -1);
        _makuOverrideRoom = overrideRules.Item1;
        _makuOverrideIncrement = overrideRules.Item2;
        _pastCliffExcludedRoom = overrideRules.Item3;
        _makuTreePaletteHeader = ParseMakuTreePaletteHeader(repo, symbols);
        _makuTreeTilesetPaletteHeader =
            symbols.TryGetValue("PALH_TILESET_MAKU_TREE", out var makuTilesetPalette)
                ? makuTilesetPalette
                : -1;
        _roomEntryEffects = RoomEntryEffects.Parse(
            Path.Combine(repo.Root, "code", repo.Game, "roomSpecificCode.s"));
    }

    private static (int MakuRoom, int MakuIncrement, int PastCliffExcludedRoom)
        ParseTilesetOverrideRules(string path)
    {
        var lines = File.ReadAllLines(path);
        var makuRoom = -1;
        var makuIncrement = 0;
        var pastCliffRoom = -1;

        var block = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("@checkMakuTreeSaved:", StringComparison.Ordinal))
                block = "maku";
            else if (line.StartsWith("@checkJabuFlooded:", StringComparison.Ordinal))
                block = "";
            else if (line.StartsWith("setPastCliffPalettesToRed:", StringComparison.Ordinal))
                block = "cliff";

            if (block == "maku")
            {
                var room = System.Text.RegularExpressions.Regex.Match(line, @"cp\s+<ROOM_AGES_([0-9a-fA-F]+)");
                if (room.Success)
                    makuRoom = Convert.ToInt32(room.Groups[1].Value, 16) & 0xFF;
                if (line.Contains("inc (hl)", StringComparison.Ordinal))
                    makuIncrement++;
            }
            else if (block == "cliff")
            {
                var room = System.Text.RegularExpressions.Regex.Match(line, @"cp\s+<ROOM_AGES_([0-9a-fA-F]+)");
                if (room.Success)
                {
                    pastCliffRoom = Convert.ToInt32(room.Groups[1].Value, 16) & 0xFF;
                    block = "";
                }
            }
        }

        if (makuRoom < 0 || makuIncrement == 0 || pastCliffRoom < 0)
            throw new InvalidDataException(
                $"{path}: could not parse checkMakuTreeSaved/setPastCliffPalettesToRed room rules.");

        return (makuRoom, makuIncrement, pastCliffRoom);
    }

    private static int ParseMakuTreePaletteHeader(OracleRepo repo,
        IReadOnlyDictionary<string, int> symbols)
    {
        if (repo.Game != "ages")
            return -1;

        var path = Path.Combine(repo.Root, "object_code", repo.Game,
            "interactions", "makuTree.s");
        if (!File.Exists(path))
            return -1;

        var match = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(path),
            @"ld\s+a\s*,\s*(?<symbol>PALH_[A-Za-z0-9_]+)");
        if (!match.Success)
            return -1;

        var symbol = match.Groups["symbol"].Value;
        if (!symbols.TryGetValue(symbol, out var header))
            throw new InvalidDataException(
                $"{path}: palette symbol '{symbol}' is not defined in paletteHeaders.s.");
        return header;
    }

    /// <summary>
    /// Applies the palette loaded by the present Maku Tree interaction after
    /// the tileset header. Only palette 7 differs, so the ground/tree palettes
    /// remain those from the tileset table.
    /// </summary>
    public bool UsesMakuTreeWoodPalette(TilesetInfo info) =>
        _makuTreePaletteHeader >= 0 &&
        info.IsMaku &&
        info.Palette == _makuTreeTilesetPaletteHeader;

    public void ApplyMakuTreePalette(TilesetRenderer renderer)
    {
        if (_makuTreePaletteHeader >= 0)
            ApplyPaletteHeader(renderer, _makuTreePaletteHeader);
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

        // checkTilesetOverride@checkMakuTreeSaved: saved Maku Tree room.
        if (Group == 0 && (room & 0xFF) == _makuOverrideRoom && options.MakuSaved)
            tileset += _makuOverrideIncrement;

        // checkTilesetOverride@checkJabuFlooded: flooded Jabu floor.
        if (options.JabuUnderwater)
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

    private void LoadTileProperties()
    {
        var constantsPath = Path.Combine(Repo.Root, "constants", "common", "tileTypes.s");
        if (File.Exists(constantsPath))
        {
            foreach (var line in File.ReadLines(constantsPath))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line, @"\.define\s+TILETYPE_(?<name>\w+)\s+\$(?<value>[0-9a-fA-F]+)");
                if (match.Success)
                    _tileTypeConstants[match.Groups["name"].Value] =
                        int.Parse(match.Groups["value"].Value, System.Globalization.NumberStyles.HexNumber);
            }
        }

        var tileIndicesPath = Path.Combine(Repo.Root, "constants", "common", "tileIndices.s");
        if (File.Exists(tileIndicesPath))
        {
            foreach (var line in File.ReadLines(tileIndicesPath))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line, @"\.define\s+(?<name>TILEINDEX_\w+)\s+\$(?<value>[0-9a-fA-F]+)");
                if (match.Success)
                    _tileTypeConstants[match.Groups["name"].Value] =
                        int.Parse(match.Groups["value"].Value, System.Globalization.NumberStyles.HexNumber);
            }
        }

        ParseModeTable(Path.Combine(_tilePropertiesDir, "hazards.s"), _hazards);
        ParseModeTable(Path.Combine(_tilePropertiesDir, "tileTypeMappings.s"), _tileTypes);
    }

    public int GetConstant(string name) => Constant(name);

    private void ParseModeTable(string path, Dictionary<int, Dictionary<int, byte>> target)
    {
        if (!File.Exists(path))
            return;

        var modes = new List<int>();
        var current = new Dictionary<int, byte>();
        var hasData = false;

        void Flush()
        {
            if (current.Count == 0)
                return;

            foreach (var mode in modes)
                target[mode] = new Dictionary<int, byte>(current);

            modes.Clear();
            current.Clear();
            hasData = false;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var modeMatch = System.Text.RegularExpressions.Regex.Match(line, @"^@(?<name>\w+):");
            if (modeMatch.Success)
            {
                if (hasData)
                    Flush();

                var mode = ModeFor(modeMatch.Groups["name"].Value);
                if (mode.HasValue)
                    modes.Add(mode.Value);
                continue;
            }

            if (!line.StartsWith(".db", StringComparison.Ordinal) || modes.Count == 0)
                continue;

            var tokens = line[3..].Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var tile = ParseNumber(tokens[0]);
            var value = ParseNumberOrSymbol(tokens[1]);
            if (tile is < 0 or > 255 || value is < 0 or > 255)
                continue;

            current[tile] = (byte)value;
            hasData = true;
        }

        Flush();
    }

    private static int? ModeFor(string label) => label switch
    {
        "overworld" => 0,
        "indoors" => 1,
        "dungeons" => 2,
        "sidescrolling" => 3,
        "underwater" => 4,
        "five" => 5,
        _ => null,
    };

    private int ParseNumberOrSymbol(string token)
    {
        if (token.StartsWith('$') || char.IsDigit(token[0]))
            return ParseNumber(token);

        return _tileTypeConstants.TryGetValue(token, out var value) ? value : -1;
    }

    private static int ParseNumber(string token)
    {
        if (token.StartsWith('$'))
            return int.Parse(token[1..], System.Globalization.NumberStyles.HexNumber);
        return int.TryParse(token, out var value) ? value : -1;
    }

    /// <summary>
    /// Terrain byte consumed by the engine:
    /// 1 water, 2 deep water, 3 lava, 4 grass, 5 hole. Water/hole/lava are made
    /// solid until swimming and falling are implemented.
    /// </summary>
    public byte GetTerrain(int mode, int metatile)
    {
        mode = Math.Clamp(mode, 0, 5);

        var hazard = _hazards.TryGetValue(mode, out var hazards) && hazards.TryGetValue(metatile, out var h) ? h : 0;
        var type = _tileTypes.TryGetValue(mode, out var types) && types.TryGetValue(metatile, out var t) ? t : 0;

        var lava = Constant("LAVA");
        var hole = Constant("HOLE");
        var water = Constant("WATER");
        var sea = Constant("SEAWATER");
        var grass = Constant("GRASS");
        var whirlpool = Constant("WHIRLPOOL");

        if (hazard == 4 || type == lava)
            return 3;
        if (hazard == 2 || type == hole)
            return 5;
        if (hazard == 1 || type == water || type == whirlpool)
            return type == sea ? (byte)2 : (byte)1;
        if (type == sea)
            return 2;
        if (type == grass)
            return 4;

        return 0;
    }

    private int Constant(string name) => _tileTypeConstants.TryGetValue(name, out var value) ? value : -1;

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

    /// <summary>
    /// Source files that can affect an object/interior/overworld build, for
    /// manifest input hashes. Missing optional files are skipped.
    /// </summary>
    public IReadOnlyList<string> GetImportedSourcePaths()
    {
        var paths = new List<string>();
        void Add(string path)
        {
            if (File.Exists(path))
                paths.Add(path);
        }

        Add(Path.Combine(Repo.DataDir, "warpSources.s"));
        Add(Path.Combine(Repo.DataDir, "warpDestinations.s"));
        Add(Path.Combine(Repo.DataDir, "tile_properties", "warpTiles.s"));
        Add(Path.Combine(Repo.DataDir, "interactionData.s"));
        Add(Path.Combine(Repo.DataDir, "interactionAnimations.s"));
        Add(Path.Combine(Repo.DataDir, "interactionOamData.s"));
        Add(Path.Combine(Repo.DataDir, "objectGfxHeaders.s"));
        Add(Path.Combine(Repo.DataDir, "paletteData.s"));
        Add(Path.Combine(Repo.Root, "code", Repo.Game, "roomSpecificCode.s"));

        var objectsDir = Path.Combine(Repo.Root, "objects", Repo.Game);
        foreach (var file in new[]
                 {
                     "pointers.s", "extraData1.s", "enemyData.s", "extraData2.s",
                     "mainData.s", "extraData3.s", "extraData4.s",
                 })
            Add(Path.Combine(objectsDir, file));

        return paths;
    }

    public IReadOnlyList<RoomEntryEffect> GetRoomEntryEffects(int group)
    {
        return _roomEntryEffects.Where(effect => effect.Group == group).ToArray();
    }

    public TilesetRenderer CreateRenderer(int tilesetId, int? backgroundPaletteHeader, int room, OracleOptions options)    {
        var info = GetTileset(tilesetId);
        var renderer = new TilesetRenderer(tilesetId)
        {
            // setPastCliffPalettesToRed: overworld collisions, past flag, room
            // not the excluded Maku/source room parsed above.
            PastCliffPalettes = info.IsPastOrSubrosia &&
                                (info.CollisionModeAndDungeon >> 4) == 0 &&
                                (room & 0xFF) != _pastCliffExcludedRoom,
        };

        renderer.LoadGraphics(AsmParser.ParseGfxHeader(_gfxHeadersPath, info.Gfx), Repo);

        // Unique graphics load after the main header and replace tiles at the
        // same VRAM destinations. Palette-only unique headers are valid; their
        // palette is installed after the tileset palette below.
        int? uniquePaletteHeader = null;
        if (info.UniqueGfx != 0)
        {
            renderer.LoadGraphics(AsmParser.ParseGfxHeader(
                _uniqueGfxHeadersPath, info.UniqueGfx, allowEmpty: true), Repo);

            var uniquePalette = AsmParser.ParseGfxHeaderEndPalette(
                _uniqueGfxHeadersPath, info.UniqueGfx);
            if (uniquePalette != null &&
                _paletteHeaderSymbols.TryGetValue(uniquePalette, out var header))
                uniquePaletteHeader = header;
        }

        // Background palette 0 is the engine's common palette (paletteData48e0).
        if (_paletteData.TryGetValue("paletteData48e0", out var commonBackground))
            renderer.ApplyPalette(0, 1, commonBackground);

        if (backgroundPaletteHeader.HasValue)
            ApplyPaletteHeader(renderer, backgroundPaletteHeader.Value);

        ApplyPaletteHeader(renderer, info.Palette);

        if (uniquePaletteHeader.HasValue)
            ApplyPaletteHeader(renderer, uniquePaletteHeader.Value);
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
