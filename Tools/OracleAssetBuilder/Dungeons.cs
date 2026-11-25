using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public sealed record DungeonInfo(
    int Id, int Group, int WallmasterRoom, int LayoutIndex,
    int Floors, int BaseFloorPlusThree, int UnlockedFloors, string Source);

public sealed record DungeonFloor(int Dungeon, int Floor, int[] Rooms);

/// <summary>
/// Reads dungeonData.s and dungeonLayouts.s. The layout table is exactly the
/// native 8x8-per-floor array starting at dungeonLayoutDataStart; labels are
/// converted to byte offsets so aliases (dungeon00/dungeon09) resolve to the
/// same floor data.
/// </summary>
public sealed class OracleDungeons
{
    private static readonly Regex DungeonRegex = new(
        @"^m_DungeonData\s+>wGroup(?<group>[45])RoomFlags,\s*\$(?<wall>[0-9a-fA-F]+),\s*(?<layout>[A-Za-z_][A-Za-z0-9_]*),\s*\$(?<floors>[0-9a-fA-F]+),\s*\$(?<base>[0-9a-fA-F]+),\s*\$(?<unlock>[0-9a-fA-F]+)",
        RegexOptions.Compiled);

    private static readonly Regex LabelRegex = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Compiled);

    private static readonly Regex DbRegex = new(
        @"^\.db\s+(?<bytes>[^;]+)", RegexOptions.Compiled);

    private static readonly Regex HexByteRegex = new(
        @"\$[0-9a-fA-F]{1,2}", RegexOptions.Compiled);

    private readonly string _game;
    private readonly OracleRepo _repo;
    private readonly Dictionary<int, byte[]> _roomProperties = new();
    private readonly Dictionary<string, int> _labelOffsets = new(StringComparer.Ordinal);
    private readonly List<byte> _layoutBytes = new();
    private readonly List<DungeonInfo> _dungeons = new();

    public OracleDungeons(OracleRepo repo)
    {
        _repo = repo;
        _game = repo.Game;
        LoadLayouts(Path.Combine(repo.DataDir, "dungeonLayouts.s"));
        LoadDungeons(Path.Combine(repo.DataDir, "dungeonData.s"));
    }

    public IReadOnlyList<DungeonInfo> Dungeons => _dungeons;

    public DungeonInfo Get(int id)
    {
        if (id < 0 || id >= _dungeons.Count)
            throw new ArgumentOutOfRangeException(nameof(id), id,
                $"Dungeon {id:x2} is not in dungeonData.s.");
        return _dungeons[id];
    }

    public int RoomProperties(int group, int room)
    {
        if (!_roomProperties.TryGetValue(group, out var bytes))
        {
            var path = Path.Combine(_repo.RoomsDir, $"group{group}DungeonProperties.bin");
            bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
            _roomProperties[group] = bytes;
        }
        return room >= 0 && room < bytes.Length ? bytes[room] : 0;
    }

    public DungeonFloor GetFloor(int dungeonId, int floor)
    {
        var dungeon = Get(dungeonId);
        if (floor < 0 || floor >= dungeon.Floors)
            throw new ArgumentOutOfRangeException(nameof(floor));

        var start = dungeon.LayoutIndex * 0x40 + floor * 0x40;
        if (start + 0x40 > _layoutBytes.Count)
            throw new InvalidDataException(
                $"dungeon {dungeonId:x2} floor {floor}: layout is outside dungeonLayouts.s.");

        var rooms = new int[64];
        for (var i = 0; i < 64; i++)
            rooms[i] = _layoutBytes[start + i];
        return new DungeonFloor(dungeonId, floor, rooms);
    }

    public static string DungeonTablePath(string mapsOutDir, string game) =>
        Path.Combine(mapsOutDir, "Dungeons", $"oracle_{game}_dungeons.tsv");

    public static string LayoutTablePath(string mapsOutDir, string game) =>
        Path.Combine(mapsOutDir, "Dungeons", $"oracle_{game}_dungeon_layouts.tsv");

    public void WriteTables(OracleRepo repo, string mapsOutDir)
    {
        var directory = Path.Combine(mapsOutDir, "Dungeons");
        Directory.CreateDirectory(directory);
        var dungeonPath = DungeonTablePath(mapsOutDir, _game);
        var layoutPath = LayoutTablePath(mapsOutDir, _game);

        var dungeons = new StringBuilder();
        dungeons.Append("dungeon\tgroup\twallmaster_room\tlayout_index\tfloors\tbase_floor_plus_3\tunlocked_floors\tsource\n");
        var layouts = new StringBuilder();
        layouts.Append("dungeon\tfloor\trow\tcol\troom\tdungeonLayouts.s\n");

        foreach (var dungeon in _dungeons.OrderBy(d => d.Id))
        {
            dungeons.Append($"0x{dungeon.Id:x2}\t{dungeon.Group}\t0x{dungeon.WallmasterRoom:x2}\t");
            dungeons.Append($"{dungeon.LayoutIndex}\t{dungeon.Floors}\t{dungeon.BaseFloorPlusThree}\t");
            dungeons.Append($"{dungeon.UnlockedFloors}\t{dungeon.Source}\n");

            if (dungeon.LayoutIndex < 0)
                continue;

            for (var floor = 0; floor < dungeon.Floors; floor++)
            {
                var data = GetFloor(dungeon.Id, floor);
                for (var row = 0; row < 8; row++)
                    for (var col = 0; col < 8; col++)
                    {
                        var room = data.Rooms[row * 8 + col];
                        if (room == 0)
                            continue;
                        layouts.Append($"0x{dungeon.Id:x2}\t{floor}\t{row}\t{col}\t0x{room:x2}\tdungeonLayouts.s\n");
                    }
            }
        }

        File.WriteAllText(dungeonPath, dungeons.ToString(), new UTF8Encoding(false));
        File.WriteAllText(layoutPath, layouts.ToString(), new UTF8Encoding(false));

        var manifestPath = Path.Combine(directory, $"oracle_{_game}_dungeons.manifest.json");
        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = _game,
            entry = "dungeons",
            dungeons = _dungeons.Count,
            floors = _dungeons.Sum(d => d.Floors),
            inputs = new[]
            {
                Hash(repo, Path.Combine(repo.DataDir, "dungeonData.s")),
                Hash(repo, Path.Combine(repo.DataDir, "dungeonLayouts.s")),
            },
            outputs = new[]
            {
                Hash(repo, dungeonPath),
                Hash(repo, layoutPath),
            },
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static object Hash(OracleRepo repo, string path)
    {
        return new
        {
            path = Path.GetRelativePath(repo.Root, path).Replace('\\', '/'),
            sha256 = File.Exists(path) ? OracleRepo.Sha256(path) : "",
        };
    }

    private void LoadDungeons(string path)
    {
        var id = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith("m_DungeonData", StringComparison.Ordinal))
                continue;

            var match = DungeonRegex.Match(line);
            if (!match.Success)
                throw new InvalidDataException($"{path}: malformed m_DungeonData: {line}");

            var layout = match.Groups["layout"].Value;
            if (!_labelOffsets.TryGetValue(layout, out var offset))
                throw new InvalidDataException($"{path}: unknown layout label '{layout}'.");

            var floors = ParseHex(match.Groups["floors"].Value);
            var layoutIndex = offset / 0x40;
            if (offset % 0x40 != 0)
                throw new InvalidDataException($"{path}: layout '{layout}' is not 0x40-aligned.");

            _dungeons.Add(new DungeonInfo(
                id++, int.Parse(match.Groups["group"].Value, CultureInfo.InvariantCulture),
                ParseHex(match.Groups["wall"].Value), layoutIndex, floors,
                ParseHex(match.Groups["base"].Value), ParseHex(match.Groups["unlock"].Value),
                $"dungeonData.s:{line}"));
        }
    }

    private void LoadLayouts(string path)
    {
        var offset = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                _labelOffsets.TryAdd(label.Groups["label"].Value, offset);
                continue;
            }

            var db = DbRegex.Match(line);
            if (!db.Success)
                continue;

            var byteMatches = HexByteRegex.Matches(db.Groups["bytes"].Value);
            foreach (Match byteMatch in byteMatches)
            {
                _layoutBytes.Add((byte)ParseHex(byteMatch.Value[1..]));
                offset++;
            }
        }
    }

    private static int ParseHex(string value) =>
        int.Parse(value.TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
