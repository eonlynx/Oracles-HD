namespace Oracles.Tools.AssetBuilder;

public sealed record EnemySprite(
    int Id, int SubId, string[] FrameIds, int[] Durations,
    int OffsetX, int OffsetY, int LoopStart);

/// <summary>
/// Composes the enemy presentation atlas using the same OAM/composition code
/// as interactions. Enemy data/animations/OAM are separate native tables, but
/// the runtime consumes the same frame/duration contract.
/// </summary>
public static class EnemyAtlas
{
    private static Dictionary<(int, int), EnemySprite>? _cache;
    private static Dictionary<(int, int), EnemySprite[]>? _directionalCache;
    private static string? _cacheKey;

    // Our runtime direction order is left, up, right, down. The native
    // enemyAnimations.s entries render as right, down, left, up (verified
    // against the exported frames; ooa-godot labels them up/right/down/left).
    private static readonly int[] DirectionAnimationIndex = { 2, 3, 0, 1 };

    public static string FileStem(string game) => $"oracle_{game}_enemies";
    public static string PngPath(string mapsOutDir, string game) =>
        Path.Combine(EntityAtlas.ResolveMapObjectsDir(mapsOutDir), FileStem(game) + ".png");
    public static string AtlasPath(string mapsOutDir, string game) =>
        Path.Combine(EntityAtlas.ResolveMapObjectsDir(mapsOutDir), FileStem(game) + ".atlas");

    public static IReadOnlyDictionary<(int, int), EnemySprite> EnsureBuilt(
        OracleRepo repo, string mapsOutDir)
    {
        var key = $"{repo.Root}|{mapsOutDir}|{repo.Game}";
        if (_cache != null && _cacheKey == key)
            return _cache;

        var result = new Dictionary<(int, int), EnemySprite>();
        var directional = new Dictionary<(int, int), EnemySprite[]>();
        var sprites = new InteractionSprites(repo);
        var atlas = new SpriteAtlasWriter();
        foreach (var (id, subid) in new[] { (0x09, 0), (0x09, 1), (0x09, 2) })
        {
            if (!sprites.TryComposeEnemy(id, subid, out var frames, out var durations,
                    out var loopStart))
                continue;
            var prefix = $"oracle_{repo.Game}_e{id:x2}_{subid:x2}";
            var frameIds = new string[frames.Length];
            for (var i = 0; i < frames.Length; i++)
            {
                frameIds[i] = $"{prefix}_{i:00}";
                atlas.Add(frameIds[i], frames[i].Image);
            }
            result[(id, subid)] = new EnemySprite(id, subid, frameIds, durations,
                frames[0].OffsetX - 8, frames[0].OffsetY - 16, loopStart);

            // Compose the four native cardinal poses for the host. The
            // presentation-only fallback above can keep using one pose.
            var directions = new EnemySprite[4];
            var complete = true;
            for (var direction = 0; direction < 4; direction++)
            {
                if (!sprites.TryComposeEnemyDirection(id, subid,
                        DirectionAnimationIndex[direction], out var dirFrames,
                        out var dirDurations, out var dirLoopStart))
                {
                    complete = false;
                    break;
                }

                var dirPrefix = $"{prefix}_d{direction}";
                var dirFrameIds = new string[dirFrames.Length];
                for (var i = 0; i < dirFrames.Length; i++)
                {
                    dirFrameIds[i] = $"{dirPrefix}_{i:00}";
                    atlas.Add(dirFrameIds[i], dirFrames[i].Image);
                }
                directions[direction] = new EnemySprite(id, subid, dirFrameIds,
                    dirDurations, dirFrames[0].OffsetX - 8,
                    dirFrames[0].OffsetY - 16, dirLoopStart);
            }
            if (complete)
                directional[(id, subid)] = directions;
        }

        // Export the source-cited health/damage/radius/speed table next to the
        // atlas. The runtime still receives the values in each map object.
        var stats = new EnemyStatsTable(repo);
        stats.WriteTable(repo, mapsOutDir);
        stats.WritePartTable(repo, mapsOutDir);

        // PART18 is the shared Octorok projectile ($18). It uses the same
        // object gfx header as the Octorok but its own tile base/palette.
        if (stats.TryGetPart(0x18, out var shotStats) &&
            sprites.TryComposePart(0x18, shotStats.GfxHeader, shotStats.TileBase,
                shotStats.Palette, out var shotFrames, out var shotDurations,
                out var shotLoopStart))
        {
            var prefix = $"oracle_{repo.Game}_p18";
            var frameIds = new string[shotFrames.Length];
            for (var i = 0; i < shotFrames.Length; i++)
            {
                frameIds[i] = $"{prefix}_{i:00}";
                atlas.Add(frameIds[i], shotFrames[i].Image);
            }
            result[(0x118, 0)] = new EnemySprite(0x118, 0, frameIds, shotDurations,
                shotFrames[0].OffsetX - shotFrames[0].Image.Width / 2,
                shotFrames[0].OffsetY - shotFrames[0].Image.Height / 2,
                shotLoopStart);
        }

        var directory = EntityAtlas.ResolveMapObjectsDir(mapsOutDir);
        Directory.CreateDirectory(directory);
        var stem = FileStem(repo.Game);
        atlas.Write(Path.Combine(directory, stem + ".png"),
            Path.Combine(directory, stem + ".atlas"));
        _cache = result;
        _directionalCache = directional;
        _cacheKey = key;
        return result;
    }

    public static bool TryGet(OracleRepo repo, string mapsOutDir, int id, int subid,
        out EnemySprite sprite)
    {
        return EnsureBuilt(repo, mapsOutDir).TryGetValue((id, subid), out sprite!);
    }

    public static bool TryGetDirectional(OracleRepo repo, string mapsOutDir, int id, int subid,
        out EnemySprite[] directions)
    {
        EnsureBuilt(repo, mapsOutDir);
        return _directionalCache!.TryGetValue((id, subid), out directions!);
    }

    public static void AddPlacements(List<MapObjectPlacement> objects, OracleRepo repo,
        string mapsOutDir, RoomObjectData roomData, int offsetX, int offsetY)
    {
        var stats = new EnemyStatsTable(repo);
        foreach (var enemy in roomData.Enemies)
        {
            if (enemy.Random || enemy.X < 0 || enemy.Y < 0)
                continue;
            if (!TryGet(repo, mapsOutDir, enemy.Id, enemy.SubId, out var sprite))
                continue;

            var host = OracleEntityFactory.TemplateFor(enemy.Id, enemy.SubId);
            if (host != null && stats.TryGet(enemy.Id, enemy.SubId, out var statsRow) &&
                stats.TryGetPart(0x18, out var shotStats) &&
                TryGet(repo, mapsOutDir, 0x118, 0, out var shot) &&
                TryGetDirectional(repo, mapsOutDir, enemy.Id, enemy.SubId, out var directions))
            {
                var directionSpecs = new string[4];
                for (var direction = 0; direction < 4; direction++)
                {
                    var dir = directions[direction];
                    directionSpecs[direction] =
                        $"{string.Join(',', dir.FrameIds)}~{string.Join(',', dir.Durations)}~{dir.OffsetX}~{dir.OffsetY}";
                }

                objects.Add(new MapObjectPlacement(host, new[]
                {
                    (enemy.X + offsetX).ToString(),
                    (enemy.Y + offsetY).ToString(),
                    string.Join(',', sprite.FrameIds),
                    string.Join(',', sprite.Durations),
                    sprite.OffsetX.ToString(),
                    sprite.OffsetY.ToString(),
                    enemy.SubId.ToString(),
                    statsRow.Health.ToString(),
                    statsRow.Damage.ToString(),
                    statsRow.Speed.ToString(),
                    statsRow.CounterMask.ToString(),
                    string.Join(',', shot.FrameIds),
                    string.Join(',', shot.Durations),
                    shot.OffsetX.ToString(),
                    shot.OffsetY.ToString(),
                    shotStats.Speed.ToString(),
                    shotStats.Damage.ToString(),
                    shotStats.Lifetime.ToString(),
                    string.Join(';', directionSpecs),
                }));
                continue;
            }

            objects.Add(new MapObjectPlacement("oracleSprite", new[]
            {
                (enemy.X + offsetX).ToString(),
                (enemy.Y + offsetY).ToString(),
                string.Join(',', sprite.FrameIds),
                string.Join(',', sprite.Durations),
                sprite.OffsetX.ToString(),
                sprite.OffsetY.ToString(),
                "2",
                sprite.LoopStart.ToString(),
                enemy.Id.ToString(),
                enemy.SubId.ToString(),
                enemy.Var03.ToString(),
                "-1", "-1",
                enemy.ConditionMask.ToString(),
                enemy.RoomFlagMask.ToString(),
                enemy.RoomFlagValue.ToString(),
                "0", "0", "0", "-1",
            }));
        }
    }
}



