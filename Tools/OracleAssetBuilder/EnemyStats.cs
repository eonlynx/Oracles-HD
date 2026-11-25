using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public readonly record struct EnemyStats(
    int Id, int SubId, int RadiusY, int RadiusX,
    int Damage, int Health, int Speed, int CounterMask, string Source);

public readonly record struct PartStats(
    int Id, int GfxHeader, int RadiusY, int RadiusX, int Damage,
    int TileBase, int Palette, int Speed, int Lifetime, string Source);

/// <summary>
/// Parses the native enemyData/extraEnemyData tables and the Octorok
/// speed/counter contract. The table is source-cited and never keyed by room.
/// </summary>
public sealed class EnemyStatsTable
{
    private readonly record struct ExtraRow(int RadiusY, int RadiusX, int RawDamage, int Health);
    private sealed record EnemyRow(int Gfx, int Collision, string? SubidTable, int ExtraOrFlags);

    private static readonly Regex EnemyRegex = new(
        @"^/\*\s*0x(?<id>[0-9a-fA-F]{2})\s*\*/\s*m_EnemyData\s+(?<args>[^;]+)", RegexOptions.Compiled);
    private static readonly Regex ExtraRegex = new(
        @"^\.db\s+\$(?<ry>[0-9a-fA-F]{2})\s+\$(?<rx>[0-9a-fA-F]{2})\s+\$(?<damage>[0-9a-fA-F]{2})\s+\$(?<health>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);
    private static readonly Regex SubidRegex = new(
        @"^m_EnemySubidData\s+\$(?<extra>[0-9a-fA-F]{2})\s+\$(?<flags>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);
    private static readonly Regex PartRegex = new(
        @"^\.db\s+\$(?<gfx>[0-9a-fA-F]{2})\s+\$(?<coll>[0-9a-fA-F]{2})\s+" +
        @"\$(?<radius>[0-9a-fA-F]{2})\s+\$(?<damage>[0-9a-fA-F]{2})\s+" +
        @"\$(?<health>[0-9a-fA-F]{2})\s+\$(?<tile>[0-9a-fA-F]{2})\s+" +
        @"\$(?<flags>[0-9a-fA-F]{2})\s+\$(?<pad>[0-9a-fA-F]{2})\s*;\s*" +
        @"\$(?<id>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);

    private readonly Dictionary<int, EnemyRow> _enemies = new();
    private readonly Dictionary<string, List<(int Extra, int Flags)>> _subids = new(StringComparer.Ordinal);
    private readonly List<ExtraRow> _extra = new();
    private readonly Dictionary<(int Id, int SubId), EnemyStats> _stats = new();
    private readonly Dictionary<int, PartStats> _parts = new();

    public EnemyStatsTable(OracleRepo repo)
    {
        var path = Path.Combine(repo.DataDir, "enemyData.s");
        if (!File.Exists(path))
            return;

        var section = "";
        var pendingSubid = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("extraEnemyData:", StringComparison.Ordinal))
            {
                section = "extra";
                pendingSubid = "";
                continue;
            }
            if (line.StartsWith("enemyData:", StringComparison.Ordinal))
            {
                section = "enemy";
                pendingSubid = "";
                continue;
            }
            if (line.StartsWith("enemy0", StringComparison.Ordinal) &&
                line.EndsWith("SubidData:", StringComparison.Ordinal))
            {
                section = "subid";
                pendingSubid = line[..^1];
                _subids.TryAdd(pendingSubid, new List<(int, int)>());
                continue;
            }
            if (line.StartsWith("m_EnemySubidDataEnd", StringComparison.Ordinal))
            {
                section = "enemy";
                pendingSubid = "";
                continue;
            }
            if (section == "extra")
            {
                var extra = ExtraRegex.Match(line);
                if (extra.Success)
                    _extra.Add(new ExtraRow(Hex(extra, "ry"), Hex(extra, "rx"),
                        Hex(extra, "damage"), Hex(extra, "health")));
                continue;
            }

            if (section == "subid" && _subids.TryGetValue(pendingSubid, out var rows))
            {
                var subid = SubidRegex.Match(line);
                if (subid.Success)
                    rows.Add((Hex(subid, "extra"), Hex(subid, "flags")));
                continue;
            }

            var enemy = EnemyRegex.Match(line);
            if (!enemy.Success)
                continue;
            var id = ParseHex(enemy.Groups["id"].Value);
            var values = Regex.Matches(enemy.Groups["args"].Value, @"\$[0-9a-fA-F]{1,2}|[A-Za-z_][A-Za-z0-9_]*")
                .Select(match => match.Value).ToArray();
            if (values.Length >= 4)
            {
                _enemies[id] = new EnemyRow(ParseHex(values[0]), ParseHex(values[1]), null,
                    ParseHex(values[2]));
            }
            else if (values.Length >= 3)
            {
                _enemies[id] = new EnemyRow(ParseHex(values[0]), ParseHex(values[1]),
                    values[2].Trim(), 0);
            }
            else if (values.Length == 2)
            {
                _enemies[id] = new EnemyRow(ParseHex(values[0]), ParseHex(values[1]),
                    values[1].TrimStart('$'), 0);
            }
        }

        // Octorok is the first supported host. Subids 0/1/2 use the native
        // SPEED_80/SPEED_c0 and counter ranges from octorok.s.
        for (var subid = 0; subid <= 2; subid++)
        {
            if (!TryResolveExtra(0x09, subid, out var extra))
                continue;
            var speed = (subid & 1) != 0 ? 0x1e : 0x14;
            var counterMask = subid < 2 ? 7 : 3;
            var damage = extra.RawDamage >= 0x80
                ? (0x100 - extra.RawDamage) / 2
                : extra.RawDamage / 2;
            _stats[(0x09, subid)] = new EnemyStats(0x09, subid, extra.RadiusY,
                extra.RadiusX, damage, extra.Health, speed, counterMask,
                "enemyData.s:enemy09SubidData; extraEnemyData.s; octorok.s:@counter1Ranges");
        }

        ParseParts(repo);
    }

    public bool TryGet(int id, int subId, out EnemyStats stats) =>
        _stats.TryGetValue((id, subId), out stats);

    public bool TryGetPart(int id, out PartStats stats) =>
        _parts.TryGetValue(id, out stats);

    private void ParseParts(OracleRepo repo)
    {
        var partPath = Path.Combine(repo.DataDir, "partData.s");
        if (!File.Exists(partPath))
            return;

        // PART18 (Octorok projectile) uses the Octorok sprite header and owns
        // its speed/lifetime counter in octorokProjectile.s state 0.
        var shotSpeed = 0;
        var shotPath = Path.Combine(repo.Root,
            "object_code/common/parts/octorokProjectile.s");
        if (File.Exists(shotPath))
        {
            var text = File.ReadAllText(shotPath);
            var state = Regex.Match(text, @"@state0:(?<body>[\s\S]*?)@state1:");
            if (state.Success)
            {
                var set = Regex.Match(state.Groups["body"].Value,
                    @"ld\s+\(hl\),\$(?<value>[0-9a-fA-F]{2})");
                if (set.Success)
                    shotSpeed = ParseHex(set.Groups["value"].Value);
            }
        }

        foreach (var raw in File.ReadLines(partPath))
        {
            var match = PartRegex.Match(raw.Trim());
            if (!match.Success)
                continue;

            var id = Hex(match, "id");
            var radius = Hex(match, "radius");
            var rawDamage = Hex(match, "damage");
            var damage = rawDamage >= 0x80 ? (0x100 - rawDamage) / 2 : rawDamage / 2;
            var isOctorokShot = id == 0x18;
            _parts[id] = new PartStats(id, Hex(match, "gfx"),
                radius >> 4, radius & 0x0f, damage,
                Hex(match, "tile"), Hex(match, "flags") & 0x07,
                isOctorokShot ? shotSpeed : 0,
                isOctorokShot ? shotSpeed : 0,
                isOctorokShot
                    ? "partData.s:$18; octorokProjectile.s:@state0"
                    : "partData.s");
        }
    }

    public void WritePartTable(OracleRepo repo, string mapsOutDir)
    {
        if (_parts.Count == 0)
            return;

        var directory = EntityAtlas.ResolveMapObjectsDir(mapsOutDir);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"oracle_{repo.Game}_part_stats.tsv");
        var builder = new System.Text.StringBuilder();
        builder.Append("id\tgfx\tradius_y\tradius_x\tdamage\ttile_base\tpalette\tspeed\tlifetime\tsource\n");
        foreach (var part in _parts.Values.OrderBy(value => value.Id))
            builder.Append($"0x{part.Id:x2}\t0x{part.GfxHeader:x2}\t{part.RadiusY}\t")
                .Append($"{part.RadiusX}\t{part.Damage}\t{part.TileBase}\t{part.Palette}\t")
                .Append($"{part.Speed}\t{part.Lifetime}\t{part.Source}\n");
        File.WriteAllText(path, builder.ToString(), new System.Text.UTF8Encoding(false));

        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "part_stats",
            rows = _parts.Count,
            inputs = new[]
            {
                new { path = $"data/{repo.Game}/partData.s",
                    sha256 = OracleRepo.Sha256(Path.Combine(repo.DataDir, "partData.s")) },
            },
            outputs = new[]
            {
                new { path = Path.GetRelativePath(mapsOutDir, path).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(path) },
            },
        };
        File.WriteAllText(Path.ChangeExtension(path, ".manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
            new System.Text.UTF8Encoding(false));
    }

    public void WriteTable(OracleRepo repo, string mapsOutDir)
    {
        var directory = EntityAtlas.ResolveMapObjectsDir(mapsOutDir);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"oracle_{repo.Game}_enemy_stats.tsv");
        var builder = new System.Text.StringBuilder();
        builder.Append("id\tsubid\tradius_y\tradius_x\tdamage\thealth\tspeed\tcounter_mask\tsource\n");
        foreach (var stats in _stats.Values.OrderBy(value => value.Id).ThenBy(value => value.SubId))
            builder.Append($"0x{stats.Id:x2}\t0x{stats.SubId:x2}\t{stats.RadiusY}\t")
                .Append($"{stats.RadiusX}\t{stats.Damage}\t{stats.Health}\t{stats.Speed}\t")
                .Append($"{stats.CounterMask}\t{stats.Source}\n");
        File.WriteAllText(path, builder.ToString(), new System.Text.UTF8Encoding(false));

        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "enemy_stats",
            rows = _stats.Count,
            inputs = new[]
            {
                new { path = "data/ages/enemyData.s", sha256 = OracleRepo.Sha256(Path.Combine(repo.DataDir, "enemyData.s")) },
            },
            outputs = new[]
            {
                new { path = Path.GetRelativePath(mapsOutDir, path).Replace('\\', '/'), sha256 = OracleRepo.Sha256(path) },
            },
        };
        File.WriteAllText(Path.ChangeExtension(path, ".manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
            new System.Text.UTF8Encoding(false));
    }

    private bool TryResolveExtra(int id, int subid, out ExtraRow extra)
    {
        extra = default;
        if (!_enemies.TryGetValue(id, out var enemy))
            return false;
        var extraIndex = enemy.ExtraOrFlags & 0x7f;
        if (enemy.SubidTable != null && _subids.TryGetValue(enemy.SubidTable, out var rows) &&
            rows.Count > 0)
            extraIndex = rows[Math.Min(subid, rows.Count - 1)].Extra & 0x7f;
        if (extraIndex < 0 || extraIndex >= _extra.Count)
            return false;
        extra = _extra[extraIndex];
        return true;
    }

    private static int Hex(Match match, string name) => ParseHex(match.Groups[name].Value);
    private static int ParseHex(string value) =>
        int.Parse(value.TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
