namespace Oracles.Tools.AssetBuilder;

public sealed record InteriorBuildResult(
    int Rooms,
    IReadOnlyList<string> MapPaths,
    IReadOnlyList<string> Warnings,
    int Interactions,
    int Enemies,
    int Parts,
    int Items,
    int Conditional);

/// <summary>
/// Builds present-day interior maps (group 2) reachable from the group-0
/// overworld. Uses the same RoomBuilder as single-room output, adds native
/// oracleSprite/oracleWarp objects, and shares one entity atlas with the
/// overworld builder.
/// </summary>
public sealed class InteriorBuilder
{
    private readonly OracleContent _content;
    private readonly string _outDir;
    private readonly string _tilesetsDir;
    private readonly OracleWarpData _warps;
    private readonly RoomObjectStreams _streams;

    public InteriorBuilder(OracleContent content, string outDir, string tilesetsDir,
        OracleWarpData warps, RoomObjectStreams streams)
    {
        _content = content;
        _outDir = outDir;
        _tilesetsDir = tilesetsDir;
        _warps = warps;
        _streams = streams;
    }

    public InteriorBuildResult BuildAll(OracleOptions options, int? onlyRoom = null)
    {
        var rooms = EntityAtlas.InteriorRooms(_warps);
        if (onlyRoom.HasValue)
            rooms = new SortedSet<int> { onlyRoom.Value };

        if (rooms.Count == 0)
            return new InteriorBuildResult(0, Array.Empty<string>(), Array.Empty<string>(), 0, 0, 0, 0, 0);

        var plan = EntityAtlas.Build(_content.Repo, _streams, _warps, _outDir, out var warnings);
        var issues = new List<string>(warnings);
        var maps = new List<string>();
        var roomBuilder = new RoomBuilder(_content, _outDir, _tilesetsDir);
        var interactions = 0;
        var enemies = 0;
        var parts = 0;
        var items = 0;
        var conditional = 0;

        foreach (var room in rooms)
        {
            var roomObjects = new List<MapObjectPlacement>();
            var roomData = _streams.GetRoom(2, room);
            interactions += roomData.Interactions.Count;
            enemies += roomData.EnemyCount;
            parts += roomData.PartCount;
            items += roomData.ItemCount;
            conditional += roomData.ConditionalCount;

            AddSpriteObjects(roomObjects, roomData, plan, 0, 0);

            var result = roomBuilder.Build(2, room, false, null, options, roomObjects,
                layout => AddWarpObjects(2, room, layout, options, issues));
            maps.Add(result.MapPath);
        }

        return new InteriorBuildResult(maps.Count, maps, issues,
            interactions, enemies, parts, items, conditional);
    }

    private void AddSpriteObjects(List<MapObjectPlacement> objects, RoomObjectData roomData,
        IReadOnlyDictionary<(int Id, int SubId), ComposedInteraction> plan, int offsetX, int offsetY)
    {
        foreach (var interaction in roomData.Interactions)
        {
            if (!plan.TryGetValue((interaction.Id, interaction.SubId), out var sprite))
                continue;

            objects.Add(new MapObjectPlacement("oracleSprite", new[]
            {
                (interaction.X + offsetX).ToString(),
                (interaction.Y + offsetY).ToString(),
                string.Join(',', sprite.FrameIds),
                string.Join(',', sprite.Durations),
                (sprite.OffsetX + offsetX).ToString(),
                (sprite.OffsetY + offsetY).ToString(),
                "2",
                sprite.LoopStart.ToString(),
                sprite.Id.ToString(),
                sprite.SubId.ToString(),
                sprite.RequiresFlag.ToString(),
                sprite.ForbiddenFlag.ToString(),
                interaction.ConditionMask.ToString(),
                interaction.RoomFlagMask.ToString(),
                interaction.RoomFlagValue.ToString(),
            }));
        }
    }

    private IReadOnlyList<MapObjectPlacement> AddWarpObjects(int group, int room,
        OracleContent.RoomLayout layout, OracleOptions options, List<string> issues)
    {
        var collisionMode = _content.GetTileset(_content.GetRoomTileset(room, options)).CollisionModeAndDungeon >> 4;
        var objects = new List<MapObjectPlacement>();

        var tileSource = _warps.FindTileWarp(group, room);
        if (tileSource != null)
        {
            if (!_warps.TryResolve(tileSource, out var destination, out var error))
            {
                issues.Add(error);
                return objects;
            }

            var map = EntityAtlas.MapName(_content.Repo, tileSource.DestinationGroup, destination.Room);
            if (map == null)
            {
                issues.Add($"room {group}:{room:x2}: warp to group {tileSource.DestinationGroup} has no generated map; skipped.");
            }
            else
            {
                var spawn = SpawnForMap(destination, tileSource.DestinationGroup, destination.Room, fallbackDirection: 3);
                for (var y = 0; y < layout.Height; y++)
                {
                    for (var x = 0; x < layout.Width; x++)
                    {
                        var metatile = layout.Tiles[y * layout.Width + x];
                        if (!_warps.IsWarpTile(collisionMode, metatile))
                            continue;
                        AddWarp(objects, x * 16, y * 16, 16, 16, map, spawn);
                    }
                }
            }
        }

        foreach (var positionSource in _warps.FindPositionWarps(group, room))
        {
            if (!_warps.TryResolve(positionSource, out var destination, out var error))
            {
                issues.Add(error);
                continue;
            }

            var map = EntityAtlas.MapName(_content.Repo, positionSource.DestinationGroup, destination.Room);
            if (map == null)
                continue;

            var x = (positionSource.Position & 0x0f) * 16;
            var y = ((positionSource.Position >> 4) & 0x0f) * 16;
            AddWarp(objects, x, y, 16, 16, map,
                SpawnForMap(destination, positionSource.DestinationGroup, destination.Room, fallbackDirection: 3));
        }

        foreach (var edgeSource in _warps.FindEdgeWarps(group, room))
        {
            if (!_warps.TryResolve(edgeSource, out var destination, out var error))
            {
                issues.Add(error);
                continue;
            }

            var map = EntityAtlas.MapName(_content.Repo, edgeSource.DestinationGroup, destination.Room);
            if (map == null)
                continue;

            var spawn = SpawnForMap(destination, edgeSource.DestinationGroup, destination.Room, fallbackDirection: 3);
            AddEdgeWarp(objects, layout, edgeSource.EdgeMask, map, spawn);
        }

        return objects;
    }

    private static void AddEdgeWarp(List<MapObjectPlacement> objects, OracleContent.RoomLayout layout,
        int edgeMask, string map, (int X, int Y, int Direction) spawn)
    {
        var half = layout.Width * 16 / 2;
        var bottom = layout.Height * 16 - 8;

        if ((edgeMask & 1) != 0)
            AddWarp(objects, 0, 0, half, 8, map, spawn);
        if ((edgeMask & 2) != 0)
            AddWarp(objects, half, 0, half, 8, map, spawn);
        if ((edgeMask & 4) != 0)
            AddWarp(objects, 0, bottom, half, 8, map, spawn);
        if ((edgeMask & 8) != 0)
            AddWarp(objects, half, bottom, half, 8, map, spawn);
    }

    private static void AddWarp(List<MapObjectPlacement> objects, int x, int y, int width, int height,
        string map, (int X, int Y, int Direction) spawn)
    {
        objects.Add(new MapObjectPlacement("oracleWarp", new[]
        {
            x.ToString(), y.ToString(), map,
            spawn.X.ToString(), spawn.Y.ToString(), spawn.Direction.ToString(),
            width.ToString(), height.ToString(),
        }));
    }

    /// <summary>
    /// Group-0/1 destinations land on the stitched overworld, so the room grid
    /// offset must be added to the native room-local YX position. Group-2/3
    /// destinations are standalone interior maps and stay local.
    /// </summary>
    public static (int X, int Y, int Direction) SpawnForMap(
        WarpDestinationRecord destination, int destinationGroup, int destinationRoom, int fallbackDirection)
    {
        var spawn = Spawn(destination, fallbackDirection);
        if (destinationGroup is 0 or 1)
        {
            spawn.X += (destinationRoom & 0x0f) * 160;
            spawn.Y += (destinationRoom >> 4) * 128;
        }
        return spawn;
    }

    /// <summary>
    /// Converts a warp destination to a DX-HD spawn point and facing direction.
    /// Transition $3 with parameter $9 enters from the bottom walking up;
    /// parameter $f enters from the top walking down. Everything else uses the
    /// packed YX tile centre.
    /// </summary>
    public static (int X, int Y, int Direction) Spawn(WarpDestinationRecord destination, int fallbackDirection)
    {
        if (destination.Transition == 3)
        {
            if (destination.Parameter == 0x9)
                return (80, 120, 1);
            if (destination.Parameter == 0xf)
                return (80, 8, 3);
        }

        if (destination.YX == 0xff)
            return (80, 64, fallbackDirection);

        var x = (destination.YX & 0x0f) * 16 + 8;
        var y = ((destination.YX >> 4) & 0x0f) * 16 + 8;
        return (x, y, fallbackDirection);
    }
}
