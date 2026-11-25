using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ProjectZ.InGame.Map;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Scripting
{
    /// <summary>Normalized opcode vocabulary emitted by ScriptImporter.</summary>
    internal enum CutsceneOpcode
    {
        ScriptJump,
        CallScript,
        ReturnScript,
        ScriptEnd,
        Wait,
        DisableInput,
        EnableInput,
        ShowText,
        SetAnimation,
        SetSpeed,
        SetAngle,
        ApplySpeed,
        MoveUp,
        MoveRight,
        MoveDown,
        MoveLeft,
        WriteObjectByte,
        PlaySound,
        Asm15,
        JumpIfGlobalFlagSet,
        JumpIfMemorySet,
        SetGlobalFlag,
        SetDisabledObjectsTo11,
    }

    /// <summary>
    /// A normalized command. Branch and call destinations are absolute command
    /// indices in the emitted stream (the table is a single sorted program).
    /// </summary>
    internal readonly record struct CutsceneCommand(
        int Index, string Script, string Label, CutsceneOpcode Opcode,
        int Arg0, int Arg1, int Target, string Payload);

    internal sealed record CutsceneScriptBinding(
        int Interaction, int SubId, string Script, bool DeleteOnEnd, int CompletedFlag,
        string Source);

    /// <summary>
    /// Loads the importer-generated Oracle script stream and its source-cited
    /// actor bindings. The table is validated once per game; runners keep only
    /// their own instruction, counters, call stack and actor state.
    /// </summary>
    internal static class CutsceneCommandCatalog
    {
        private static readonly Dictionary<string, CutsceneScriptTable> Cache =
            new(StringComparer.Ordinal);

        public static CutsceneScriptTable Load(OracleGameProfile profile)
        {
            if (Cache.TryGetValue(profile.Game, out var table))
                return table;

            var directory = Path.Combine(Values.PathMapsFolder, "Scripts");
            var commandPath = Path.Combine(directory, $"oracle_{profile.Game}_scripts.tsv");
            var bindingPath = Path.Combine(directory, $"oracle_{profile.Game}_script_bindings.tsv");

            table = CutsceneScriptTable.Load(commandPath, bindingPath);
            Cache[profile.Game] = table;
            return table;
        }

        public static bool TryLoad(OracleGameProfile profile, out CutsceneScriptTable table)
        {
            try
            {
                table = Load(profile);
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Oracle scripts disabled for {profile.Game}: {exception.Message}");
                table = null;
                return false;
            }
        }
    }

    internal sealed class CutsceneScriptTable
    {
        private readonly Dictionary<string, int> _scriptStarts;
        private readonly Dictionary<(int Interaction, int SubId), CutsceneScriptBinding> _bindings;

        public IReadOnlyList<CutsceneCommand> Commands { get; }

        private CutsceneScriptTable(IReadOnlyList<CutsceneCommand> commands,
            Dictionary<string, int> scriptStarts,
            Dictionary<(int, int), CutsceneScriptBinding> bindings)
        {
            Commands = commands;
            _scriptStarts = scriptStarts;
            _bindings = bindings;
        }

        public int StartIndex(string script)
        {
            if (!_scriptStarts.TryGetValue(script, out var start))
                throw new InvalidDataException($"Script '{script}' is not in the command table.");
            return start;
        }

        public bool TryGetBinding(int interaction, int subId, out CutsceneScriptBinding binding)
        {
            return _bindings.TryGetValue((interaction, subId), out binding);
        }

        public static CutsceneScriptTable Load(string commandPath, string bindingPath)
        {
            if (!File.Exists(commandPath))
                throw new FileNotFoundException("Oracle script table not found.", commandPath);
            if (!File.Exists(bindingPath))
                throw new FileNotFoundException("Oracle script binding table not found.", bindingPath);

            var commands = LoadCommands(commandPath);
            var scriptStarts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < commands.Count; index++)
                scriptStarts.TryAdd(commands[index].Script, index);

            var bindings = LoadBindings(bindingPath, scriptStarts);
            return new CutsceneScriptTable(commands, scriptStarts, bindings);
        }

        private static List<CutsceneCommand> LoadCommands(string path)
        {
            var lines = File.ReadAllLines(path);
            const string expected = "script\tlabel\tindex\topcode\targ0\targ1\tpayload";
            if (lines.Length == 0 || lines[0].TrimEnd('\r') != expected)
                throw new InvalidDataException($"{path}: unexpected script table header.");

            var commands = new List<CutsceneCommand>(lines.Length - 1);
            for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                var fields = line.Split('\t');
                if (fields.Length != 7)
                    throw new InvalidDataException(
                        $"{path}:{lineIndex + 1}: expected 7 columns, found {fields.Length}.");

                var index = ParseInt(path, lineIndex, "index", fields[2]);
                if (index != commands.Count)
                    throw new InvalidDataException(
                        $"{path}:{lineIndex + 1}: expected command index {commands.Count}, found {index}.");

                var opcodeText = fields[3];
                var opcode = ParseOpcode(path, lineIndex, opcodeText);
                var arg0 = ParseInt(path, lineIndex, "arg0", fields[4]);
                var arg1 = ParseInt(path, lineIndex, "arg1", fields[5]);
                var payload = fields[6];

                var target = opcode switch
                {
                    CutsceneOpcode.ScriptJump or CutsceneOpcode.CallScript => arg0,
                    CutsceneOpcode.JumpIfGlobalFlagSet => arg1,
                    CutsceneOpcode.JumpIfMemorySet => ParseInt(path, lineIndex, "target", payload),
                    _ => -1,
                };

                commands.Add(new CutsceneCommand(index, fields[0], fields[1], opcode,
                    arg0, arg1, target, payload));
            }

            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (command.Target < 0)
                    continue;
                if (command.Target >= commands.Count)
                    throw new InvalidDataException(
                        $"{path}: command {index} ({command.Script}) targets {command.Target}.");
            }

            return commands;
        }

        private static CutsceneOpcode ParseOpcode(string path, int lineIndex, string value)
        {
            return value switch
            {
                "scriptjump" => CutsceneOpcode.ScriptJump,
                "callscript" => CutsceneOpcode.CallScript,
                "retscript" => CutsceneOpcode.ReturnScript,
                "scriptend" => CutsceneOpcode.ScriptEnd,
                "wait" => CutsceneOpcode.Wait,
                "disableinput" => CutsceneOpcode.DisableInput,
                "enableinput" => CutsceneOpcode.EnableInput,
                "showtext" => CutsceneOpcode.ShowText,
                "setanimation" => CutsceneOpcode.SetAnimation,
                "setspeed" => CutsceneOpcode.SetSpeed,
                "setangle" => CutsceneOpcode.SetAngle,
                "applyspeed" => CutsceneOpcode.ApplySpeed,
                "moveup" => CutsceneOpcode.MoveUp,
                "moveright" => CutsceneOpcode.MoveRight,
                "movedown" => CutsceneOpcode.MoveDown,
                "moveleft" => CutsceneOpcode.MoveLeft,
                "writeobjectbyte" => CutsceneOpcode.WriteObjectByte,
                "playsound" => CutsceneOpcode.PlaySound,
                "asm15" => CutsceneOpcode.Asm15,
                "jumpifglobalflagset" => CutsceneOpcode.JumpIfGlobalFlagSet,
                "jumpifmemoryset" => CutsceneOpcode.JumpIfMemorySet,
                "setglobalflag" => CutsceneOpcode.SetGlobalFlag,
                "setdisabledobjectsto11" => CutsceneOpcode.SetDisabledObjectsTo11,
                _ => throw new InvalidDataException(
                    $"{path}:{lineIndex + 1}: unsupported script opcode '{value}'."),
            };
        }

        private static Dictionary<(int, int), CutsceneScriptBinding> LoadBindings(
            string path, IReadOnlyDictionary<string, int> scriptStarts)
        {
            var lines = File.ReadAllLines(path);
            const string expected = "interaction\tsubid\tscript\tdelete_on_end\tcompleted_flag\tsource";
            if (lines.Length == 0 || lines[0].TrimEnd('\r') != expected)
                throw new InvalidDataException($"{path}: unexpected binding table header.");

            var result = new Dictionary<(int, int), CutsceneScriptBinding>();
            for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                var fields = line.Split('\t');
                if (fields.Length != 6)
                    throw new InvalidDataException(
                        $"{path}:{lineIndex + 1}: expected 6 columns, found {fields.Length}.");

                var interaction = ParseHex(path, lineIndex, "interaction", fields[0]);
                var subId = ParseHex(path, lineIndex, "subid", fields[1]);
                var script = fields[2];
                if (!scriptStarts.ContainsKey(script))
                    throw new InvalidDataException(
                        $"{path}:{lineIndex + 1}: binding references missing script '{script}'.");

                var completedFlag = fields[4] == "-1"
                    ? -1
                    : ParseHex(path, lineIndex, "completed_flag", fields[4]);
                var binding = new CutsceneScriptBinding(interaction, subId, script,
                    fields[3] == "1", completedFlag, fields[5]);
                if (!result.TryAdd((interaction, subId), binding))
                    throw new InvalidDataException(
                        $"{path}:{lineIndex + 1}: duplicate binding for ${interaction:x2}:${subId:x2}.");
            }
            return result;
        }

        private static int ParseInt(string path, int lineIndex, string field, string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;
            if (value.Length == 0)
                return 0;
            throw new InvalidDataException(
                $"{path}:{lineIndex + 1}: invalid {field} '{value}'.");
        }

        private static int ParseHex(string path, int lineIndex, string field, string value)
        {
            var text = value.Trim();
            if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(text.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var result))
            {
                throw new InvalidDataException(
                    $"{path}:{lineIndex + 1}: invalid {field} '{value}'.");
            }
            return result;
        }
    }
}
