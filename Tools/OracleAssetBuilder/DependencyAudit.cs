using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Exhaustive reference audit for the source tree. This is deliberately a
/// scanner, not a semantic reimplementation: every WRAM/global-flag/room-flag
/// reference and every object-stream condition is exported with its entity
/// scope and source line, so the runtime rule tables can be reviewed against
/// the complete set instead of discovered one object at a time.
/// </summary>
public static class DependencyAudit
{
    private static readonly Regex EntityLabel = new(
        @"^(?<kind>interaction|enemy|part|specialObject)Code(?<id>[0-9a-fA-F]{2})(?:_body)?:\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SubidLabel = new(
        @"^@(?<label>initSubid|subid|state|substate)(?<id>[0-9a-fA-F]+)[A-Za-z0-9_]*:\s*$",
        RegexOptions.Compiled);
    private static readonly Regex TopLabel = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Compiled);
    private static readonly Regex Symbol = new(
        @"\b(?<symbol>GLOBALFLAG_[A-Za-z0-9_]+|w[A-Za-z0-9_]+|h[A-Za-z0-9_]+)\b",
        RegexOptions.Compiled);
    private static readonly Regex GlobalFlagDefinition = new(
        @"^\s*(?<name>GLOBALFLAG_[A-Za-z0-9_]+)\s+db\s*;\s*\$(?<value>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);
    private static readonly Regex Define = new(
        @"^\s*\.define\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+\$(?<value>[0-9a-fA-F]+)",
        RegexOptions.Compiled);
    private static readonly Regex ObjectEntry = new(
        @"^\s*obj_(?<kind>Interaction|RandomEnemy|SpecificEnemyA|SpecificEnemyB|Part|ItemDrop|Condition|BeforeEvent|AfterEvent)\s*(?<args>.*)$",
        RegexOptions.Compiled);

    public static string EntityPath(string mapsOutDir, string game) =>
        Path.Combine(mapsOutDir, "Audit", $"oracle_{game}_entity_conditions.tsv");

    public static string SymbolPath(string mapsOutDir, string game) =>
        Path.Combine(mapsOutDir, "Audit", $"oracle_{game}_symbols.tsv");

    public static void Build(OracleRepo repo, string mapsOutDir, out IReadOnlyList<string> warnings)
    {
        var issues = new List<string>();
        var directory = Path.Combine(mapsOutDir, "Audit");
        Directory.CreateDirectory(directory);
        var entityPath = EntityPath(mapsOutDir, repo.Game);
        var symbolPath = SymbolPath(mapsOutDir, repo.Game);

        var flags = LoadGlobalFlags(repo);
        var symbols = LoadSymbols(repo);
        var rows = new List<string>
        {
            "kind\tid\tsubid\tscope\tsymbol\tvalue\toperation\tsource\tline"
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Files(repo))
        {
            var relative = Path.GetRelativePath(repo.Root, file).Replace('\\', '/');
            var kind = "source";
            var id = "";
            var subid = "";
            var scope = "";
            var lineNumber = 0;
            foreach (var raw in File.ReadAllLines(file))
            {
                lineNumber++;
                var line = StripComment(raw).TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entity = EntityLabel.Match(line);
                if (entity.Success)
                {
                    kind = entity.Groups["kind"].Value;
                    id = $"0x{entity.Groups["id"].Value:x2}";
                    subid = "";
                    scope = line.TrimEnd(':');
                    continue;
                }

                var sub = SubidLabel.Match(line);
                if (sub.Success)
                {
                    subid = $"0x{sub.Groups["id"].Value:x2}";
                    scope = sub.Groups["label"].Value;
                }
                else
                {
                    var top = TopLabel.Match(line);
                    if (top.Success && !line.StartsWith('@'))
                        scope = top.Groups["label"].Value;
                }

                var objectEntry = ObjectEntry.Match(line);
                if (objectEntry.Success)
                {
                    var operation = objectEntry.Groups["kind"].Value;
                    var args = objectEntry.Groups["args"].Value.Trim();
                    var entryId = id;
                    var entrySubid = subid;
                    if (operation is "Interaction" or "RandomEnemy" or "SpecificEnemyA" or "SpecificEnemyB" or "Part" or "ItemDrop")
                    {
                        var values = ParseHexArguments(args);
                        if (values.Count >= 2)
                        {
                            entryId = $"0x{values[0]:x2}";
                            entrySubid = $"0x{values[1]:x2}";
                        }
                    }
                    var key = $"object\t{entryId}\t{entrySubid}\t{relative}\t{lineNumber}";
                    if (seen.Add(key))
                        rows.Add($"object\t{entryId}\t{entrySubid}\tstream\t-\t{args}\t{operation}\t{relative}\t{lineNumber}");
                    continue;
                }

                foreach (Match match in Symbol.Matches(line))
                {
                    var symbol = match.Groups["symbol"].Value;
                    if (!symbols.ContainsKey(symbol) && !flags.ContainsKey(symbol))
                        continue;
                    var value = flags.TryGetValue(symbol, out var flagValue)
                        ? $"0x{flagValue:x2}"
                        : symbols.TryGetValue(symbol, out var address) ? address : "-";
                    var operation = OperationFor(line, symbol);
                    var rowKind = symbol.StartsWith("GLOBALFLAG_", StringComparison.Ordinal)
                        ? "flag" : "wram";
                    var key = $"{rowKind}\t{id}\t{subid}\t{relative}\t{lineNumber}\t{symbol}\t{operation}";
                    if (seen.Add(key))
                        rows.Add($"{rowKind}\t{id}\t{subid}\t{scope}\t{symbol}\t{value}\t{operation}\t{relative}\t{lineNumber}");
                }
            }
        }

        File.WriteAllText(entityPath, string.Join('\n', rows) + "\n", new UTF8Encoding(false));

        var symbolRows = new List<string> { "symbol\tvalue\tsource" };
        foreach (var pair in flags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            symbolRows.Add($"{pair.Key}\t0x{pair.Value:x2}\tconstants/common/globalFlags.s");
        foreach (var pair in symbols.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            symbolRows.Add($"{pair.Key}\t{pair.Value}\tconstants/*.s");
        File.WriteAllText(symbolPath, string.Join('\n', symbolRows) + "\n", new UTF8Encoding(false));

        var manifestPath = Path.Combine(directory, $"oracle_{repo.Game}_audit.manifest.json");
        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "dependency_audit",
            rows = rows.Count - 1,
            symbols = symbolRows.Count - 1,
            inputs = Files(repo).Select(path => new
            {
                path = Path.GetRelativePath(repo.Root, path).Replace('\\', '/'),
                sha256 = OracleRepo.Sha256(path),
            }).ToArray(),
            outputs = new[]
            {
                new
                {
                    path = Path.GetRelativePath(mapsOutDir, entityPath).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(entityPath),
                },
                new
                {
                    path = Path.GetRelativePath(mapsOutDir, symbolPath).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(symbolPath),
                },
            },
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
        warnings = issues;
    }

    private static IEnumerable<string> Files(OracleRepo repo)
    {
        foreach (var root in new[]
                 {
                     Path.Combine(repo.Root, "object_code"),
                     Path.Combine(repo.Root, "objects"),
                     Path.Combine(repo.Root, "scripts"),
                     Path.Combine(repo.Root, "code"),
                 })
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.s", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
                yield return file;
        }
    }

    private static Dictionary<string, int> LoadGlobalFlags(OracleRepo repo)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = Path.Combine(repo.Root, "constants", "common", "globalFlags.s");
        if (!File.Exists(path))
            return result;
        var active = true;
        var stack = new Stack<(bool Parent, bool Cond)>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith(".ifdef ", StringComparison.Ordinal))
            {
                var token = line[".ifdef ".Length..].Trim();
                var cond = token == "ROM_AGES" ? repo.Game == "ages" :
                    token == "ROM_SEASONS" ? repo.Game == "seasons" : false;
                stack.Push((active, cond));
                active &= cond;
                continue;
            }
            if (line.StartsWith(".ifndef ", StringComparison.Ordinal))
            {
                var token = line[".ifndef ".Length..].Trim();
                var cond = token == "ROM_AGES" ? repo.Game != "ages" :
                    token == "ROM_SEASONS" ? repo.Game != "seasons" : true;
                stack.Push((active, cond));
                active &= cond;
                continue;
            }
            if (line.StartsWith(".else", StringComparison.Ordinal) && stack.Count > 0)
            {
                var (parent, cond) = stack.Pop();
                stack.Push((parent, !cond));
                active = parent && !cond;
                continue;
            }
            if (line.StartsWith(".endif", StringComparison.Ordinal) && stack.Count > 0)
            {
                var (parent, _) = stack.Pop();
                active = parent;
                continue;
            }
            if (!active)
                continue;
            var match = GlobalFlagDefinition.Match(raw);
            if (match.Success)
                result.TryAdd(match.Groups["name"].Value,
                    int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
        }
        return result;
    }

    private static Dictionary<string, string> LoadSymbols(OracleRepo repo)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in new[]
                 {
                     Path.Combine(repo.Root, "constants"),
                     Path.Combine(repo.Root, "include"),
                 })
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.s", SearchOption.AllDirectories))
                foreach (var raw in File.ReadAllLines(file))
                {
                    var match = Define.Match(raw);
                    if (match.Success)
                        result.TryAdd(match.Groups["name"].Value,
                            $"0x{int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture):x}");
                }
        }
        return result;
    }

    private static string OperationFor(string line, string symbol)
    {
        foreach (var operation in new[]
                 {
                     "checkGlobalFlag", "setGlobalFlag", "unsetGlobalFlag",
                     "checkMemoryEq", "checkMemoryNe", "writeMemory", "orMemory",
                     "getThisRoomFlags", "wRoomState", "wRoomStateModifier",
                     "wEssencesObtained", "wMakuTreeState",
                 })
            if (line.Contains(operation, StringComparison.Ordinal))
                return operation;
        return "reference";
    }

    private static List<int> ParseHexArguments(string text)
    {
        var values = new List<int>();
        foreach (Match match in Regex.Matches(text, @"\$(?<value>[0-9a-fA-F]{1,2})"))
            values.Add(int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture));
        return values;
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line[..index] : line;
    }
}
