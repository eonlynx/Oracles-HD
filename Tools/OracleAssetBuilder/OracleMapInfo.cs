namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Per-map metadata consumed by the DX-HD runtime. The importer is the only
/// writer; the runtime only reads these already-converted values.
/// </summary>
public sealed record OracleMapInfo(
    string Game,
    int Group,
    string Era,
    string Kind,
    int RoomWidth,
    int RoomHeight,
    int GridWidth,
    int GridHeight,
    int Room,
    int Floor,
    int Dungeon,
    string Camera,
    IReadOnlyList<RoomEntryEffect> RoomEffects)
{
    public static OracleMapInfo Overworld(string game, int group, int grid,
        IReadOnlyList<RoomEntryEffect>? roomEffects = null)
    {
        return new OracleMapInfo(
            game, group,
            group is 1 or 3 or 5 or 7 ? "past" : "present",
            "overworld",
            10, 8, grid, grid,
            -1, -1, -1,
            "follow",
            roomEffects ?? Array.Empty<RoomEntryEffect>());
    }

    public static OracleMapInfo SingleRoom(string game, int group, int room,
        int roomWidth, int roomHeight, string kind, string camera,
        IReadOnlyList<RoomEntryEffect>? roomEffects = null)
    {
        return new OracleMapInfo(
            game, group,
            group is 1 or 3 or 5 or 7 ? "past" : "present",
            kind,
            roomWidth, roomHeight,
            0, 0,
            room, -1, -1,
            camera,
            roomEffects ?? Array.Empty<RoomEntryEffect>());
    }
}
