using System;
using System.Windows.Forms;

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
                else if (arg == "--pos" && i + 1 < args.Length)
                {
                    var parts = args[++i].Split(',');
                    if (parts.Length == 2 &&
                        float.TryParse(parts[0], out var x) &&
                        float.TryParse(parts[1], out var y))
                        Game1.OracleStartPosition = new Microsoft.Xna.Framework.Vector2(x, y);
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
    }
}