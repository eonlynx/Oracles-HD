using System.Text.Json;

namespace Oracles.Tools.AssetBuilder;

public sealed record OverworldBuildResult(
    string MapPath,
    string TilesetPath,
    string PreviewPath,
    string ManifestPath,
    int Rooms,
    int SkippedRooms,
    int WidthTiles,
    int HeightTiles,
    int AtlasMetatiles);

/// <summary>
/// Stitches every OUTDOORS room of a group into one continuous DX-HD map.
/// Grid position is the native Oracle room id: x = id &amp; 0x0f, y = id &gt;&gt; 4.
/// </summary>
public sealed class OverworldBuilder
{
    private const int RoomWidth = 10;
    private const int RoomHeight = 8;

    private readonly OracleContent _content;
    private readonly string _outDir;
    private readonly string _tilesetsDir;

    public OverworldBuilder(OracleContent content, string outDir, string tilesetsDir)
    {
        _content = content;
        _outDir = outDir;
        _tilesetsDir = tilesetsDir;
    }

    public OverworldBuildResult Build(int group, OracleOptions options)
    {
        var game = _content.Repo.Game;
        var grid = game == "ages" ? 14 : 16;
        var width = grid * RoomWidth;
        var height = grid * RoomHeight;

        var layer = new int[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                layer[x, y] = -1;

        var atlas = new MetatileAtlas(64);
        var renderers = new Dictionary<int, TilesetRenderer>();
        var usedRooms = new List<int>();
        var skipped = new List<int>();
        var composer = new AnimationComposer(_content.Animation);
        var collisionMap = new byte[width, height];
        var terrainMap = new byte[width, height];
        var placements = new List<MapAnimationPlacement>();

        // Outside rooms and the skipped indoor cell are solid so Link cannot
        // walk into the void until interiors are generated.
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                collisionMap[x, y] = 0xFF;

        for (var room = 0; room < 256; room++)
        {
            var gridX = room & 0x0F;
            var gridY = room >> 4;
            if (gridX >= grid || gridY >= grid)
                continue;

            var tilesetId = _content.GetRoomTileset(room, options);
            if (tilesetId >= _content.Tilesets.Count)
            {
                skipped.Add(room);
                continue;
            }

            var info = _content.GetTileset(tilesetId);
            // TILESETFLAG_MAKU marks the Maku Tree screen; it has no OUTDOORS
            // bit but is part of the overworld map (top/ooa-godot map it to 0x38).
            if (!info.IsOutdoors && !info.IsMaku)
            {
                skipped.Add(room);
                continue;
            }

            if (!renderers.TryGetValue(tilesetId, out var renderer))
            {
                renderer = _content.CreateRenderer(tilesetId, null, room, options);
                renderers.Add(tilesetId, renderer);
            }

            var mappings = _content.GetMappings(info.Layout);
            var collisions = _content.GetCollisions(info.Layout);
            var collisionMode = info.CollisionModeAndDungeon >> 4;

            OracleContent.RoomLayout layout;
            try
            {
                layout = _content.GetRoomLayout(_content.GetLayoutGroup(room, info, options), room, largeRoom: false);
            }
            catch (FileNotFoundException)
            {
                skipped.Add(room);
                continue;
            }

            usedRooms.Add(room);

            for (var y = 0; y < layout.Height; y++)
            {
                for (var x = 0; x < layout.Width; x++)
                {
                    var metatile = layout.Tiles[y * layout.Width + x];
                    var atlasIndex = atlas.GetOrAdd(tilesetId, metatile, () => renderer.RenderMetatile(mappings, metatile));

                    var mapX = gridX * RoomWidth + x;
                    var mapY = gridY * RoomHeight + y;
                    layer[mapX, mapY] = atlasIndex;

                    var terrain = _content.GetTerrain(collisionMode, metatile);
                    terrainMap[mapX, mapY] = terrain;
                    var collisionByte = collisions[metatile];
                    collisionMap[mapX, mapY] = terrain is 1 or 2 or 3 or 5
                        ? (byte)0xFF
                        : collisionByte < 0x10 ? collisionByte : (byte)0;

                    var variants = composer.GetVariants(tilesetId, metatile, info.Animation, mappings, renderer);
                    if (variants != null)
                    {
                        var frames = new int[variants.Count];
                        var durations = new int[variants.Count];
                        for (var i = 0; i < variants.Count; i++)
                        {
                            frames[i] = atlas.GetOrAddKey(
                                AnimationComposer.VariantKey(tilesetId, metatile, info.Animation, i),
                                () => variants[i].Image);
                            durations[i] = variants[i].DurationMs;
                        }
                        placements.Add(new MapAnimationPlacement(mapX, mapY, frames, durations));
                    }
                }
            }
        }

        var emptyLayer = EmptyLayer(width, height);
        var layers = new List<int[,]> { layer, emptyLayer, EmptyLayer(width, height) };

        var stem = $"oracle_{game}_g{group}_overworld";
        var tilesetName = stem + ".png";
        var mapPath = Path.Combine(_outDir, stem + ".map");
        var tilesetPath = Path.Combine(_tilesetsDir, tilesetName);
        var previewPath = Path.Combine(_outDir, "Previews", stem + ".png");
        var manifestPath = Path.Combine(_outDir, stem + ".manifest.json");

        Directory.CreateDirectory(_tilesetsDir);
        atlas.SavePng(tilesetPath);

        DxMapWriter.Write(mapPath, tilesetName, width, height, layers, collisionMap, terrainMap, placements);

        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
        var preview = RenderPreview(layer, atlas);
        Png.SaveRgba(previewPath, preview);

        WriteManifest(manifestPath, stem, group, usedRooms, skipped, mapPath, tilesetPath, previewPath, atlas.Count);

        return new OverworldBuildResult(
            mapPath, tilesetPath, previewPath, manifestPath,
            usedRooms.Count, skipped.Count, width, height, atlas.Count);
    }

    private static RgbaImage RenderPreview(int[,] layer, MetatileAtlas atlas)
    {
        var width = layer.GetLength(0);
        var height = layer.GetLength(1);
        var image = new RgbaImage(width * TilesetRenderer.MetatileSize, height * TilesetRenderer.MetatileSize);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = layer[x, y];
                var originX = x * TilesetRenderer.MetatileSize;
                var originY = y * TilesetRenderer.MetatileSize;

                if (index < 0)
                {
                    for (var py = 0; py < TilesetRenderer.MetatileSize; py++)
                        for (var px = 0; px < TilesetRenderer.MetatileSize; px++)
                            image[originX + px, originY + py] = new Rgba(0, 0, 0, 255);
                    continue;
                }

                image.CopyFrom(atlas.GetImage(index), originX, originY);
            }
        }

        return image;
    }

    private static int[,] EmptyLayer(int width, int height)
    {
        var layer = new int[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                layer[x, y] = -1;
        return layer;
    }

    private void WriteManifest(
        string path,
        string stem,
        int group,
        IReadOnlyList<int> usedRooms,
        IReadOnlyList<int> skippedRooms,
        string mapPath,
        string tilesetPath,
        string previewPath,
        int atlasMetatiles)
    {
        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 2,
            game = _content.Repo.Game,
            entry = stem,
            group,
            rooms = usedRooms.Count,
            skippedRooms = skippedRooms.Count,
            atlasMetatiles,
            roomIds = usedRooms.Select(r => $"0x{r:x2}").ToArray(),
            skippedRoomIds = skippedRooms.Select(r => $"0x{r:x2}").ToArray(),
            outputs = new[] { mapPath, tilesetPath, previewPath }
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new
                {
                    path = Path.GetRelativePath(_outDir, p).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(p),
                })
                .ToArray(),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
