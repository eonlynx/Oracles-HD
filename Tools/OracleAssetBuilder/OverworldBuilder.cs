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

    public OverworldBuildResult Build(int group)
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

        for (var room = 0; room < 256; room++)
        {
            var gridX = room & 0x0F;
            var gridY = room >> 4;
            if (gridX >= grid || gridY >= grid)
                continue;

            var tilesetId = _content.GetRoomTileset(room);
            if (tilesetId >= _content.Tilesets.Count)
            {
                skipped.Add(room);
                continue;
            }

            var info = _content.GetTileset(tilesetId);
            if (!info.IsOutdoors)
            {
                skipped.Add(room);
                continue;
            }

            if (!renderers.TryGetValue(tilesetId, out var renderer))
            {
                renderer = _content.CreateRenderer(tilesetId, null);
                renderers.Add(tilesetId, renderer);
            }

            var mappings = _content.GetMappings(info.Layout);

            OracleContent.RoomLayout layout;
            try
            {
                layout = _content.GetRoomLayout(info.LayoutGroup, room, largeRoom: false);
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
                    layer[gridX * RoomWidth + x, gridY * RoomHeight + y] = atlasIndex;
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

        DxMapWriter.Write(mapPath, tilesetName, width, height, layers);

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
