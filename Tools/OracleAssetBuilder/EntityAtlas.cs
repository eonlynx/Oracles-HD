namespace Oracles.Tools.AssetBuilder;

public sealed record ComposedInteraction(
    int Id,
    int SubId,
    string[] FrameIds,
    int[] Durations,
    int OffsetX,
    int OffsetY,
    int LoopStart);

/// <summary>
/// Owns the single shared entity atlas. Both the overworld and the interior
/// commands compose the same interaction set, so running either command (or
/// both in any order) produces the same oracle_&lt;game&gt;_entities.png/.atlas.
/// </summary>
public static class EntityAtlas
{
    public static string FileStem(string game) => $"oracle_{game}_entities";

    public static string ResolveMapObjectsDir(string mapsOutDir)
    {
        var parent = Directory.GetParent(Path.GetFullPath(mapsOutDir))?.FullName
                     ?? Path.GetFullPath(mapsOutDir);
        return Path.Combine(parent, "Map Objects");
    }

    public static IReadOnlyDictionary<(int Id, int SubId), ComposedInteraction> Build(
        OracleRepo repo,
        RoomObjectStreams streams,
        OracleWarpData warps,
        string mapsOutDir,
        out IReadOnlyList<string> warnings)
    {
        var spriteData = new InteractionSprites(repo);
        var atlas = new SpriteAtlasWriter();
        var result = new Dictionary<(int, int), ComposedInteraction>();
        var issues = new List<string>();

        foreach (var pair in CollectInteractions(streams, warps))
        {
            try
            {
                if (!spriteData.TryCompose(pair.Id, pair.SubId, out var frames, out var durations, out var loopStart))
                    continue;

                var idPrefix = $"oracle_{repo.Game}_i{pair.Id:x2}_{pair.SubId:x2}";
                var frameIds = new string[frames.Length];
                for (var i = 0; i < frames.Length; i++)
                {
                    frameIds[i] = $"{idPrefix}_{i:00}";
                    atlas.Add(frameIds[i], frames[i].Image);
                }

                // The native interaction position is the centre of a 16x16
                // object cell (ooa-godot: SpriteBounds = Position + (-8,-8)).
                // OAM X already includes an 8px bias; Y uses the -16 screen
                // offset, so the two anchors are not symmetric.
                result[pair] = new ComposedInteraction(
                    pair.Id, pair.SubId, frameIds, durations,
                    frames[0].OffsetX - 8, frames[0].OffsetY - 16,
                    loopStart);
            }
            catch (Exception exception)
            {
                issues.Add($"interaction ${pair.Id:x2}/${pair.SubId:x2}: {exception.Message}");
            }
        }

        var directory = ResolveMapObjectsDir(mapsOutDir);
        var stem = FileStem(repo.Game);
        atlas.Write(Path.Combine(directory, stem + ".png"), Path.Combine(directory, stem + ".atlas"));

        warnings = issues;
        return result;
    }

    /// <summary>
    /// Positioned interactions from the present overworld (group 0) and from
    /// every present interior reachable from a group-0 warp (group 2). This is
    /// deliberately a stable plan, not a function of which command runs.
    /// </summary>
    public static IReadOnlyList<(int Id, int SubId)> CollectInteractions(
        RoomObjectStreams streams, OracleWarpData warps)
    {
        var ids = new SortedSet<(int, int)>();
        for (var room = 0; room < 256; room++)
            AddRoom(streams, 0, room, ids);

        foreach (var room in InteriorRooms(warps))
            AddRoom(streams, 2, room, ids);

        return ids.ToArray();
    }

    public static SortedSet<int> InteriorRooms(OracleWarpData warps)
    {
        var rooms = new SortedSet<int>();
        foreach (var source in warps.Sources(0))
        {
            if (source.DestinationGroup != 2)
                continue;
            if (warps.TryResolve(source, out var destination, out _))
                rooms.Add(destination.Room);
        }
        return rooms;
    }

    public static string? MapName(OracleRepo repo, int group, int room)
    {
        return group switch
        {
            0 => $"oracle_{repo.Game}_g0_overworld.map",
            1 => $"oracle_{repo.Game}_g1_overworld.map",
            2 => $"oracle_{repo.Game}_g2_r{room:x2}.map",
            3 => $"oracle_{repo.Game}_g3_r{room:x2}.map",
            _ => null,
        };
    }

    private static void AddRoom(RoomObjectStreams streams, int group, int room, SortedSet<(int, int)> ids)
    {
        foreach (var interaction in streams.GetRoom(group, room).Interactions)
        {
            if (interaction.Id >= 0x200)
                continue;
            ids.Add((interaction.Id, interaction.SubId));
        }
    }
}
