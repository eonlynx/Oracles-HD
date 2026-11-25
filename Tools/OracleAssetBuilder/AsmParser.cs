using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public sealed record GfxEntry(string Name, int Destination);
public sealed record PaletteEntry(int Start, int Count, string DataLabel);
public readonly record struct Rgb555(byte R, byte G, byte B);

public sealed record TilesetInfo(
    int Index,
    byte CollisionModeAndDungeon,
    byte Flags,
    int UniqueGfx,
    int Gfx,
    int Palette,
    int Layout,
    int LayoutGroup,
    int Animation)
{
    public bool IsOutdoors => (Flags & 0x01) != 0;
    public bool IsMaku => (Flags & 0x02) != 0;
    public bool IsIndoors => (Flags & 0x04) != 0;
    public bool IsDungeon => (Flags & 0x08) != 0;
    public bool IsSideScroll => (Flags & 0x20) != 0;
    public bool IsUnderwater => (Flags & 0x40) != 0;
    public bool IsPastOrSubrosia => (Flags & 0x80) != 0;
}

/// <summary>
/// Parsers for the small, stable subset of oracles-disasm tables this tool needs.
/// Formats are documented in oracles-disasm itself and in top/docs/ROM_DATA_FORMATS.md.
/// </summary>
public static class AsmParser
{
    private static readonly Regex IndexedStart = new(
        @"^\s*(?<macro>m_(?:Gfx|UniqueGfx|Palette)HeaderStart)\s+\$(?<index>[0-9A-Fa-f]+)\s*,\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex GfxEntryRegex = new(
        @"^\s*m_GfxHeader\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*\$(?<dest>[0-9A-Fa-f]+)",
        RegexOptions.Compiled);

    private static readonly Regex PaletteBgRegex = new(
        @"^\s*m_PaletteHeaderBg\s+(?<start>\d+)\s*,\s*(?<count>\d+)\s*,\s*(?<label>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex PaletteSprRegex = new(
        @"^\s*m_PaletteHeaderSpr\s+(?<start>\d+)\s*,\s*(?<count>\d+)\s*,\s*(?<label>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex LabelRegex = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):",
        RegexOptions.Compiled);

    private static readonly Regex Rgb16Regex = new(
        @"m_RGB16\s+\$(?<r>[0-9A-Fa-f]{1,2})\s+\$(?<g>[0-9A-Fa-f]{1,2})\s+\$(?<b>[0-9A-Fa-f]{1,2})",
        RegexOptions.Compiled);

    public static Dictionary<string, int> ParseHeaderStartIndices(string path, string macro)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var match = IndexedStart.Match(line);
            if (!match.Success || match.Groups["macro"].Value != macro)
                continue;

            var name = match.Groups["name"].Value;
            var index = ParseHexCapture(match.Groups["index"].Value);
            if (result.TryGetValue(name, out var existing) && existing != index)
                throw new InvalidDataException($"{path}: header '{name}' is declared with both ${existing:x2} and ${index:x2}.");
            result[name] = index;
        }
        return result;
    }

    public static IReadOnlyList<GfxEntry> ParseGfxHeader(string path, int index)
    {
        var entries = new List<GfxEntry>();
        var inside = false;
        foreach (var line in File.ReadLines(path))
        {
            var start = IndexedStart.Match(line);
            if (start.Success &&
                start.Groups["macro"].Value is "m_GfxHeaderStart" or "m_UniqueGfxHeaderStart")
            {
                inside = ParseHexCapture(start.Groups["index"].Value) == index;
                continue;
            }

            if (!inside)
                continue;

            if (line.Contains("m_GfxHeaderEnd", StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            var entry = GfxEntryRegex.Match(line);
            if (entry.Success)
                entries.Add(new GfxEntry(entry.Groups["name"].Value, ParseHexCapture(entry.Groups["dest"].Value)));
        }

        if (entries.Count == 0)
            throw new InvalidDataException($"{path}: gfx header ${index:x2} has no entries (or does not exist).");
        return entries;
    }

    public static IReadOnlyList<PaletteEntry> ParsePaletteHeader(string path, int index)
    {
        var entries = new List<PaletteEntry>();
        var inside = false;
        foreach (var line in File.ReadLines(path))
        {
            var start = IndexedStart.Match(line);
            if (start.Success && start.Groups["macro"].Value == "m_PaletteHeaderStart")
            {
                inside = ParseHexCapture(start.Groups["index"].Value) == index;
                continue;
            }

            if (!inside)
                continue;

            if (line.Contains("m_PaletteHeaderEnd", StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            var entry = PaletteBgRegex.Match(line);
            if (entry.Success)
            {
                entries.Add(new PaletteEntry(
                    int.Parse(entry.Groups["start"].Value, CultureInfo.InvariantCulture),
                    int.Parse(entry.Groups["count"].Value, CultureInfo.InvariantCulture),
                    entry.Groups["label"].Value));
            }
        }

        if (entries.Count == 0)
            throw new InvalidDataException($"{path}: palette header ${index:x2} has no background entries (or does not exist).");
        return entries;
    }

    /// <summary>Sprite-palette entries (`m_PaletteHeaderSpr`) for one header.</summary>
    public static IReadOnlyList<PaletteEntry> ParsePaletteHeaderSpr(string path, int index)
    {
        var entries = new List<PaletteEntry>();
        var inside = false;
        foreach (var line in File.ReadLines(path))
        {
            var start = IndexedStart.Match(line);
            if (start.Success && start.Groups["macro"].Value == "m_PaletteHeaderStart")
            {
                inside = ParseHexCapture(start.Groups["index"].Value) == index;
                continue;
            }

            if (!inside)
                continue;

            if (line.Contains("m_PaletteHeaderEnd", StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            var entry = PaletteSprRegex.Match(line);
            if (entry.Success)
            {
                entries.Add(new PaletteEntry(
                    int.Parse(entry.Groups["start"].Value, CultureInfo.InvariantCulture),
                    int.Parse(entry.Groups["count"].Value, CultureInfo.InvariantCulture),
                    entry.Groups["label"].Value));
            }
        }

        return entries;
    }

    /// <summary>
    /// Maps a tileset layout index (tileset byte 5) to the mapping and collision
    /// labels named by tilesetLayoutGroupXX. Several layout indices alias the same
    /// data (for example layout $20 uses tilesetMappings1b).
    /// </summary>
    public static Dictionary<int, (string Mappings, string Collisions)> ParseLayoutHeaders(string path)
    {
        var result = new Dictionary<int, (string, string)>();
        var mappingsRegex = new Regex(@"m_TilesetLayoutHeader\s+\$\w+\s+(?<label>tilesetMappings\w+)\s+w3TileMappingIndices", RegexOptions.Compiled);
        var collisionsRegex = new Regex(@"m_TilesetLayoutHeader\s+\$\w+\s+(?<label>tilesetCollisions\w+)\s+w3TileCollisions", RegexOptions.Compiled);
        var groupRegex = new Regex(@"^tilesetLayoutGroup(?<index>[0-9a-fA-F]{2}):", RegexOptions.Compiled);

        int? current = null;
        string? mappings = null;
        string? collisions = null;

        void Flush()
        {
            if (current is { } index && mappings != null && collisions != null)
                result[index] = (mappings, collisions);
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw);
            if (line.Length == 0)
                continue;

            var group = groupRegex.Match(line);
            if (group.Success)
            {
                Flush();
                current = ParseHexCapture(group.Groups["index"].Value);
                mappings = null;
                collisions = null;
                continue;
            }

            if (current is null)
                continue;

            var mapping = mappingsRegex.Match(line);
            if (mapping.Success)
                mappings = mapping.Groups["label"].Value;

            var collision = collisionsRegex.Match(line);
            if (collision.Success)
                collisions = collision.Groups["label"].Value;
        }

        Flush();
        return result;
    }

    public static Dictionary<string, Rgb555[]> ParsePaletteData(string path)
    {
        var result = new Dictionary<string, Rgb555[]>(StringComparer.Ordinal);
        string? current = null;
        var colors = new List<Rgb555>();

        void Flush()
        {
            if (current != null && colors.Count > 0)
                result.TryAdd(current, colors.ToArray());
            colors.Clear();
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw);
            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                Flush();
                current = label.Groups["label"].Value;
                continue;
            }

            foreach (Match match in Rgb16Regex.Matches(line))
            {
                colors.Add(new Rgb555(
                    (byte)ParseHexCapture(match.Groups["r"].Value),
                    (byte)ParseHexCapture(match.Groups["g"].Value),
                    (byte)ParseHexCapture(match.Groups["b"].Value)));
            }
        }

        Flush();
        return result;
    }

    public static IReadOnlyList<TilesetInfo> ParseTilesetData(string path, IReadOnlyDictionary<string, int> symbols)
    {
        var result = new List<TilesetInfo>();
        var values = new List<int>();
        var inside = false;

        void Flush()
        {
            if (values.Count == 0)
                return;

            if (values.Count % 8 != 0)
                throw new InvalidDataException($"{path}: tileset entry has {values.Count % 8} trailing values (expected 8 bytes per entry).");

            for (var i = 0; i < values.Count; i += 8)
            {
                var b = values.GetRange(i, 8);
                result.Add(new TilesetInfo(
                    result.Count,
                    (byte)b[0], (byte)b[1], b[2], b[3], b[4], b[5], b[6], b[7]));
            }
            values.Clear();
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = StripComment(raw);
            if (line.Length == 0)
                continue;

            if (!inside)
            {
                if (line.TrimEnd() == "tilesetData:")
                    inside = true;
                continue;
            }

            if (LabelRegex.IsMatch(line))
            {
                Flush();
                break;
            }

            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(".db", StringComparison.Ordinal))
                continue;

            var payload = trimmed[3..];
            foreach (var token in payload.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                values.Add(ResolveValue(token, symbols, path));
        }

        Flush();
        if (result.Count == 0)
            throw new InvalidDataException($"{path}: tilesetData table is empty.");
        return result;
    }

    private static int ResolveValue(string token, IReadOnlyDictionary<string, int> symbols, string path)
    {
        token = token.Trim();
        if (token.StartsWith('$'))
            return int.Parse(token[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.Parse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return number;
        if (symbols.TryGetValue(token, out var value))
            return value;

        throw new InvalidDataException($"{path}: cannot resolve tileset data symbol '{token}'.");
    }

    private static int ParseHexCapture(string value)
    {
        return int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line[..index] : line;
    }
}
