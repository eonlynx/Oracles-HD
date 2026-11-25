using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public sealed record SingleTileChange(
    int Group, int Room, int Mask, int Position, int Tile, string Source);

/// <summary>
/// Reads singleTileChanges.s (applySingleTileChanges). These writes happen on
/// room load, selected by a room-flag mask or the Ages $f0/$f1/$f2 game-state
/// predicates. Runtime application is data-driven; no room is special-cased.
/// </summary>
public sealed class OracleTileChanges
{
    private static readonly Regex GroupLabel = new(
        @"^singleTileChangeGroup(?<group>[0-7])Data:\s*$", RegexOptions.Compiled);
    private static readonly Regex RowRegex = new(
        @"^\.db\s+\$(?<room>[0-9a-fA-F]{2})\s+\$(?<mask>[0-9a-fA-F]{2})\s+\$(?<position>[0-9a-fA-F]{2})\s+\$(?<tile>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);

    private readonly List<SingleTileChange> _changes = new();

    public IReadOnlyList<SingleTileChange> Changes => _changes;

    public OracleTileChanges(OracleRepo repo)
    {
        var path = Path.Combine(repo.DataDir, "singleTileChanges.s");
        if (!File.Exists(path))
            return;

        var group = -1;
        var lineNumber = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNumber++;
            var line = StripComment(raw).Trim();
            var label = GroupLabel.Match(line);
            if (label.Success)
            {
                group = ParseHex(label.Groups["group"].Value);
                continue;
            }
            if (group < 0)
                continue;

            var row = RowRegex.Match(line);
            if (!row.Success)
                continue;
            _changes.Add(new SingleTileChange(group,
                ParseHex(row.Groups["room"].Value),
                ParseHex(row.Groups["mask"].Value),
                ParseHex(row.Groups["position"].Value),
                ParseHex(row.Groups["tile"].Value),
                $"singleTileChanges.s:{lineNumber}"));
        }
    }

    public static string RawTablePath(string mapsOutDir, string game) =>
        Path.Combine(mapsOutDir, "TileChanges", $"oracle_{game}_tile_changes.tsv");

    public static string BakedTablePath(string mapsOutDir, string game, int group) =>
        Path.Combine(mapsOutDir, "TileChanges",
            $"oracle_{game}_g{group}_tile_changes.tsv");

    /// <summary>
    /// Writes all source rows, including groups that are not currently baked
    /// into a map. Baked per-group tables carry the resolved atlas indices.
    /// </summary>
    public void WriteRawTable(OracleRepo repo, string mapsOutDir)
    {
        var path = RawTablePath(mapsOutDir, repo.Game);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var builder = new StringBuilder();
        builder.Append("group\troom\tmask\tposition\ttile\tsource\n");
        foreach (var change in _changes.OrderBy(c => c.Group).ThenBy(c => c.Room)
                     .ThenBy(c => c.Position))
            builder.Append($"{change.Group}\t0x{change.Room:x2}\t0x{change.Mask:x2}\t")
                .Append($"0x{change.Position:x2}\t0x{change.Tile:x2}\t{change.Source}\n");

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));

        var sourcePath = Path.Combine(repo.DataDir, "singleTileChanges.s");
        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "single_tile_changes",
            rows = _changes.Count,
            inputs = new[]
            {
                new
                {
                    path = Path.GetRelativePath(repo.Root, sourcePath).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(sourcePath),
                },
            },
            outputs = new[]
            {
                new
                {
                    path = Path.GetRelativePath(mapsOutDir, path).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(path),
                },
            },
        };
        File.WriteAllText(Path.ChangeExtension(path, ".manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static int ParseHex(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line[..index] : line;
    }
}
