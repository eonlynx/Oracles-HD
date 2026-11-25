using System.Text;
using System.Text.Json;

namespace Oracles.Tools.AssetBuilder;

public sealed record ComposedInteraction(
    int Id,
    int SubId,
    string[] FrameIds,
    int[] Durations,
    int OffsetX,
    int OffsetY,
    int LoopStart,
    int RequiresFlag,
    int ForbiddenFlag,
    bool TimeWarp,
    bool SolidTile,
    int RadiusY,
    int RadiusX,
    int RequiresEntryDirection);

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
        var behaviors = new InteractionBehaviors(repo);
        var atlas = new SpriteAtlasWriter();
        var result = new Dictionary<(int, int), ComposedInteraction>();
        var issues = new List<string>();
        var paletteHeadersPath = Path.Combine(repo.DataDir, "paletteHeaders.s");
        var paletteDataPath = Path.Combine(repo.DataDir, "paletteData.s");
        var paletteData = AsmParser.ParsePaletteData(paletteDataPath);
        var interactionPairs = CollectInteractions(streams, warps, repo.Game);

        foreach (var pair in interactionPairs)
        {
            try
            {
                var behavior = behaviors.For(pair.Id, pair.SubId);
                var paletteOverrides = behavior.SpritePaletteHeader >= 0
                    ? LoadSpritePaletteOverrides(paletteHeadersPath, paletteData, behavior.SpritePaletteHeader)
                    : null;
                if (paletteOverrides is { Count: 0 })
                    paletteOverrides = null;

                if (!spriteData.TryCompose(pair.Id, pair.SubId, out var frames, out var durations,
                        out var loopStart, paletteOverrides))
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
                    loopStart, behavior.RequiresFlag, behavior.ForbiddenFlag,
                    behavior.TimeWarp, behavior.SolidTile,
                    behavior.RadiusY, behavior.RadiusX, behavior.RequiresEntryDirection);
            }
            catch (Exception exception)
            {
                issues.Add($"interaction ${pair.Id:x2}/${pair.SubId:x2}: {exception.Message}");
            }
        }

        var directory = ResolveMapObjectsDir(mapsOutDir);
        var stem = FileStem(repo.Game);
        atlas.Write(Path.Combine(directory, stem + ".png"), Path.Combine(directory, stem + ".atlas"));
        WriteBehaviorTable(repo, behaviors, interactionPairs, mapsOutDir);
        ScriptImporter.Build(repo, mapsOutDir, out var scriptWarnings);
        issues.AddRange(scriptWarnings);
        EntityPlacements.Build(repo, streams, mapsOutDir, out var placementWarnings);
        issues.AddRange(placementWarnings);
        InteractionVisibility.Build(repo, mapsOutDir);
        EnemyAtlas.EnsureBuilt(repo, mapsOutDir);

        warnings = issues;
        return result;
    }

    public static string BehaviorTablePath(string mapsOutDir, string game)
    {
        return Path.Combine(ResolveMapObjectsDir(mapsOutDir),
            $"oracle_{game}_interaction_behaviors.tsv");
    }

    private static void WriteBehaviorTable(OracleRepo repo, InteractionBehaviors behaviors,
        IReadOnlyList<(int Id, int SubId)> pairs, string mapsOutDir)
    {
        var tablePath = BehaviorTablePath(mapsOutDir, repo.Game);
        var manifestPath = Path.ChangeExtension(tablePath, ".manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(tablePath)!);

        var builder = new StringBuilder();
        builder.Append("id\tsubid\tsolid\tradius_y\tradius_x\trequires_flag\tforbidden_flag\tpalette_header\ttime_warp\tentry_direction\n");
        foreach (var pair in pairs.OrderBy(p => p.Id).ThenBy(p => p.SubId))
        {
            var behavior = behaviors.For(pair.Id, pair.SubId);
            builder.Append($"0x{pair.Id:x2}\t0x{pair.SubId:x2}\t{(behavior.SolidTile ? 1 : 0)}\t");
            builder.Append($"{behavior.RadiusY}\t{behavior.RadiusX}\t");
            builder.Append($"{behavior.RequiresFlag}\t{behavior.ForbiddenFlag}\t");
            builder.Append(behavior.SpritePaletteHeader >= 0 ? $"0x{behavior.SpritePaletteHeader:x2}" : "-1");
            builder.Append($"\t{(behavior.TimeWarp ? 1 : 0)}\t{behavior.RequiresEntryDirection}\n");
        }
        File.WriteAllText(tablePath, builder.ToString(), new UTF8Encoding(false));

        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "interaction_behaviors",
            rows = pairs.Count,
            inputs = behaviors.SourcePaths
                .Select(path => new
                {
                    path = Path.GetRelativePath(repo.Root, path).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(path),
                })
                .ToArray(),
            outputs = new[]
                {
                    new
                    {
                        path = Path.GetRelativePath(mapsOutDir, tablePath).Replace('\\', '/'),
                        sha256 = OracleRepo.Sha256(tablePath),
                    },
                },
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    /// <summary>
    /// Positioned interactions from every game overworld and from every
    /// present interior reachable from a group-0 warp. This is deliberately a
    /// stable plan, not a function of which command runs.
    /// </summary>
    public static IReadOnlyList<(int Id, int SubId)> CollectInteractions(
        RoomObjectStreams streams, OracleWarpData warps, string game)
    {
        var ids = new SortedSet<(int, int)>();
        foreach (var group in OracleGameConfig.OverworldGroups(game))
            for (var room = 0; room < 256; room++)
                AddRoom(streams, group, room, ids);

        foreach (var room in InteriorRooms(warps, OracleGameConfig.InteriorGroup(game)))
            AddRoom(streams, OracleGameConfig.InteriorGroup(game), room, ids);

        if (OracleGameConfig.SupportsTimeTravel(game))
            foreach (var room in InteriorRooms(warps, 3))
                AddRoom(streams, 3, room, ids);

        foreach (var group in new[] { 4, 5 })
            for (var room = 0; room < 256; room++)
                AddRoom(streams, group, room, ids);

        return ids.ToArray();
    }

    public static SortedSet<int> InteriorRooms(OracleWarpData warps, int destinationGroup = 2)
    {
        var rooms = new SortedSet<int>();
        var sourceGroups = destinationGroup == 3 ? new[] { 1 } : new[] { 0, 1 };
        foreach (var sourceGroup in sourceGroups)
            foreach (var source in warps.Sources(sourceGroup))
            {
                if (source.DestinationGroup != destinationGroup)
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
            4 or 5 => $"oracle_{repo.Game}_g{group}_r{room:x2}.map",
            _ => null,
        };
    }

    private static Dictionary<int, Rgba[]> LoadSpritePaletteOverrides(
        string paletteHeadersPath, IReadOnlyDictionary<string, Rgb555[]> paletteData, int header)
    {
        var result = new Dictionary<int, Rgba[]>();
        foreach (var entry in AsmParser.ParsePaletteHeaderSpr(paletteHeadersPath, header))
        {
            if (!paletteData.TryGetValue(entry.DataLabel, out var colors))
                continue;

            for (var palette = 0; palette < entry.Count; palette++)
            {
                var target = entry.Start + palette;
                if (target is < 0 or > 7 || (palette + 1) * 4 > colors.Length)
                    continue;

                result[target] = new[]
                {
                    new Rgba(0, 0, 0, 0),
                    ToRgba(colors[palette * 4 + 1]),
                    ToRgba(colors[palette * 4 + 2]),
                    ToRgba(colors[palette * 4 + 3]),
                };
            }
        }
        return result;
    }

    private static Rgba ToRgba(Rgb555 color) => new(
        (byte)(color.R * 255 / 31),
        (byte)(color.G * 255 / 31),
        (byte)(color.B * 255 / 31),
        255);

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
