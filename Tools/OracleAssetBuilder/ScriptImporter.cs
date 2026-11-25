using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Extracts the script subset reachable from the first room-entry events into a
/// typed command table. The format follows ooa-godot's cutscene command stream
/// (script, label, index, opcode, args, payload) but resolves targets to command
/// indices and leaves text/audio as source identifiers for the runtime host.
/// </summary>
public static class ScriptImporter
{
    private sealed record EntryBinding(
        string InteractionConstant, int SubId, string Script, bool DeleteOnEnd, string Source);

    // Source-cited one-shot room-entry bindings. Interaction ids come from
    // constants/<game>/interactions.s; the script name must exist in the
    // assembled script sources. Extend this list as more initializers are
    // imported; the runtime itself never branches on an object or room id.
    private static readonly EntryBinding[] AgesEntryBindings =
    {
        new("INTERAC_RALPH", 0x0d, "ralphSubid0dScript", true,
            "object_code/ages/interactions/ralph.s:ralphRunScriptAndDeleteWhenOver"),
        new("INTERAC_MALE_VILLAGER", 0x0d, "villagerSubid0dScript", true,
            "object_code/ages/interactions/villager.s:@runSubid0d"),
    };

    private static readonly Regex TopLabel = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Compiled);
    private static readonly Regex LocalLabel = new(
        @"^@(?<label>[A-Za-z0-9_]+):\s*$", RegexOptions.Compiled);
    private static readonly Regex TextId = new(
        @"^<?TX_(?<value>[0-9a-fA-F]{4})$", RegexOptions.Compiled);

    private sealed class Node
    {
        public string Path = "";
        public int Line;
        public string Opcode = "";
        public string Operands = "";
    }

    private sealed class Script
    {
        public string Name = "";
        public string Path = "";
        public readonly List<Node> Nodes = new();
        public readonly Dictionary<string, int> Labels = new(StringComparer.Ordinal);
    }

    public static void Build(OracleRepo repo, string mapsOutDir, out IReadOnlyList<string> warnings)
    {
        var issues = new List<string>();
        var files = ScriptFiles(repo);
        var scripts = Parse(files);
        var flags = LoadGlobalFlags(repo, repo.Game);
        var bindings = LoadBindings(repo, scripts, flags, issues);
        var included = Collect(scripts, bindings.Select(binding => binding.Script).ToArray(), issues);
        var speeds = LoadSpeeds(repo);

        var tablePath = TablePath(mapsOutDir, repo.Game);
        Directory.CreateDirectory(Path.GetDirectoryName(tablePath)!);

        var rows = new List<string>
        {
            "script\tlabel\tindex\topcode\targ0\targ1\tpayload"
        };

        var ordered = included.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray();
        var bases = new Dictionary<string, int>(StringComparer.Ordinal);
        var flat = 0;
        foreach (var script in ordered)
        {
            bases[script.Name] = flat;
            flat += script.Nodes.Count;
        }

        var globalTargets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var script in ordered)
            foreach (var pair in script.Labels)
            {
                var name = pair.Key;
                if (name.StartsWith("@", StringComparison.Ordinal))
                    globalTargets[script.Name + name] = bases[script.Name] + pair.Value;
                else if (name.Length > 0)
                    globalTargets[name] = bases[script.Name] + pair.Value;
                else
                    globalTargets[script.Name] = bases[script.Name];
            }

        foreach (var script in ordered)
        {
            for (var index = 0; index < script.Nodes.Count; index++)
            {
                var node = script.Nodes[index];
                var localLabel = script.Labels.FirstOrDefault(pair =>
                    pair.Key.Length > 0 && pair.Value == index).Key ?? "";
                try
                {
                    rows.Add(Encode(script, node, bases[script.Name] + index, localLabel,
                        flags, speeds, bases[script.Name], globalTargets));
                }
                catch (Exception exception)
                {
                    issues.Add($"{Path.GetFileName(node.Path)}:{node.Line}:{script.Name}: {exception.Message}");
                }
            }
        }

        File.WriteAllText(tablePath, string.Join('\n', rows) + "\n", new UTF8Encoding(false));

        var bindingPath = BindingTablePath(mapsOutDir, repo.Game);
        var bindingRows = new List<string>
        {
            "interaction\tsubid\tscript\tdelete_on_end\tcompleted_flag\tsource"
        };
        foreach (var binding in bindings.OrderBy(binding => binding.Interaction)
                     .ThenBy(binding => binding.SubId))
        {
            bindingRows.Add(string.Join('\t',
                $"0x{binding.Interaction:x2}", $"0x{binding.SubId:x2}", binding.Script,
                binding.DeleteOnEnd ? "1" : "0",
                binding.CompletedFlag >= 0 ? $"0x{binding.CompletedFlag:x2}" : "-1",
                binding.Source));
        }
        File.WriteAllText(bindingPath, string.Join('\n', bindingRows) + "\n",
            new UTF8Encoding(false));

        var manifestPath = Path.ChangeExtension(tablePath, ".manifest.json");
        var inputs = files
            .Concat(new[]
            {
                Path.Combine(repo.Root, "constants", "common", "globalFlags.s"),
                Path.Combine(repo.Root, "constants", "common", "objectSpeeds.s"),
                Path.Combine(repo.Root, "constants", repo.Game, "interactions.s"),
            })
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new
            {
                path = Path.GetRelativePath(repo.Root, path).Replace('\\', '/'),
                sha256 = OracleRepo.Sha256(path),
            })
            .ToArray();

        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "event_scripts",
            rows = rows.Count - 1,
            scripts = included.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            bindings = bindings.Select(binding => new
            {
                interaction = binding.Interaction,
                subid = binding.SubId,
                script = binding.Script,
                deleteOnEnd = binding.DeleteOnEnd,
                completedFlag = binding.CompletedFlag,
                source = binding.Source,
            }).ToArray(),
            inputs,
            outputs = new[]
            {
                new
                {
                    path = Path.GetRelativePath(mapsOutDir, tablePath).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(tablePath),
                },
                new
                {
                    path = Path.GetRelativePath(mapsOutDir, bindingPath).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(bindingPath),
                },
            },
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));

        warnings = issues;
    }

    private sealed record ResolvedBinding(
        int Interaction, int SubId, string Script, bool DeleteOnEnd, int CompletedFlag,
        string Source);

    public static string TablePath(string mapsOutDir, string game)
    {
        return Path.Combine(mapsOutDir, "Scripts", $"oracle_{game}_scripts.tsv");
    }

    public static string BindingTablePath(string mapsOutDir, string game)
    {
        return Path.Combine(mapsOutDir, "Scripts", $"oracle_{game}_script_bindings.tsv");
    }

    private static IReadOnlyList<ResolvedBinding> LoadBindings(OracleRepo repo,
        IReadOnlyDictionary<string, Script> scripts, IReadOnlyDictionary<string, int> flags,
        List<string> issues)
    {
        if (repo.Game != "ages")
            return Array.Empty<ResolvedBinding>();

        var constantsPath = Path.Combine(repo.Root, "constants", repo.Game, "interactions.s");
        var interactions = LoadInteractionConstants(constantsPath);
        var result = new List<ResolvedBinding>();
        foreach (var binding in AgesEntryBindings)
        {
            if (!interactions.TryGetValue(binding.InteractionConstant, out var interaction))
            {
                issues.Add($"{Path.GetFileName(constantsPath)}: missing {binding.InteractionConstant}");
                continue;
            }
            if (!scripts.TryGetValue(binding.Script, out var script))
            {
                issues.Add($"{binding.Source}: missing script {binding.Script}");
                continue;
            }
            result.Add(new ResolvedBinding(interaction, binding.SubId, binding.Script,
                binding.DeleteOnEnd, CompletedFlag(script, scripts, flags), binding.Source));
        }
        return result;
    }

    /// <summary>
    /// Recognizes the one-shot guard at a script root:
    /// jumpifglobalflagset FLAG, stubScript, where the target is only scriptend.
    /// This is the source fact that lets presentation hide a completed event
    /// before Link ever crosses into its room.
    /// </summary>
    private static int CompletedFlag(Script script,
        IReadOnlyDictionary<string, Script> scripts, IReadOnlyDictionary<string, int> flags)
    {
        if (script.Nodes.Count == 0)
            return -1;
        var first = script.Nodes[0];
        if (first.Opcode != "jumpifglobalflagset")
            return -1;
        var operands = SplitOperands(first.Operands);
        if (operands.Length != 2 || !flags.TryGetValue(operands[0], out var flag))
            return -1;

        var targetName = ResolveScriptName(operands[1]);
        if (targetName == null || !scripts.TryGetValue(targetName, out var target) ||
            target.Nodes.Count == 0 || target.Nodes[0].Opcode != "scriptend")
            return -1;
        return flag;
    }

    private static IReadOnlyDictionary<string, int> LoadInteractionConstants(string path)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return result;

        foreach (var raw in File.ReadAllLines(path))
        {
            var match = Regex.Match(raw,
                @"^\s*\.define\s+(?<name>INTERAC_[A-Za-z0-9_]+)\s+\$(?<value>[0-9a-fA-F]+)");
            if (match.Success)
                result[match.Groups["name"].Value] =
                    int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture);
        }
        return result;
    }

    private static IReadOnlyList<string> ScriptFiles(OracleRepo repo)
    {
        var result = new List<string>();
        foreach (var path in new[]
                 {
                     Path.Combine(repo.Root, "scripts", repo.Game, "scripts.s"),
                     Path.Combine(repo.Root, "scripts", repo.Game, "scripts2.s"),
                     Path.Combine(repo.Root, "scripts", "common", "commonScripts.s"),
                 })
        {
            if (File.Exists(path))
                result.Add(path);
        }
        return result;
    }

    private static Dictionary<string, Script> Parse(IReadOnlyList<string> files)
    {
        var scripts = new Dictionary<string, Script>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            Script? current = null;
            var lineNumber = 0;
            foreach (var raw in File.ReadAllLines(file))
            {
                lineNumber++;
                var line = StripComment(raw).Trim();
                if (line.Length == 0)
                    continue;

                var top = TopLabel.Match(line);
                if (top.Success)
                {
                    var name = top.Groups["label"].Value;
                    current = new Script { Name = name, Path = file };
                    scripts[name] = current;
                    current.Labels[""] = 0;
                    continue;
                }

                var local = LocalLabel.Match(line);
                if (local.Success)
                {
                    if (current == null)
                        throw new InvalidDataException($"{file}: local label without a script");
                    current.Labels["@" + local.Groups["label"].Value] = current.Nodes.Count;
                    continue;
                }

                if (current == null || line.StartsWith(".", StringComparison.Ordinal))
                    continue;

                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                current.Nodes.Add(new Node
                {
                    Path = file,
                    Line = lineNumber,
                    Opcode = parts[0].ToLowerInvariant(),
                    Operands = parts.Length > 1 ? parts[1].Trim() : "",
                });
            }
        }
        return scripts;
    }

    private static Dictionary<string, Script> Collect(Dictionary<string, Script> scripts,
        IReadOnlyList<string> roots, List<string> issues)
    {
        var result = new Dictionary<string, Script>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (result.ContainsKey(name) || !scripts.TryGetValue(name, out var script))
                continue;
            result[name] = script;

            foreach (var node in script.Nodes)
            {
                var target = TargetOperand(node);
                if (target == null)
                    continue;
                var resolved = ResolveScriptName(target);
                if (resolved != null && scripts.ContainsKey(resolved))
                    queue.Enqueue(resolved);
            }
        }
        return result;
    }

    private static string? TargetOperand(Node node)
    {
        return node.Opcode switch
        {
            "scriptjump" or "callscript" or "loadscript" => LastOperand(node.Operands),
            "jumpifglobalflagset" => LastOperand(node.Operands),
            "jumpifmemoryset" => LastOperand(node.Operands),
            _ => null,
        };
    }

    private static string? ResolveScriptName(string target)
    {
        if (target.StartsWith("@", StringComparison.Ordinal))
            return null;
        return target.Replace("mainScripts.", "").Replace("scripts2.", "");
    }

    private static string Encode(Script script, Node node, int index, string label,
        IReadOnlyDictionary<string, int> flags, IReadOnlyDictionary<string, int> speeds,
        int scriptBase, IReadOnlyDictionary<string, int> globalTargets)
    {
        var operands = SplitOperands(node.Operands);
        var opcode = node.Opcode;
        var arg0 = "";
        var arg1 = "";
        var payload = "";

        int Num(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.StartsWith("$", StringComparison.Ordinal))
                return int.Parse(trimmed[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return number;
            if (speeds.TryGetValue(trimmed, out var speed))
                return speed;
            throw new InvalidDataException($"unresolved scalar '{trimmed}'");
        }

        int Target(string value)
        {
            var name = value.Trim();
            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                var local = script.Name + name;
                if (globalTargets.TryGetValue(local, out var localTarget))
                    return localTarget;
            }
            var global = name.Replace("mainScripts.", "").Replace("scripts2.", "");
            if (globalTargets.TryGetValue(global, out var target))
                return target;
            throw new InvalidDataException($"unresolved target '{value}'");
        }

        switch (opcode)
        {
            case "scriptjump":
                arg0 = Target(operands[0]).ToString();
                break;
            case "callscript":
                arg0 = Target(operands[0]).ToString();
                break;
            case "retscript":
            case "scriptend":
            case "disableinput":
            case "enableinput":
                break;
            case "wait":
                arg0 = Num(operands[0]).ToString();
                break;
            case "showtext":
            {
                var match = TextId.Match(operands[0].Trim());
                if (!match.Success)
                    throw new InvalidDataException($"malformed text operand '{operands[0]}'");
                arg0 = int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture).ToString();
                payload = operands[0].Trim().TrimStart('<');
                break;
            }
            case "setglobalflag":
                arg0 = Flag(operands[0]).ToString();
                break;
            case "jumpifglobalflagset":
                arg0 = Flag(operands[0]).ToString();
                arg1 = Target(operands[1]).ToString();
                break;
            case "jumpifmemoryset":
                arg0 = Memory(operands[0]).ToString();
                arg1 = Num(operands[1]).ToString();
                payload = Target(operands[2]).ToString();
                break;
            case "setanimation":
            case "setspeed":
            case "setangle":
            case "applyspeed":
                arg0 = Num(operands[0]).ToString();
                break;
            case "movedown":
            case "moveup":
            case "moveright":
            case "moveleft":
                arg0 = Num(operands[0]).ToString();
                break;
            case "writeobjectbyte":
                arg0 = ObjectByte(operands[0]).ToString();
                arg1 = Num(operands[1]).ToString();
                break;
            case "playsound":
                payload = operands[0].Trim();
                break;
            case "asm15":
                payload = operands[0].Trim().Replace("scriptHelp.", "");
                break;
            case "setdisabledobjectsto11":
                arg0 = "17";
                break;
            case "stopifroomflag80set":
                arg0 = "128";
                arg1 = Target("stubScript").ToString();
                opcode = "jumpifroomflagset";
                break;
            default:
                throw new InvalidDataException($"unsupported opcode '{node.Opcode} {node.Operands}'");
        }

        int Flag(string value)
        {
            var name = value.Trim();
            if (!flags.TryGetValue(name, out var flag))
                throw new InvalidDataException($"unresolved global flag '{name}'");
            return flag;
        }

        return string.Join('\t', script.Name, label, index.ToString(),
            opcode, arg0, arg1, payload);
    }

    private static int Memory(string value)
    {
        return value.Trim() switch
        {
            "wcddb" => 1,
            _ => throw new InvalidDataException($"unresolved memory '{value}'"),
        };
    }

    private static int ObjectByte(string value)
    {
        return value.Trim() switch
        {
            "Interaction.var3f" => 1,
            _ => throw new InvalidDataException($"unresolved object byte '{value}'"),
        };
    }

    private static IReadOnlyDictionary<string, int> LoadGlobalFlags(OracleRepo repo, string game)
    {
        var path = Path.Combine(repo.Root, "constants", "common", "globalFlags.s");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
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
                var cond = token == "ROM_AGES" ? game == "ages" : token == "ROM_SEASONS" ? game == "seasons" : false;
                stack.Push((active, cond));
                active &= cond;
                continue;
            }
            if (line.StartsWith(".ifndef ", StringComparison.Ordinal))
            {
                var token = line[".ifndef ".Length..].Trim();
                var cond = token == "ROM_AGES" ? game != "ages" : token == "ROM_SEASONS" ? game != "seasons" : true;
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

            var match = Regex.Match(raw,
                @"^\s*(?<name>GLOBALFLAG_[A-Za-z0-9_]+)\s+db\s*;\s*\$(?<value>[0-9a-fA-F]{2})");
            if (match.Success)
                result.TryAdd(match.Groups["name"].Value,
                    int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, int> LoadSpeeds(OracleRepo repo)
    {
        var path = Path.Combine(repo.Root, "constants", "common", "objectSpeeds.s");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return result;

        foreach (var raw in File.ReadAllLines(path))
        {
            var match = Regex.Match(raw,
                @"^\s*(?<name>SPEED_[A-Za-z0-9_]+)\s+dsb\s+\d+\s*;\s*(?:\$|0x)(?<value>[0-9a-fA-F]{2})");
            if (match.Success)
                result.TryAdd(match.Groups["name"].Value,
                    int.Parse(match.Groups["value"].Value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));

            var alias = Regex.Match(raw, @"^\s*\.define\s+(?<name>SPEED_[A-Za-z0-9_]+)\s+(?<target>SPEED_[A-Za-z0-9_]+)");
            if (alias.Success)
                result[alias.Groups["name"].Value] = -1; // resolved in a second pass
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var alias = Regex.Match(raw, @"^\s*\.define\s+(?<name>SPEED_[A-Za-z0-9_]+)\s+(?<target>SPEED_[A-Za-z0-9_]+)");
            if (alias.Success && result.TryGetValue(alias.Groups["target"].Value, out var value) && value >= 0)
                result[alias.Groups["name"].Value] = value;
        }
        return result.Where(pair => pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static string[] SplitOperands(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string LastOperand(string value)
    {
        var parts = SplitOperands(value);
        return parts.Length == 0 ? "" : parts[^1];
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line[..index] : line;
    }
}
