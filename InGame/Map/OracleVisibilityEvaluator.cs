using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Map
{
    /// <summary>
    /// Single save/progress visibility evaluator for every room entity. The
    /// generated table carries source-cited rules keyed by (id, subid, var03);
    /// map flags and room-state bits remain the generic fallback for objects
    /// whose initializer has no extra story rule.
    /// </summary>
    internal static class OracleVisibilityEvaluator
    {
        private sealed record Rule(int Flag, bool Expected);

        private static readonly Dictionary<(int Id, int SubId, int Var03), List<Rule>>
            Rules = new();
        private static bool _loaded;
        private static string _game;

        public static bool IsVisible(OracleSessionState session,
            int id, int subId, int var03,
            int requiresFlag, int forbiddenFlag, int roomStateMask,
            int roomFlagMask, int roomFlagValue)
        {
            EnsureLoaded();
            if (Rules.TryGetValue((id, subId, var03), out var rules))
            {
                foreach (var rule in rules)
                    if (session.GetFlag(rule.Flag) != rule.Expected)
                        return false;
            }

            if (requiresFlag >= 0 && !session.GetFlag(requiresFlag))
                return false;
            if (forbiddenFlag >= 0 && session.GetFlag(forbiddenFlag))
                return false;
            if (roomStateMask != 0 && (roomStateMask & (1 << session.RoomStateModifier)) != 0)
                return false;
            if (roomFlagMask != 0 && (session.CurrentRoomFlags & roomFlagMask) != roomFlagValue)
                return false;
            return true;
        }

        private static void EnsureLoaded()
        {
            var game = Game1.OracleProfile.Game;
            if (_loaded && _game == game)
                return;
            _loaded = true;
            _game = game;
            Rules.Clear();

            var path = Path.Combine(Values.PathMapsFolder, "Visibility",
                $"oracle_{game}_visibility.tsv");
            if (!File.Exists(path))
                return;

            var lines = File.ReadAllLines(path);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                var fields = line.Split('\t');
                if (fields.Length != 6)
                    continue;
                var id = ParseHex(fields[0]);
                var subId = ParseHex(fields[1]);
                var var03 = ParseHex(fields[2]);
                var flag = ParseHex(fields[3]);
                var expected = fields[4] == "1";
                if (!Rules.TryGetValue((id, subId, var03), out var list))
                    Rules[(id, subId, var03)] = list = new List<Rule>();
                list.Add(new Rule(flag, expected));
            }
        }

        private static int ParseHex(string value)
        {
            var text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}
