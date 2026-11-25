using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Generic behaviour for one interaction/subid, derived from the interaction's
/// own source block instead of per-id tables. This keeps collision, global-flag
/// gates, palette loads and warp behaviour automatic for every interaction that
/// uses the same native helpers.
/// </summary>
public readonly record struct InteractionBehavior(
    bool SolidTile,
    int RadiusY,
    int RadiusX,
    int RequiresFlag,
    int ForbiddenFlag,
    int SpritePaletteHeader,
    bool TimeWarp,
    int RequiresEntryDirection);

/// <summary>
/// Parses interaction code for the native helpers that define physical
/// behaviour:
///   - objectMarkSolidPosition / wRoomCollisions  -> full tile collision
///   - objectSetCollideRadius / objectSetCollideRadii -> explicit radii
///   - checkGlobalFlag + interactionDelete        -> required global flag
///   - PALH_x + loadPaletteHeader                 -> sprite palette header
///   - interactionBeginTimewarp                   -> present/past portal
/// The subid jump table (ld e,Interaction.subid / rst_jumpTable / .dw) scopes the
/// derived behaviour per subid when the source has one.
/// </summary>
public sealed class InteractionBehaviors
{
    private readonly Dictionary<(int Id, int SubId), InteractionBehavior> _behaviors = new();
    private readonly Dictionary<string, int> _paletteHeaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _globalFlags = new(StringComparer.Ordinal);

    private static readonly Regex TopLabelRegex = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Compiled);
    private static readonly Regex LocalLabelRegex = new(
        @"^@(?<label>[A-Za-z0-9_]+):\s*$", RegexOptions.Compiled);
    private static readonly Regex DwRegex = new(
        @"^\s*\.dw\s+(?<label>@?[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.Compiled);
    private static readonly Regex LabelRefRegex = new(
        @"\b(?<label>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);
    private static readonly Regex GlobalFlagRegex = new(
        @"^\s*(?<name>GLOBALFLAG_[A-Za-z0-9_]+)\s+db\s*;\s*\$(?<value>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);
    private static readonly Regex PaletteHeaderRegex = new(
        @"m_PaletteHeaderStart\s+\$(?<value>[0-9a-fA-F]{2})\s*,\s*(?<label>PALH_[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private sealed record Entry(string? Top, string? Local, string Text);

    public InteractionBehaviors(OracleRepo repo)
    {
        LoadGlobalFlags(Path.Combine(repo.Root, "constants", "common", "globalFlags.s"));
        LoadPaletteHeaders(Path.Combine(repo.DataDir, "paletteHeaders.s"));

        foreach (var root in new[]
                 {
                     Path.Combine(repo.Root, "object_code", repo.Game, "interactions"),
                     Path.Combine(repo.Root, "object_code", "common", "interactions"),
                 })
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.s"))
                ParseFile(file);
        }
    }

    public InteractionBehavior For(int id, int subId)
    {
        return _behaviors.TryGetValue((id, subId), out var behavior)
            ? behavior
            : new InteractionBehavior(false, 0, 0, -1, -1, -1, false, -1);
    }

    private void LoadGlobalFlags(string path)
    {
        if (!File.Exists(path))
            return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var match = GlobalFlagRegex.Match(raw);
            if (match.Success)
                _globalFlags.TryAdd(match.Groups["name"].Value, ParseHex(match.Groups["value"].Value));
        }
    }

    private void LoadPaletteHeaders(string path)
    {
        if (!File.Exists(path))
            return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var match = PaletteHeaderRegex.Match(raw);
            if (match.Success)
                _paletteHeaders.TryAdd(match.Groups["label"].Value, ParseHex(match.Groups["value"].Value));
        }
    }

    private void ParseFile(string path)
    {
        var entries = new List<Entry>();
        string? top = null;
        string? local = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = StripComment(raw).TrimEnd();
            var topLabel = TopLabelRegex.Match(line);
            if (topLabel.Success)
            {
                top = topLabel.Groups["label"].Value;
                local = null;
                entries.Add(new Entry(top, null, line));
                continue;
            }

            var localLabel = LocalLabelRegex.Match(line);
            if (localLabel.Success)
            {
                local = localLabel.Groups["label"].Value;
                entries.Add(new Entry(top, local, line));
                continue;
            }

            entries.Add(new Entry(top, local, line));
        }

        var topLabels = new HashSet<string>(
            entries.Where(entry => entry.Local == null && entry.Text.EndsWith(':'))
                .Select(entry => entry.Top!),
            StringComparer.Ordinal);

        for (var start = 0; start < entries.Count; start++)
        {
            if (!TryInteractionCode(entries[start].Text, out var id))
                continue;

            var end = start + 1;
            while (end < entries.Count && !TryInteractionCode(entries[end].Text, out _))
                end++;

            var region = entries.GetRange(start, end - start);
            var targets = FindSubIdTable(region);

            if (targets.Count == 0)
            {
                var behavior = Analyze(Expand(entries, topLabels, region.Select(entry => entry.Text).ToList()));
                for (var subId = 0; subId < 0x100; subId++)
                    _behaviors[(id, subId)] = behavior;
                continue;
            }

            // Shared state code (state handlers, helpers) applies to every
            // subid, while a sibling subid block must not leak its flag gate or
            // palette into the others.
            var shared = RegionWithoutTargets(region, targets);

            for (var subId = 0; subId < targets.Count; subId++)
            {
                var targetLines = TargetLines(entries, region, targets[subId]);
                var combined = new List<string>(shared);
                combined.AddRange(Expand(entries, topLabels, targetLines));
                _behaviors[(id, subId)] = Analyze(combined);
            }

            var fallback = Analyze(shared);
            for (var subId = targets.Count; subId < 0x100; subId++)
                _behaviors[(id, subId)] = fallback;
        }
    }

    private static bool TryInteractionCode(string line, out int id)
    {
        id = -1;
        if (!line.StartsWith("interactionCode", StringComparison.Ordinal) ||
            line.Length != "interactionCode00:".Length)
            return false;
        return int.TryParse(line["interactionCode".Length..^1],
            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    private static List<string> FindSubIdTable(List<Entry> region)
    {
        for (var i = 0; i < region.Count; i++)
        {
            if (!region[i].Text.Contains("Interaction.subid", StringComparison.Ordinal))
                continue;

            var jump = -1;
            for (var j = i; j < region.Count && j <= i + 8; j++)
            {
                if (region[j].Text.Contains("rst_jumpTable", StringComparison.Ordinal))
                {
                    jump = j;
                    break;
                }
            }
            if (jump < 0)
                continue;

            var targets = new List<string>();
            for (var k = jump + 1; k < region.Count; k++)
            {
                var dw = DwRegex.Match(region[k].Text);
                if (!dw.Success)
                    break;
                targets.Add(dw.Groups["label"].Value);
            }
            if (targets.Count > 0)
                return targets;
        }
        return new List<string>();
    }

    private static List<string> TargetLines(List<Entry> entries, List<Entry> region, string target)
    {
        if (!target.StartsWith("@", StringComparison.Ordinal))
            return TopLines(entries, target);

        var name = target[1..];
        var start = region.FindIndex(entry => entry.Local == name && entry.Text.StartsWith("@", StringComparison.Ordinal));
        if (start < 0)
            return new List<string>();

        var owner = region[start].Top;
        var lines = new List<string>();
        for (var i = start; i < region.Count && region[i].Top == owner; i++)
        {
            if (i > start &&
                (region[i].Text.StartsWith("@", StringComparison.Ordinal) ||
                 (region[i].Local == null && region[i].Text.EndsWith(':'))))
                break;
            lines.Add(region[i].Text);
        }
        return lines;
    }

    private static List<string> TopLines(List<Entry> entries, string label)
    {
        var lines = new List<string>();
        var inside = false;
        foreach (var entry in entries)
        {
            if (entry.Local == null && entry.Text == label + ":")
            {
                inside = true;
                continue;
            }
            if (inside && entry.Local == null && entry.Text.EndsWith(':'))
                break;
            if (inside)
                lines.Add(entry.Text);
        }
        return lines;
    }

    private static List<string> RegionWithoutTargets(List<Entry> region, List<string> targets)
    {
        var topTargets = targets
            .Where(target => !target.StartsWith("@", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var localTargets = targets
            .Where(target => target.StartsWith("@", StringComparison.Ordinal))
            .Select(target => target[1..])
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<string>();
        for (var i = 0; i < region.Count; i++)
        {
            var entry = region[i];
            if (entry.Top != null && topTargets.Contains(entry.Top))
                continue;

            if (entry.Local != null && localTargets.Contains(entry.Local))
            {
                var name = entry.Local;
                while (i < region.Count && region[i].Local == name)
                    i++;
                i--;
                continue;
            }

            result.Add(entry.Text);
        }
        return result;
    }

    private static List<string> Expand(List<Entry> entries, HashSet<string> topLabels,
        List<string> seed, HashSet<string>? excludedTop = null, HashSet<string>? visited = null, int depth = 0)
    {
        var result = new List<string>(seed);
        if (depth >= 4)
            return result;

        visited ??= new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in seed)
        {
            foreach (Match match in LabelRefRegex.Matches(line))
            {
                var name = match.Groups["label"].Value;
                if (!topLabels.Contains(name) || !seen.Add(name) || !visited.Add(name))
                    continue;
                if (excludedTop != null && excludedTop.Contains(name))
                    continue;
                var block = TopLines(entries, name);
                if (block.Count > 0)
                    result.AddRange(Expand(entries, topLabels, block, excludedTop, visited, depth + 1));
            }
        }
        return result;
    }

    private InteractionBehavior Analyze(List<string> lines)
    {
        var solidTile = false;
        var radiusY = 0;
        var radiusX = 0;
        var requiresFlag = -1;
        var forbiddenFlag = -1;
        var palette = -1;
        var timeWarp = false;
        var requiresEntryDirection = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Contains("objectMarkSolidPosition", StringComparison.Ordinal) ||
                line.Contains("wRoomCollisions", StringComparison.Ordinal))
                solidTile = true;

            if (line.Contains("interactionBeginTimewarp", StringComparison.Ordinal))
                timeWarp = true;

            if (requiresEntryDirection < 0 && line.Contains("wScreenTransitionDirection", StringComparison.Ordinal))
            {
                for (var next = i + 1; next < lines.Count && next <= i + 4; next++)
                {
                    var cp = Regex.Match(lines[next], @"cp\s+\$(?<value>[0-9a-fA-F]{1,2})");
                    if (!cp.Success)
                        continue;
                    for (var branch = next + 1; branch < lines.Count && branch <= next + 3; branch++)
                    {
                        if (!lines[branch].Contains("interactionDelete", StringComparison.Ordinal))
                            continue;
                        if (lines[branch].Contains("jp nz", StringComparison.Ordinal) ||
                            lines[branch].Contains("jr nz", StringComparison.Ordinal))
                            requiresEntryDirection = ParseHex(cp.Groups["value"].Value);
                        break;
                    }
                    break;
                }
            }

            if (line.Contains("objectSetCollideRadii", StringComparison.Ordinal))
                (radiusY, radiusX) = ReadRadii(lines, i);

            if (line.Contains("objectSetCollideRadius", StringComparison.Ordinal))
            {
                var radius = ReadRadius(lines, i);
                if (radius > 0)
                    radiusY = radiusX = radius;
            }

            if (line.Contains("loadPaletteHeader", StringComparison.Ordinal))
            {
                for (var back = 1; back <= 5 && i - back >= 0; back++)
                {
                    var match = Regex.Match(lines[i - back], @"PALH_(?<name>[A-Za-z0-9_]+)");
                    if (!match.Success)
                        continue;
                    if (_paletteHeaders.TryGetValue("PALH_" + match.Groups["name"].Value, out var header))
                        palette = header;
                    break;
                }
            }

            if ((requiresFlag < 0 || forbiddenFlag < 0) &&
                line.Contains("checkGlobalFlag", StringComparison.Ordinal))
            {
                for (var back = 1; back <= 3 && i - back >= 0; back++)
                {
                    var match = Regex.Match(lines[i - back], @"GLOBALFLAG_(?<name>[A-Za-z0-9_]+)");
                    if (!match.Success)
                        continue;
                    if (!_globalFlags.TryGetValue("GLOBALFLAG_" + match.Groups["name"].Value, out var flag))
                        break;

                    for (var next = i + 1; next < lines.Count && next <= i + 4; next++)
                    {
                        if (!lines[next].Contains("interactionDelete", StringComparison.Ordinal))
                            continue;
                        if (lines[next].Contains("jp nz", StringComparison.Ordinal) ||
                            lines[next].Contains("jr nz", StringComparison.Ordinal))
                            forbiddenFlag = flag;
                        else if (lines[next].Contains("jp z", StringComparison.Ordinal) ||
                                 lines[next].Contains("jr z", StringComparison.Ordinal))
                            requiresFlag = flag;
                        break;
                    }
                    break;
                }
            }
        }

        return new InteractionBehavior(solidTile, radiusY, radiusX, requiresFlag, forbiddenFlag,
            palette, timeWarp, requiresEntryDirection);
    }

    private static (int Y, int X) ReadRadii(List<string> lines, int index)
    {
        var y = 0;
        var x = 0;
        for (var back = 1; back <= 4 && index - back >= 0; back++)
        {
            var line = lines[index - back];
            var bc = Regex.Match(line, @"ld\s+bc\s*,\s*\$([0-9a-fA-F]{1,4})");
            if (bc.Success)
            {
                var value = ParseHex(bc.Groups[1].Value);
                return ((value >> 8) & 0xFF, value & 0xFF);
            }
            var b = Regex.Match(line, @"ld\s+b\s*,\s*\$([0-9a-fA-F]{1,2})");
            if (b.Success)
                y = ParseHex(b.Groups[1].Value);
            var c = Regex.Match(line, @"ld\s+c\s*,\s*\$([0-9a-fA-F]{1,2})");
            if (c.Success)
                x = ParseHex(c.Groups[1].Value);
        }
        return (y, x);
    }

    private static int ReadRadius(List<string> lines, int index)
    {
        for (var back = 1; back <= 4 && index - back >= 0; back++)
        {
            var line = lines[index - back];
            var a = Regex.Match(line, @"ld\s+a\s*,\s*\$([0-9a-fA-F]{1,2})");
            if (a.Success)
                return ParseHex(a.Groups[1].Value);
            var bc = Regex.Match(line, @"ld\s+bc\s*,\s*\$([0-9a-fA-F]{1,2})");
            if (bc.Success)
                return ParseHex(bc.Groups[1].Value);
        }
        return 0;
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line[..index] : line;
    }

    private static int ParseHex(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
