using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public sealed record RoomInteraction(
    int Id, int SubId, int X, int Y, int Var03,
    byte ConditionMask, byte RoomFlagMask, byte RoomFlagValue, string Source);

public sealed record RoomEnemy(
    int Id, int SubId, int X, int Y, int Var03, bool Random,
    byte ConditionMask, byte RoomFlagMask, byte RoomFlagValue, string Source);

public sealed record RoomPart(
    int Id, int SubId, int X, int Y, int Var03,
    byte ConditionMask, byte RoomFlagMask, byte RoomFlagValue, string Source);

public sealed record RoomItem(
    int Flags, int Item, int X, int Y,
    byte ConditionMask, byte RoomFlagMask, byte RoomFlagValue, string Source);

public sealed record RoomObjectData(
    IReadOnlyList<RoomInteraction> Interactions,
    IReadOnlyList<RoomEnemy> Enemies,
    IReadOnlyList<RoomPart> Parts,
    IReadOnlyList<RoomItem> Items,
    int ConditionalCount);

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
    private int _lastEnemyAFlags;
    private int _lastItemFlags;

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
            return new RoomObjectData(Array.Empty<RoomInteraction>(), Array.Empty<RoomEnemy>(),
                Array.Empty<RoomPart>(), Array.Empty<RoomItem>(), 0);

        var interactions = new List<RoomInteraction>();
        var enemies = new List<RoomEnemy>();
        var parts = new List<RoomPart>();
        var items = new List<RoomItem>();
        _lastEnemyAFlags = 0;
        _lastItemFlags = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        ParseBlock(label, block, interactions, enemies, parts, items, visited,
            conditionMask: 0, roomFlagMask: 0, roomFlagValue: 0);

        // A parent initializer can own a child object-data block. The child
        // rows keep their own visibility rules, so this is only placement.
        foreach (var parent in interactions.ToArray())
        {
            var childGroup = InteractionVisibility.ChildGroup(
                parent.Id, parent.SubId, parent.Var03);
            if (childGroup == null || !_blocks.TryGetValue(childGroup, out var childBlock))
                continue;
            var childInteractions = new List<RoomInteraction>();
            var childEnemies = new List<RoomEnemy>();
            var childParts = new List<RoomPart>();
            var childItems = new List<RoomItem>();
            ParseBlock(childGroup, childBlock, childInteractions, childEnemies, childParts,
                childItems, new HashSet<string>(StringComparer.Ordinal),
                parent.ConditionMask, parent.RoomFlagMask, parent.RoomFlagValue);
            interactions.AddRange(childInteractions);
            enemies.AddRange(childEnemies);
            parts.AddRange(childParts);
            items.AddRange(childItems);
        }

        return new RoomObjectData(interactions, enemies, parts, items,
            interactions.Count(interaction => interaction.ConditionMask != 0));
    }

    /// <summary>Returns true when the native obj_End terminates the whole stream.</summary>
    private bool ParseBlock(string label, string[] lines, List<RoomInteraction> interactions,
        List<RoomEnemy> enemies, List<RoomPart> parts, List<RoomItem> items,
        HashSet<string> visited,
        byte conditionMask, byte roomFlagMask, byte roomFlagValue)
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
                var conditionValues = ParseArguments(line);
                if (conditionValues.Count > 0)
                    conditionMask = (byte)conditionValues[0];
                continue;
            }

            if (line.StartsWith("obj_BeforeEvent", StringComparison.Ordinal) ||
                line.StartsWith("obj_AfterEvent", StringComparison.Ordinal))
            {
                var eventPointer = PointerRegex.Match(line.Replace("obj_BeforeEvent", "obj_Pointer")
                    .Replace("obj_AfterEvent", "obj_Pointer"));
                if (eventPointer.Success)
                {
                    var target = eventPointer.Groups["label"].Value;
                    var after = line.StartsWith("obj_AfterEvent", StringComparison.Ordinal);
                    if (_blocks.TryGetValue(target, out var targetBlock) &&
                        ParseBlock(target, targetBlock, interactions, enemies, parts, items, visited,
                            conditionMask, 0x80, (byte)(after ? 0x80 : 0)))
                        return true;
                }
                continue;
            }

            var pointer = PointerRegex.Match(line);
            if (pointer.Success)
            {
                var target = pointer.Groups["label"].Value;
                if (_blocks.TryGetValue(target, out var targetBlock) &&
                    ParseBlock(target, targetBlock, interactions, enemies, parts, items, visited,
                        conditionMask, roomFlagMask, roomFlagValue))
                    return true;
                continue;
            }

            var interaction = InteractionRegex.Match(line);
            if (interaction.Success)
            {
                var values = ParseArguments(interaction.Groups["args"].Value);
                if (values.Count >= 4)
                    interactions.Add(new RoomInteraction(values[0], values[1], values[3], values[2],
                        values.Count >= 5 ? values[4] : 0,
                        conditionMask, roomFlagMask, roomFlagValue, $"{label}:{line}"));
                else if (values.Count >= 2)
                    interactions.Add(new RoomInteraction(values[0], values[1], -1, -1,
                        values.Count >= 3 ? values[2] : 0,
                        conditionMask, roomFlagMask, roomFlagValue, $"{label}:{line}"));
                continue;
            }

            var enemy = EnemyRegex.Match(line);
            if (enemy.Success)
            {
                ParseEnemy(line, enemy.Groups["args"].Value, enemies,
                    conditionMask, roomFlagMask, roomFlagValue, $"{label}:{line}");
                continue;
            }

            var part = PartRegex.Match(line);
            if (part.Success)
            {
                var values = ParseArguments(part.Groups["args"].Value);
                if (values.Count >= 3)
                {
                    var x = values.Count >= 5 ? values[3] : (values[2] & 0x0f) * 16;
                    var y = values.Count >= 5 ? values[2] : ((values[2] >> 4) & 0x0f) * 16;
                    var var03 = values.Count >= 5 ? values[4] : 0;
                    parts.Add(new RoomPart(values[0], values[1], x, y, var03,
                        conditionMask, roomFlagMask, roomFlagValue, $"{label}:{line}"));
                }
                continue;
            }

            var item = ItemRegex.Match(line);
            if (item.Success)
            {
                var values = ParseArguments(item.Groups["args"].Value);
                if (values.Count >= 3)
                {
                    _lastItemFlags = values[0];
                    items.Add(new RoomItem(_lastItemFlags, values[1],
                        (values[2] & 0x0f) * 16, ((values[2] >> 4) & 0x0f) * 16,
                        conditionMask, roomFlagMask, roomFlagValue, $"{label}:{line}"));
                }
                else if (values.Count == 2)
                {
                    items.Add(new RoomItem(_lastItemFlags, values[0],
                        (values[1] & 0x0f) * 16, ((values[1] >> 4) & 0x0f) * 16,
                        conditionMask, roomFlagMask, roomFlagValue, $"{label}:{line}"));
                }
            }
        }

        return false;
    }

    private void ParseEnemy(string line, string args, List<RoomEnemy> enemies,
        byte conditionMask, byte roomFlagMask, byte roomFlagValue, string source)
    {
        var values = ParseArguments(args);
        var random = line.Contains("obj_RandomEnemy", StringComparison.Ordinal);
        var specificA = line.Contains("obj_SpecificEnemyA", StringComparison.Ordinal);
        var specificB = line.Contains("obj_SpecificEnemyB", StringComparison.Ordinal);

        int id, subId, x, y, var03;
        if (random)
        {
            if (values.Count < 3)
                return;
            id = values[1];
            subId = values[2];
            x = y = -1;
            var03 = 0;
        }
        else if (specificB)
        {
            if (values.Count < 5)
                return;
            id = values[0];
            subId = values[1];
            y = values[2];
            x = values[3];
            var03 = values[4];
        }
        else if (specificA)
        {
            if (values.Count >= 5)
            {
                _lastEnemyAFlags = values[0];
                id = values[1];
                subId = values[2];
                y = values[3];
                x = values[4];
            }
            else if (values.Count >= 4)
            {
                id = values[0];
                subId = values[1];
                y = values[2];
                x = values[3];
            }
            else
            {
                return;
            }
            var03 = 0;
        }
        else
        {
            return;
        }

        enemies.Add(new RoomEnemy(id, subId, x, y, var03, random,
            conditionMask, roomFlagMask, roomFlagValue, source));
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

