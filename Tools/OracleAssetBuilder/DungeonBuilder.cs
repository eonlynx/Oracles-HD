namespace Oracles.Tools.AssetBuilder;

public sealed record DungeonBuildResult(
    int Dungeon,
    int Group,
    int Rooms,
    int Floors,
    IReadOnlyList<string> MapPaths,
    IReadOnlyList<string> Warnings,
    int Interactions,
    int Enemies,
    int Parts,
    int Items);

/// <summary>
/// Builds the maps of one dungeon from dungeonLayouts.s. The floor grid is the
/// source of room identity: a dungeon's rooms are not laid out by room index
/// (top's view_map.c reads the same live 8x8 grid for this reason).
/// </summary>
public sealed class DungeonBuilder
{
    private readonly OracleContent _content;
    private readonly string _outDir;
    private readonly string _tilesetsDir;
    private readonly OracleWarpData _warps;
    private readonly RoomObjectStreams _streams;

    public DungeonBuilder(OracleContent content, string outDir, string tilesetsDir,
        OracleWarpData warps, RoomObjectStreams streams)
    {
        _content = content;
        _outDir = outDir;
        _tilesetsDir = tilesetsDir;
        _warps = warps;
        _streams = streams;
    }

    public DungeonBuildResult Build(int dungeonId, OracleOptions options)
    {
        var dungeons = new OracleDungeons(_content.Repo);
        dungeons.WriteTables(_content.Repo, _outDir);
        var dungeon = dungeons.Get(dungeonId);

        // Use a group-specific content view; dungeon rooms live in group 4/5.
        var content = new OracleContent(_content.Repo, dungeon.Group);
        var roomObjects = new InteriorBuilder(content, _outDir, _tilesetsDir, _warps, _streams);
        var plan = EntityAtlas.Build(_content.Repo, _streams, _warps, _outDir, out var warnings);
        var issues = new List<string>(warnings);
        var maps = new List<string>();
        var roomBuilder = new RoomBuilder(content, _outDir, _tilesetsDir);
        var rooms = 0;
        var interactions = 0;
        var enemies = 0;
        var parts = 0;
        var items = 0;

        for (var floor = 0; floor < dungeon.Floors; floor++)
        {
            var layout = dungeons.GetFloor(dungeonId, floor);
            for (var row = 0; row < 8; row++)
                for (var col = 0; col < 8; col++)
                {
                    var room = layout.Rooms[row * 8 + col];
                    if (room == 0)
                        continue;

                    rooms++;
                    var roomData = _streams.GetRoom(dungeon.Group, room);
                    interactions += roomData.Interactions.Count;
                    enemies += roomData.Enemies.Count;
                    parts += roomData.Parts.Count;
                    items += roomData.Items.Count;

                    var objects = new List<MapObjectPlacement>();
                    roomObjects.AddSpriteObjects(objects, roomData, plan, 0, 0);
                    AddDungeonWarps(objects, dungeons, dungeon, layout, row, col, room);
                    var result = roomBuilder.Build(dungeon.Group, room, false, null, options,
                        objects,
                        mapLayout => roomObjects.AddWarpObjects(
                            dungeon.Group, room, mapLayout, options, issues),
                        floor, dungeonId);
                    maps.Add(result.MapPath);
                }
        }

        return new DungeonBuildResult(dungeonId, dungeon.Group, rooms, dungeon.Floors,
            maps, issues, interactions, enemies, parts, items);
    }

    private void AddDungeonWarps(List<MapObjectPlacement> objects, OracleDungeons dungeons,
        DungeonInfo dungeon, DungeonFloor floor, int row, int col, int room)
    {
        var properties = dungeons.RoomProperties(dungeon.Group, room);
        if (properties == 0)
            return;

        TryWarp(1, 0, -1, 80, 112, 0, 0, 120, 160, 8);
        TryWarp(2, 1, 0, 8, 64, 1, 152, 0, 8, 128);
        TryWarp(4, 0, 1, 80, 8, 2, 0, 0, 160, 8);
        TryWarp(8, -1, 0, 152, 64, 3, 0, 0, 8, 128);

        void TryWarp(int bit, int dx, int dy, int spawnX, int spawnY,
            int enterDirection, int x, int y, int width, int height)
        {
            if ((properties & bit) == 0)
                return;
            var targetRow = row + dy;
            var targetCol = col + dx;
            if (targetRow is < 0 or > 7 || targetCol is < 0 or > 7)
                return;
            var targetRoom = floor.Rooms[targetRow * 8 + targetCol];
            if (targetRoom == 0)
                return;
            var target = EntityAtlas.MapName(_content.Repo, dungeon.Group, targetRoom);
            if (target == null)
                return;
            objects.Add(new MapObjectPlacement("oracleWarp", new[]
            {
                x.ToString(), y.ToString(), target,
                spawnX.ToString(), spawnY.ToString(), enterDirection.ToString(),
                width.ToString(), height.ToString(),
                (3 | (3 << 4)).ToString(),
            }));
        }
    }
}
