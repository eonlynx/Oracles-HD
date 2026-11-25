using System;
using System.Windows.Forms;
using ProjectZ.InGame.Things;

namespace ProjectZ
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var editorMode = false;
            var loadFirstSave = false;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "editor")
                    editorMode = true;
                else if (arg == "loadSave")
                    loadFirstSave = true;
                else if (arg == "oracle")
                    Game1.OracleOverworld = true;
                else if (arg == "--room" && i + 1 < args.Length)
                {
                    // Development entry: boot directly into a generated present
                    // interior (group 2), e.g. --room e7.
                    var value = args[++i].TrimStart('$');
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        value = value[2..];
                    var room = Convert.ToInt32(value, 16);
                    Game1.OracleStartMap = $"oracle_ages_g2_r{room:x2}.map";
                    Game1.OracleStartPosition = new Microsoft.Xna.Framework.Vector2(80, 120);
                }
                else if (arg == "--map" && i + 1 < args.Length)
                {
                    // Development entry: boot a generated Oracle map by name,
                    // e.g. --map oracle_ages_g1_overworld.map.
                    Game1.OracleStartMap = args[++i];
                }
                else if (arg == "--pos" && i + 1 < args.Length)
                {
                    var parts = args[++i].Split(',');
                    if (parts.Length == 2 &&
                        float.TryParse(parts[0], out var x) &&
                        float.TryParse(parts[1], out var y))
                        Game1.OracleStartPosition = new Microsoft.Xna.Framework.Vector2(x, y);
                }
                else if (arg == "--flag" && i + 1 < args.Length)
                    Game1.OracleDevFlags.Add(ParseFlag(args[++i]));
                else if (arg == "--room-state" && i + 1 < args.Length)
                    Game1.OracleRoomStateModifier = ParseHex(args[++i]);
                else if (arg == "--room-flags" && i + 1 < args.Length)
                    Game1.OracleRoomFlags = ParseHex(args[++i]);
            }

            try
            {
                using (var game = new Game1(editorMode, loadFirstSave))
                    game.Run();
            }
            catch (Exception exception)
            {
               MessageBox.Show(exception.StackTrace, exception.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
               throw;
            }
        }

        private static int ParseFlag(string value)
        {
            return value.ToUpperInvariant() switch
            {
                "FINISHEDGAME" => OracleFlagState.FinishedGame,
                "INTRO_DONE" => OracleFlagState.IntroDone,
                _ => ParseHex(value),
            };
        }

        private static int ParseHex(string value)
        {
            value = value.Trim();
            if (value.StartsWith('$'))
                value = value[1..];
            else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value[2..];
            return Convert.ToInt32(value, 16);
        }
    }
}