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
    private static string? _cacheKey;

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
        }

        var directory = EntityAtlas.ResolveMapObjectsDir(mapsOutDir);
        Directory.CreateDirectory(directory);
        var stem = FileStem(repo.Game);
        atlas.Write(Path.Combine(directory, stem + ".png"),
            Path.Combine(directory, stem + ".atlas"));
        _cache = result;
        _cacheKey = key;
        return result;
    }

    public static bool TryGet(OracleRepo repo, string mapsOutDir, int id, int subid,
        out EnemySprite sprite)
    {
        return EnsureBuilt(repo, mapsOutDir).TryGetValue((id, subid), out sprite!);
    }

    public static void AddPlacements(List<MapObjectPlacement> objects, OracleRepo repo,
        string mapsOutDir, RoomObjectData roomData, int offsetX, int offsetY)
    {
        foreach (var enemy in roomData.Enemies)
        {
            if (enemy.Random || enemy.X < 0 || enemy.Y < 0)
                continue;
            if (!TryGet(repo, mapsOutDir, enemy.Id, enemy.SubId, out var sprite))
                continue;

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



