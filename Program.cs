using System;
using System.Windows.Forms;
using ProjectZ.Base;
using ProjectZ.InGame.Things;

namespace ProjectZ
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            OraclePerf.Mark("main start");
            var editorMode = false;
            var loadFirstSave = false;
            int? roomArg = null;
            string? mapArg = null;
            var positionExplicit = false;
            var flagArgs = new System.Collections.Generic.List<string>();
            var equipArgs = new System.Collections.Generic.List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "editor")
                    editorMode = true;
                else if (arg == "loadSave")
                    loadFirstSave = true;
                else if (arg == "oracle")
                    Game1.OracleOverworld = true;
                else if (arg == "--game" && i + 1 < args.Length)
                {
                    Game1.OracleGame = args[++i].Trim().ToLowerInvariant();
                    _ = ProjectZ.InGame.Map.OracleGameProfile.For(Game1.OracleGame);
                }
                else if (arg == "--room" && i + 1 < args.Length)
                {
                    // Development entry: boot a generated interior, e.g. --room e7.
                    var value = args[++i].TrimStart('$');
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        value = value[2..];
                    roomArg = Convert.ToInt32(value, 16);
                }
                else if (arg == "--map" && i + 1 < args.Length)
                {
                    // Development entry: boot a generated Oracle map by name,
                    // e.g. --map oracle_ages_g1_overworld.map.
                    mapArg = args[++i];
                }
                else if (arg == "--pos" && i + 1 < args.Length)
                {
                    var parts = args[++i].Split(',');
                    if (parts.Length == 2 &&
                        float.TryParse(parts[0], out var x) &&
                        float.TryParse(parts[1], out var y))
                    {
                        Game1.OracleStartPosition = new Microsoft.Xna.Framework.Vector2(x, y);
                        positionExplicit = true;
                    }
                }
                else if (arg == "--entry" && i + 1 < args.Length)
                {
                    // Development entry: force the native screen-transition
                    // direction for the first map load (0 up, 1 right, 2 down, 3 left).
                    if (int.TryParse(args[++i], out var entry) && entry is >= 0 and <= 3)
                        Game1.OracleEntryDirection = entry;
                }
                else if (arg == "--no-intro")
                    Game1.OracleIntroFlag = -1;
                else if (arg == "--intro-done")
                    Game1.OracleIntroFlag = Game1.OracleProfile.IntroFlag;
                else if (arg == "--flag" && i + 1 < args.Length)
                    flagArgs.Add(args[++i]);
                else if (arg == "--equip" && i + 1 < args.Length)
                    // Development entry: grant an inventory item to the clean
                    // Oracle new game, e.g. --equip sword1 for combat checks.
                    equipArgs.Add(args[++i]);
                else if (arg == "--freeze-enemies")
                    // Development entry: keep enemy AI targets still so a
                    // deterministic combat check can aim at a known position.
                    Game1.OracleFreezeEnemies = true;
                else if (arg == "--room-state" && i + 1 < args.Length)
                    Game1.OracleRoomStateModifier = ParseHex(args[++i]);
                else if (arg == "--room-flags" && i + 1 < args.Length)
                {
                    Game1.OracleRoomFlags = ParseHex(args[++i]);
                    Game1.OracleRoomFlagsSet = true;
                }
            }

            if (Game1.OracleOverworld)
            {
                foreach (var flag in flagArgs)
                    Game1.OracleDevFlags.Add(Game1.OracleProfile.ParseFlag(flag));
                foreach (var item in equipArgs)
                    Game1.OracleDevEquipment.Add(item);

                if (mapArg != null)
                    Game1.OracleStartMap = mapArg;
                else if (roomArg is int room)
                {
                    Game1.OracleStartMap = Game1.OracleProfile.InteriorMap(room);
                    if (!positionExplicit)
                        Game1.OracleStartPosition = new Microsoft.Xna.Framework.Vector2(80, 120);
                }
                else
                {
                    Game1.OracleStartMap = Game1.OracleProfile.OverworldMap(
                        Game1.OracleProfile.OverworldGroups[0]);
                }
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

