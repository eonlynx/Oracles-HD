using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public sealed record RoomInteraction(int Id, int SubId, int X, int Y, bool Conditional, string Source);

public sealed record RoomObjectData(
    IReadOnlyList<RoomInteraction> Interactions,
    int EnemyCount,
    int PartCount,
    int ItemCount);

/// <summary>
/// Reads the native room object stream. The macro/control-flow model follows
/// ooa-godot's tools/import_oracles/Shared/Import-ObjectStreams.ps1, but the
/// output is limited to the placements this milestone can render (positioned
/// interactions) plus counts for the remaining object kinds.
/// </summary>
public sealed class RoomObjectStreams
{
    private static readonly Regex RoomLabelRegex = new(
        @"^group(?<group>[0-7])Map(?<room>[0-9a-fA-F]{2})ObjectData:\s*$", RegexOptions.Compiled);

    private static readonly Regex InteractionRegex = new(
        @"^obj_Interaction\s+(?<args>.+)$", RegexOptions.Compiled);

    private static readonly Regex PointerRegex = new(
        @"^obj_Pointer\s+(?<label>[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.Compiled);

    private static readonly Regex PartRegex = new(
        @"^obj_Part\s+(?<args>.+)$", RegexOptions.Compiled);

    private static readonly Regex EnemyRegex = new(
        @"^obj_(?:RandomEnemy|SpecificEnemyA|SpecificEnemyB)\s+(?<args>.+)$", RegexOptions.Compiled);

    private static readonly Regex ItemRegex = new(
        @"^obj_ItemDrop\s+(?<args>.+)$", RegexOptions.Compiled);

    private static readonly Regex HexRegex = new(
        @"\$(?<value>[0-9a-fA-F]{1,2})", RegexOptions.Compiled);

    private readonly Dictionary<string, string[]> _blocks = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Group, int Room), string> _roomLabels = new();

    public RoomObjectStreams(OracleRepo repo)
    {
        var objectsDir = Path.Combine(repo.Root, "objects", repo.Game);
        foreach (var file in new[]
                 {
                     "extraData1.s", "enemyData.s", "extraData2.s",
                     "mainData.s", "extraData3.s", "extraData4.s",
                 })
        {
            var path = Path.Combine(objectsDir, file);
            if (File.Exists(path))
                LoadBlocks(path);
        }

        LoadRoomLabels(Path.Combine(objectsDir, "pointers.s"));
    }

    public RoomObjectData GetRoom(int group, int room)
    {
        if (!_roomLabels.TryGetValue((group, room), out var label) ||
            !_blocks.TryGetValue(label, out var block))
            return new RoomObjectData(Array.Empty<RoomInteraction>(), 0, 0, 0);

        var interactions = new List<RoomInteraction>();
        var enemies = 0;
        var parts = 0;
        var items = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        ParseBlock(label, block, interactions, ref enemies, ref parts, ref items, visited, conditional: false);
        return new RoomObjectData(interactions, enemies, parts, items);
    }

    /// <summary>Returns true when the native obj_End terminates the whole stream.</summary>
    private bool ParseBlock(string label, string[] lines, List<RoomInteraction> interactions,
        ref int enemies, ref int parts, ref int items, HashSet<string> visited, bool conditional)
    {
        if (!visited.Add(label))
            return false;

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("obj_EndPointer", StringComparison.Ordinal))
                return false;
            if (line.StartsWith("obj_End", StringComparison.Ordinal))
                return true;

            if (line.StartsWith("obj_Condition", StringComparison.Ordinal))
            {
                conditional = true;
                continue;
            }

            var pointer = PointerRegex.Match(line);
            if (pointer.Success)
            {
                var target = pointer.Groups["label"].Value;
                if (_blocks.TryGetValue(target, out var targetBlock) &&
                    ParseBlock(target, targetBlock, interactions, ref enemies, ref parts, ref items, visited, conditional))
                    return true;
                continue;
            }

            var interaction = InteractionRegex.Match(line);
            if (interaction.Success)
            {
                var values = ParseArguments(interaction.Groups["args"].Value);
                if (values.Count >= 4)
                    interactions.Add(new RoomInteraction(values[0], values[1], values[3], values[2], conditional,
                        $"{label}:{line}"));
                continue;
            }

            if (EnemyRegex.IsMatch(line))
            {
                enemies++;
                continue;
            }

            var part = PartRegex.Match(line);
            if (part.Success)
            {
                parts++;
                continue;
            }

            if (ItemRegex.IsMatch(line))
                items++;
        }

        return false;
    }

    private void LoadBlocks(string path)
    {
        string? current = null;
        var lines = new List<string>();

        void Flush()
        {
            if (current != null)
                _blocks[current] = lines.ToArray();
            lines.Clear();
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            if (line.EndsWith(':'))
            {
                Flush();
                current = line[..^1];
                continue;
            }

            if (current != null)
                lines.Add(line);
        }

        Flush();
    }

    private void LoadRoomLabels(string path)
    {
        var group = -1;
        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var table = Regex.Match(line, @"^group(?<group>[0-7])ObjectDataTable:\s*$");
            if (table.Success)
            {
                group = ParseHex(table.Groups["group"].Value);
                continue;
            }

            if (group < 0)
                continue;

            var pointer = Regex.Match(line, @"^\.dw\s+(?<label>[A-Za-z_][A-Za-z0-9_]*)\s*$");
            if (!pointer.Success)
                continue;

            var label = pointer.Groups["label"].Value;
            var roomMatch = Regex.Match(label, @"^group[0-7]Map(?<room>[0-9a-fA-F]{2})ObjectData$");
            if (!roomMatch.Success)
                continue;

            var room = ParseHex(roomMatch.Groups["room"].Value);
            _roomLabels[(group, room)] = label;
        }
    }

    private static List<int> ParseArguments(string text)
    {
        var values = new List<int>();
        foreach (Match match in HexRegex.Matches(text))
            values.Add(ParseHex(match.Groups["value"].Value));
        return values;
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return (index >= 0 ? line[..index] : line).Trim();
    }

    private static int ParseHex(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
