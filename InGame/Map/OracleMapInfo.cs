using System.Collections.Generic;

namespace ProjectZ.InGame.Map
{
    public sealed class OracleRoomEffectInfo
    {
        public int Group;
        public int Room;
        public int Handler;
    }

    /// <summary>
    /// Read-only metadata emitted by OracleAssetBuilder. It carries converted
    /// room/era/kind values so runtime code does not derive them from map file
    /// names or pixel positions.
    /// </summary>
    public sealed class OracleMapInfo
    {
        public string Game = "ages";
        public int Group;
        public string Era = "present";
        public string Kind = "overworld";
        public int RoomWidth = 10;
        public int RoomHeight = 8;
        public int GridWidth;
        public int GridHeight;
        public int Room = -1;
        public int Floor = -1;
        public int Dungeon = -1;
        public string Camera = "follow";
        public readonly List<OracleRoomEffectInfo> RoomEffects = new();

        public bool IsOverworld => Kind == "overworld";
        public bool IsInterior => Kind == "interior";
        public bool IsDungeon => Kind == "dungeon";
        public bool IsPast => Era == "past";
        public bool FixedCamera => Camera == "fixed";
    }
}
