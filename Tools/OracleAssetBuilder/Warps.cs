using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public readonly record struct WarpDestinationRecord(int Room, int YX, int Parameter, int Transition);

/// <summary>
/// One native warp source row. Adapted from ooa-godot's WarpDatabase and
/// tools/import_oracles/Shared/Import-Warps.ps1:
/// bit 6 marks a pointer row, the low nibble is the screen-edge mask, and the
/// high byte packs (destination group &lt;&lt; 4) | source transition.
/// </summary>
public sealed record WarpSourceRecord(
    int EdgeMask,
    int Room,
    int DestinationIndex,
    int DestinationGroup,
    int SourceTransition,
    int Position,
    bool IsPosition,
    bool IsDefault,
    string Source);

/// <summary>
/// Reads the native warp streams. This deliberately keeps the native source
/// order and the pointer-expanded position rows; the builder only needs the
/// room lookup, but the model mirrors the imported stream so later work can
/// add defaults and dungeon fallbacks without re-parsing.
/// </summary>
public sealed class OracleWarpData
{
    private static readonly Regex GroupBlockRegex = new(
        @"^group(?<group>[0-7])(?<kind>WarpSources|WarpDestTable):\s*$", RegexOptions.Compiled);

    private static readonly Regex StandardWarpRegex = new(
        @"^m_StandardWarp\s+\$(?<edge>[0-9a-fA-F]+)\s+\$(?<room>[0-9a-fA-F]+)\s+\$(?<dest>[0-9a-fA-F]+)\s+\$(?<group>[0-9a-fA-F]+)\s+\$(?<transition>[0-9a-fA-F]+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex PointerWarpRegex = new(
        @"^m_PointerWarp\s+\$(?<room>[0-9a-fA-F]+)\s+(?<label>[A-Za-z_][A-Za-z0-9_]*)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex PositionWarpRegex = new(
        @"^m_PositionWarp\s+\$(?<dest>[0-9a-fA-F]+)\s+\$(?<position>[0-9a-fA-F]+)\s+\$(?<group>[0-9a-fA-F]+)\s+\$(?<transition>[0-9a-fA-F]+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex WarpDestRegex = new(
        @"^m_WarpDest\s+\$(?<room>[0-9a-fA-F]+)\s+\$(?<position>[0-9a-fA-F]+)\s+\$(?<parameter>[0-9a-fA-F]+)\s+\$(?<transition>[0-9a-fA-F]+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex LabelRegex = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Compiled);

    private readonly List<WarpSourceRecord>[] _sources = new List<WarpSourceRecord>[8];
    private readonly List<WarpDestinationRecord>[] _destinations = new List<WarpDestinationRecord>[8];
    private readonly Dictionary<int, HashSet<int>> _warpTiles = new();
    private readonly Dictionary<int, Dictionary<int, int>> _facing = new();
    private readonly Dictionary<string, List<string>> _labels = new(StringComparer.Ordinal);

    public OracleWarpData(OracleRepo repo)
    {
        for (var i = 0; i < 8; i++)
        {
            _sources[i] = new List<WarpSourceRecord>();
            _destinations[i] = new List<WarpDestinationRecord>();
        }

        LoadSources(Path.Combine(repo.DataDir, "warpSources.s"));
        LoadDestinations(Path.Combine(repo.DataDir, "warpDestinations.s"));
        LoadWarpTiles(Path.Combine(repo.DataDir, "tile_properties", "warpTiles.s"), _warpTiles);
        LoadFacingTable(Path.Combine(repo.DataDir, "tile_properties", "facingDirAfterWarp.s"), _facing);
    }

    public IReadOnlyList<WarpSourceRecord> Sources(int group) => _sources[group];
    public IReadOnlyList<WarpDestinationRecord> Destinations(int group) => _destinations[group];

    public bool TryResolve(WarpSourceRecord source, out WarpDestinationRecord destination, out string error)
    {
        destination = default;
        error = string.Empty;
        if (source.DestinationGroup is < 0 or > 7)
        {
            error = $"{source.Source}: destination group {source.DestinationGroup} is invalid.";
            return false;
        }

        var table = _destinations[source.DestinationGroup];
        if (source.DestinationIndex < 0 || source.DestinationIndex >= table.Count)
        {
            error = $"{source.Source}: destination {source.DestinationGroup}:0x{source.DestinationIndex:x2} is outside the table ({table.Count} entries).";
            return false;
        }

        destination = table[source.DestinationIndex];
        return true;
    }

    public WarpDestinationRecord Resolve(WarpSourceRecord source)
    {
        if (!TryResolve(source, out var destination, out var error))
            throw new InvalidDataException(error);
        return destination;
    }

    public IEnumerable<WarpSourceRecord> FindSources(int group, int room)
    {
        if (group is < 0 or > 7)
            return Enumerable.Empty<WarpSourceRecord>();
        return _sources[group].Where(source => source.Room == room && !source.IsPosition);
    }

    public WarpSourceRecord? FindTileWarp(int group, int room)
    {
        return FindSources(group, room).FirstOrDefault(source => source.EdgeMask == 0 && !source.IsDefault);
    }

    public IReadOnlyList<WarpSourceRecord> FindPositionWarps(int group, int room)
    {
        if (group is < 0 or > 7)
            return Array.Empty<WarpSourceRecord>();
        return _sources[group].Where(source => source.Room == room && source.IsPosition).ToArray();
    }

    public IReadOnlyCollection<WarpSourceRecord> FindEdgeWarps(int group, int room)
    {
        return FindSources(group, room).Where(source => source.EdgeMask != 0).ToArray();
    }

    public bool IsWarpTile(int collisionMode, int metatile)
    {
        return _warpTiles.TryGetValue(Math.Clamp(collisionMode, 0, 5), out var tiles) && tiles.Contains(metatile);
    }

    /// <summary>
    /// Direction (DX-HD convention: 0 left, 1 up, 2 right, 3 down) after
    /// spawning on a warp/facing tile. Returns -1 when the table has no row.
    /// </summary>
    public int FacingDirection(int collisionMode, int metatile)
    {
        if (!_facing.TryGetValue(Math.Clamp(collisionMode, 0, 5), out var table) ||
            !table.TryGetValue(metatile, out var oracleDirection))
            return -1;

        // Oracle: 0 up, 1 right, 2 down, 3 left.
        return oracleDirection switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 0,
            _ => -1,
        };
    }

    private void LoadSources(string path)
    {
        var lines = File.ReadAllLines(path);

        // Pointer targets are ordinary labels in the same file. Index them first
        // so a pointer row can be expanded even when it appears before its target.
        string? currentLabel = null;
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                currentLabel = label.Groups["label"].Value;
                if (!_labels.ContainsKey(currentLabel))
                    _labels[currentLabel] = new List<string>();
                continue;
            }

            if (currentLabel != null)
                _labels[currentLabel].Add(line);
        }

        var group = -1;
        var lineNumber = 0;
        foreach (var raw in lines)
        {
            lineNumber++;
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('@'))
                continue;

            var groupMatch = GroupBlockRegex.Match(line);
            if (groupMatch.Success && groupMatch.Groups["kind"].Value == "WarpSources")
            {
                group = int.Parse(groupMatch.Groups["group"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                continue;
            }

            if (group < 0)
                continue;

            var standard = StandardWarpRegex.Match(line);
            if (standard.Success)
            {
                _sources[group].Add(new WarpSourceRecord(
                    ParseHex(standard.Groups["edge"].Value),
                    ParseHex(standard.Groups["room"].Value),
                    ParseHex(standard.Groups["dest"].Value),
                    ParseHex(standard.Groups["group"].Value),
                    ParseHex(standard.Groups["transition"].Value),
                    0, false, false,
                    $"{path}:{lineNumber}"));
                continue;
            }

            var pointer = PointerWarpRegex.Match(line);
            if (pointer.Success)
            {
                var room = ParseHex(pointer.Groups["room"].Value);
                var label = pointer.Groups["label"].Value;
                if (!_labels.TryGetValue(label, out var targetLines))
                    throw new InvalidDataException($"{path}: pointer warp label '{label}' was not found.");

                var expanded = 0;
                foreach (var targetLine in targetLines)
                {
                    var position = PositionWarpRegex.Match(targetLine);
                    if (!position.Success)
                        continue;

                    _sources[group].Add(new WarpSourceRecord(
                        0, room,
                        ParseHex(position.Groups["dest"].Value),
                        ParseHex(position.Groups["group"].Value),
                        ParseHex(position.Groups["transition"].Value),
                        ParseHex(position.Groups["position"].Value),
                        true, false,
                        $"{path}:{label}"));
                    expanded++;
                }

                if (expanded == 0)
                    throw new InvalidDataException($"{path}: pointer warp label '{label}' has no m_PositionWarp rows.");
                continue;
            }

            if (line.StartsWith("m_WarpListEndWithDefault", StringComparison.Ordinal))
            {
                // The row immediately before the terminator is the room default.
                var list = _sources[group];
                if (list.Count > 0)
                    list[^1] = list[^1] with { IsDefault = true };
            }
        }
    }

    private void LoadDestinations(string path)
    {
        var group = -1;
        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var groupMatch = GroupBlockRegex.Match(line);
            if (groupMatch.Success && groupMatch.Groups["kind"].Value == "WarpDestTable")
            {
                group = int.Parse(groupMatch.Groups["group"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                continue;
            }

            if (group < 0)
                continue;

            var destination = WarpDestRegex.Match(line);
            if (!destination.Success)
                continue;

            _destinations[group].Add(new WarpDestinationRecord(
                ParseHex(destination.Groups["room"].Value),
                ParseHex(destination.Groups["position"].Value),
                ParseHex(destination.Groups["parameter"].Value),
                ParseHex(destination.Groups["transition"].Value)));
        }
    }

    private static void LoadWarpTiles(string path, Dictionary<int, HashSet<int>> target)
    {
        if (!File.Exists(path))
            return;

        var modes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["overworld"] = 0,
            ["indoors"] = 1,
            ["dungeons"] = 2,
            ["sidescrolling"] = 3,
            ["underwater"] = 4,
            ["five"] = 5,
        };

        // Stacked labels share one stream: @overworld: immediately followed by
        // @underwater: means both modes use the same tile rows.
        var activeModes = new List<int>();
        var tiles = new HashSet<int>();
        void Flush()
        {
            if (tiles.Count > 0)
                foreach (var mode in activeModes)
                    target[mode] = new HashSet<int>(tiles);
            tiles.Clear();
            activeModes.Clear();
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw).Trim();
            var modeMatch = Regex.Match(line, @"^@(?<name>\w+):");
            if (modeMatch.Success)
            {
                if (tiles.Count > 0)
                    Flush();
                if (modes.TryGetValue(modeMatch.Groups["name"].Value, out var id))
                    activeModes.Add(id);
                continue;
            }

            if (activeModes.Count == 0 || !line.StartsWith(".db", StringComparison.Ordinal))
                continue;

            var tokens = line[3..].Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            var tile = ParseNumber(tokens[0]);
            if (tile == 0)
            {
                Flush();
                continue;
            }

            if (tile > 0 && tile <= 0xFF)
                tiles.Add(tile);
        }

        Flush();
    }

    private static void LoadFacingTable(string path, Dictionary<int, Dictionary<int, int>> target)
    {
        if (!File.Exists(path))
            return;

        var modes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["overworld"] = 0,
            ["indoors"] = 1,
            ["dungeons"] = 2,
            ["sidescrolling"] = 3,
            ["underwater"] = 4,
            ["five"] = 5,
        };

        var activeModes = new List<int>();
        var rows = new Dictionary<int, int>();
        void Flush()
        {
            if (rows.Count > 0)
                foreach (var mode in activeModes)
                    target[mode] = new Dictionary<int, int>(rows);
            rows.Clear();
            activeModes.Clear();
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw).Trim();
            var modeMatch = Regex.Match(line, @"^@(?<name>\w+):");
            if (modeMatch.Success)
            {
                if (rows.Count > 0)
                    Flush();
                if (modes.TryGetValue(modeMatch.Groups["name"].Value, out var id))
                    activeModes.Add(id);
                continue;
            }

            if (activeModes.Count == 0 || !line.StartsWith(".db", StringComparison.Ordinal))
                continue;

            var tokens = line[3..].Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var tile = ParseNumber(tokens[0]);
            if (tile == 0)
            {
                Flush();
                continue;
            }

            var direction = tokens[1] switch
            {
                "DIR_UP" => 0,
                "DIR_RIGHT" => 1,
                "DIR_DOWN" => 2,
                "DIR_LEFT" => 3,
                _ => ParseNumber(tokens[1]),
            };
            if (tile > 0 && tile <= 0xFF && direction is >= 0 and <= 3)
                rows[tile] = direction;
        }

        Flush();
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return (index >= 0 ? line[..index] : line).Trim();
    }

    private static int ParseHex(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static int ParseNumber(string value)
    {
        if (value.StartsWith('$'))
            value = value[1..];
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
    }
}
