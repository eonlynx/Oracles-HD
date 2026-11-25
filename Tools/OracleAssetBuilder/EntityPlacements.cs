using System.Text;
using System.Text.Json;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Emits every deterministic enemy/part/item placement from the native object
/// streams. Random enemies are recorded but marked, so the runtime can keep
/// them behind its own RNG policy instead of inventing positions.
/// </summary>
public static class EntityPlacements
{
    public static string TablePath(string mapsOutDir, string game)
    {
        return Path.Combine(mapsOutDir, "Entities", $"oracle_{game}_entity_placements.tsv");
    }

    public static void Build(OracleRepo repo, RoomObjectStreams streams, string mapsOutDir,
        out IReadOnlyList<string> warnings)
    {
        var path = TablePath(mapsOutDir, repo.Game);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new StringBuilder();
        builder.Append("group\troom\tkind\tid\tsubid\tx\ty\tvar03\trandom\tcondition_mask\troom_flag_mask\troom_flag_value\tsource\n");
        var rows = 0;
        var issues = new List<string>();

        for (var group = 0; group <= 5; group++)
            for (var room = 0; room < 256; room++)
            {
                var data = streams.GetRoom(group, room);
                foreach (var enemy in data.Enemies)
                {
                    if (!enemy.Random && (enemy.X < 0 || enemy.Y < 0))
                    {
                        issues.Add($"{enemy.Source}: specific enemy has no position");
                        continue;
                    }
                    builder.Append($"{group}\t0x{room:x2}\tenemy\t0x{enemy.Id:x2}\t0x{enemy.SubId:x2}\t");
                    builder.Append($"{enemy.X}\t{enemy.Y}\t{enemy.Var03}\t{(enemy.Random ? 1 : 0)}\t");
                    builder.Append($"{enemy.ConditionMask}\t{enemy.RoomFlagMask}\t{enemy.RoomFlagValue}\t");
                    builder.Append(enemy.Source).Append('\n');
                    rows++;
                }

                foreach (var part in data.Parts)
                {
                    builder.Append($"{group}\t0x{room:x2}\tpart\t0x{part.Id:x2}\t0x{part.SubId:x2}\t");
                    builder.Append($"{part.X}\t{part.Y}\t{part.Var03}\t0\t");
                    builder.Append($"{part.ConditionMask}\t{part.RoomFlagMask}\t{part.RoomFlagValue}\t");
                    builder.Append(part.Source).Append('\n');
                    rows++;
                }

                foreach (var item in data.Items)
                {
                    builder.Append($"{group}\t0x{room:x2}\titem\t0x{item.Item:x2}\t0x00\t");
                    builder.Append($"{item.X}\t{item.Y}\t{item.Flags}\t0\t");
                    builder.Append($"{item.ConditionMask}\t{item.RoomFlagMask}\t{item.RoomFlagValue}\t");
                    builder.Append(item.Source).Append('\n');
                    rows++;
                }
            }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        var manifestPath = Path.ChangeExtension(path, ".manifest.json");
        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "entity_placements",
            rows,
            inputs = new[]
            {
                Hash(repo, Path.Combine(repo.Root, "objects", repo.Game, "mainData.s")),
                Hash(repo, Path.Combine(repo.Root, "objects", repo.Game, "enemyData.s")),
            },
            outputs = new[] { Hash(repo, path) },
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));

        warnings = issues;
    }

    private static object Hash(OracleRepo repo, string path) => new
    {
        path = Path.GetRelativePath(repo.Root, path).Replace('\\', '/'),
        sha256 = File.Exists(path) ? OracleRepo.Sha256(path) : "",
    };
}
