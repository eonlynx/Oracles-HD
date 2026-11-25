using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Map
{
    /// <summary>
    /// Runtime application of applySingleTileChanges. The generated table is
    /// keyed by overworld group/room and carries the resolved atlas index,
    /// collision nibble and terrain byte for both the base and replacement
    /// metatile. A room-flag mask (or Ages' $f0/$f1/$f2 predicate) selects the
    /// write; nothing about a specific room or tile is hardcoded here.
    /// </summary>
    internal static class OracleTileChangeCatalog
    {
        private sealed record Row(
            int Room, int Mask, int X, int Y,
            int BaseAtlas, int ReplacementAtlas,
            byte BaseCollision, byte ReplacementCollision,
            byte BaseTerrain, byte ReplacementTerrain);

        private static readonly Dictionary<(string Game, int Group), List<Row>> Cache = new();
        private static readonly Dictionary<(string Game, int Group), bool> Loaded = new();

        public static void Apply(Map map, OracleSessionState session, OracleGameProfile profile)
        {
            var info = map?.OracleInfo;
            if (info == null || info.Kind != "overworld" || map.TileMap.ArrayTileMap == null)
                return;

            foreach (var row in GetRows(info.Game, info.Group))
            {
                var active = IsActive(row.Mask, info.Group, row.Room, session, profile);
                var atlas = active ? row.ReplacementAtlas : row.BaseAtlas;
                var collision = active ? row.ReplacementCollision : row.BaseCollision;
                var terrain = active ? row.ReplacementTerrain : row.BaseTerrain;

                if (row.X < 0 || row.Y < 0 ||
                    row.X >= map.TileMap.ArrayTileMap.GetLength(0) ||
                    row.Y >= map.TileMap.ArrayTileMap.GetLength(1) ||
                    map.TileMap.ArrayTileMap.GetLength(2) == 0)
                    continue;

                map.TileMap.ArrayTileMap[row.X, row.Y, 0] = atlas;
                if (map.CollisionMap != null &&
                    row.X < map.CollisionMap.GetLength(0) &&
                    row.Y < map.CollisionMap.GetLength(1))
                    map.CollisionMap[row.X, row.Y] = collision;
                if (map.TerrainMap != null &&
                    row.X < map.TerrainMap.GetLength(0) &&
                    row.Y < map.TerrainMap.GetLength(1))
                    map.TerrainMap[row.X, row.Y] = terrain;
            }
        }

        private static bool IsActive(int mask, int group, int room,
            OracleSessionState session, OracleGameProfile profile)
        {
            if (mask < 0xF0)
                return (session.GetRoomFlags(group, room) & mask) != 0;
            return mask switch
            {
                // applySingleTileChanges: unlinked, linked, finished-game.
                0xF0 => !session.IsLinkedGame,
                0xF1 => session.IsLinkedGame,
                0xF2 => session.GetFlag(profile.FinishedFlag),
                _ => false,
            };
        }

        private static IReadOnlyList<Row> GetRows(string game, int group)
        {
            var key = (game, group);
            if (Loaded.TryGetValue(key, out var loaded) && loaded)
                return Cache[key];

            Loaded[key] = true;
            var rows = new List<Row>();
            var path = Path.Combine(Values.PathMapsFolder, "TileChanges",
                $"oracle_{game}_g{group}_tile_changes.tsv");
            if (!File.Exists(path))
            {
                Cache[key] = rows;
                return rows;
            }

            var lines = File.ReadAllLines(path);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                var fields = line.Split('\t');
                if (fields.Length != 13)
                    continue;
                rows.Add(new Row(
                    ParseHex(fields[0]), ParseHex(fields[1]),
                    ParseHex(fields[2]), ParseHex(fields[3]),
                    ParseHex(fields[6]), ParseHex(fields[7]),
                    (byte)ParseHex(fields[8]), (byte)ParseHex(fields[9]),
                    (byte)ParseHex(fields[10]), (byte)ParseHex(fields[11])));
            }

            Cache[key] = rows;
            return rows;
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
