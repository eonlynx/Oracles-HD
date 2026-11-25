namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// One row of `roomSpecificCode.s`: a room-wide handler that runs when the room
/// is entered (music, minimap, respawn, story hooks). The handler number is the
/// native jump-table index; the runtime owns the supported subset.
/// </summary>
public readonly record struct RoomEntryEffect(int Group, int Room, int Handler);

public static class RoomEntryEffects
{
    public static IReadOnlyList<RoomEntryEffect> Parse(string path)
    {
        var result = new List<RoomEntryEffect>();
        if (!File.Exists(path))
            return result;

        var lines = File.ReadAllLines(path);
        var group = -1;
        var inTable = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]).Trim();
            var label = System.Text.RegularExpressions.Regex.Match(
                line, @"^roomSpecificCodeGroup(?<group>[0-7])Table:\s*$");
            if (label.Success)
            {
                group = label.Groups["group"].Value[0] - '0';
                inTable = true;
                continue;
            }

            if (!inTable)
                continue;

            if (!line.StartsWith(".db", StringComparison.Ordinal))
            {
                inTable = false;
                continue;
            }

            var values = System.Text.RegularExpressions.Regex.Matches(
                line, @"\$(?<value>[0-9a-fA-F]{1,2})");
            for (var v = 0; v + 1 < values.Count; v += 2)
            {
                var room = Convert.ToInt32(values[v].Groups["value"].Value, 16);
                var handler = Convert.ToInt32(values[v + 1].Groups["value"].Value, 16);
                if (room == 0)
                {
                    inTable = false;
                    break;
                }
                result.Add(new RoomEntryEffect(group, room, handler));
            }
        }

        return result
            .OrderBy(effect => effect.Group)
            .ThenBy(effect => effect.Room)
            .ThenBy(effect => effect.Handler)
            .ToArray();
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line[..index] : line;
    }
}
