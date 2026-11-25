using System.Text.Json;

namespace Oracles.Tools.AssetBuilder;

public sealed record RoomBuildResult(
    string MapPath,
    string TilesetPath,
    string PreviewPath,
    string ManifestPath,
    int TilesetId,
    int Layout,
    int LayoutGroup,
    int DistinctMetatiles,
    int MissingPaletteMetatiles,
    int MissingTileMetatiles);

public sealed class RoomBuilder
{
    private readonly OracleContent _content;
    private readonly string _outDir;
    private readonly string _tilesetsDir;

    public RoomBuilder(OracleContent content, string outDir, string tilesetsDir)
    {
        _content = content;
        _outDir = outDir;
        _tilesetsDir = tilesetsDir;
    }

    public RoomBuildResult Build(int group, int room, bool largeRoom, int? backgroundPaletteHeader, OracleOptions options,
        IReadOnlyList<MapObjectPlacement>? objects = null,
        Func<OracleContent.RoomLayout, IReadOnlyList<MapObjectPlacement>>? objectFactory = null,
        int floor = -1, int dungeon = -1)
    {
        var tilesetId = _content.GetRoomTileset(room, options);
        var info = _content.GetTileset(tilesetId);
        var mappings = _content.GetMappings(info.Layout);
        var layout = _content.GetRoomLayout(_content.GetLayoutGroup(room, info, options), room, largeRoom);
        var width = layout.Width;
        var height = layout.Height;

        if (objectFactory != null)
        {
            var extra = objectFactory(layout);
            if (extra.Count > 0)
                objects = (objects ?? Array.Empty<MapObjectPlacement>()).Concat(extra).ToArray();
        }
        var renderer = _content.CreateRenderer(tilesetId, backgroundPaletteHeader, room, options);

        var atlas = new MetatileAtlas(64);
        for (var metatile = 0; metatile < 256; metatile++)
            atlas.GetOrAdd(tilesetId, metatile, () => renderer.RenderMetatile(mappings, metatile));

        var collisions = _content.GetCollisions(info.Layout);
        var collisionMode = info.CollisionModeAndDungeon >> 4;
        var composer = new AnimationComposer(_content.Animation);
        var collisionMap = new byte[width, height];
        var terrainMap = new byte[width, height];
        var placements = new List<MapAnimationPlacement>();

        var layer0 = new int[width, height];
        var distinct = new HashSet<int>();
        var missingPalette = new HashSet<int>();
        var missingTiles = new HashSet<int>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var metatile = layout.Tiles[y * width + x];
                distinct.Add(metatile);
                layer0[x, y] = atlas.GetOrAdd(tilesetId, metatile, () => renderer.RenderMetatile(mappings, metatile));

                var terrain = _content.GetTerrain(collisionMode, metatile);
                terrainMap[x, y] = terrain;
                var collisionByte = collisions[metatile];
                collisionMap[x, y] = terrain is 1 or 2 or 3 or 5
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
                    placements.Add(new MapAnimationPlacement(x, y, frames, durations));
                }

                for (var part = 0; part < 4; part++)
                {
                    var tile = mappings[metatile * 8 + part];
                    var attributes = mappings[metatile * 8 + 4 + part];
                    if (!renderer.IsPaletteLoaded(attributes & 0x07))
                        missingPalette.Add(metatile);
                    if (!renderer.IsTileLoaded(tile, (attributes & 0x08) != 0 ? 1 : 0))
                        missingTiles.Add(metatile);
                }
            }
        }

        var layer1 = EmptyLayer(width, height);
        var layer2 = EmptyLayer(width, height);
        var layers = new List<int[,]> { layer0, layer1, layer2 };

        var stem = $"oracle_{_content.Repo.Game}_g{group:x1}_r{room:x2}";
        var tilesetName = $"oracle_{_content.Repo.Game}_t{tilesetId:x2}.png";
        var mapPath = Path.Combine(_outDir, stem + ".map");
        var tilesetPath = Path.Combine(_tilesetsDir, tilesetName);
        var previewPath = Path.Combine(_outDir, "Previews", stem + ".png");
        var manifestPath = Path.Combine(_outDir, stem + ".manifest.json");

        Directory.CreateDirectory(_tilesetsDir);
        atlas.SavePng(tilesetPath);

        var kind = group switch
        {
            2 => "interior",
            4 or 5 => "dungeon",
            _ => "room",
        };
        // Match DX-HD: follow by default in every map, including houses. A
        // future puzzle can set Map.CameraTarget explicitly.
        var camera = "follow";
        var mapInfo = OracleMapInfo.SingleRoom(_content.Repo.Game, group, room,
            width, height, kind, camera, _content.GetRoomEntryEffects(group),
            floor, dungeon);

        DxMapWriter.Write(mapPath, tilesetName, width, height, layers, collisionMap, terrainMap, placements, objects, mapInfo);

        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
        var preview = _content.RenderRoomPreview(layout, atlas, tilesetId);
        Png.SaveRgba(previewPath, preview);

        WriteManifest(manifestPath, stem, group, room, info, renderer, layout.SourcePath, mapPath, tilesetPath, previewPath);

        return new RoomBuildResult(
            mapPath, tilesetPath, previewPath, manifestPath,
            tilesetId, info.Layout, info.LayoutGroup,
            distinct.Count, missingPalette.Count, missingTiles.Count);
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
        int room,
        TilesetInfo info,
        TilesetRenderer renderer,
        string layoutPath,
        string mapPath,
        string tilesetPath,
        string previewPath)
    {
        var inputs = new List<string>
        {
            Path.Combine(_content.Repo.DataDir, "gfxHeaders.s"),
            Path.Combine(_content.Repo.DataDir, "uniqueGfxHeaders.s"),
            Path.Combine(_content.Repo.DataDir, "paletteHeaders.s"),
            Path.Combine(_content.Repo.DataDir, "paletteData.s"),
            Path.Combine(_content.Repo.DataDir, "tilesets.s"),
            Path.Combine(_content.Repo.RoomsDir, $"group{group}Tilesets.bin"),
            _content.GetMappingPath(info.Layout),
            _content.GetCollisionPath(info.Layout),
            layoutPath,
        };
        inputs.AddRange(renderer.SourcePngs);
        inputs.AddRange(_content.GetImportedSourcePaths());

        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 2,
            game = _content.Repo.Game,
            entry = stem,
            group,
            room = $"0x{room:x2}",
            tileset = new
            {
                id = $"0x{info.Index:x2}",
                layout = $"0x{info.Layout:x2}",
                layoutGroup = $"0x{info.LayoutGroup:x2}",
                gfxHeader = $"0x{info.Gfx:x2}",
                uniqueGfxHeader = $"0x{info.UniqueGfx:x2}",
                paletteHeader = $"0x{info.Palette:x2}",
                appliedPaletteHeader = $"0x{info.Palette:x2}",
                flags = $"0x{info.Flags:x2}",
                outdoors = info.IsOutdoors,
                indoors = info.IsIndoors,
                sideScroll = info.IsSideScroll,
                underwater = info.IsUnderwater,
            },
            inputs = inputs
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new
                {
                    path = Path.GetRelativePath(_content.Repo.Root, p).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(p),
                })
                .ToArray(),
            outputs = new[]
                {
                    mapPath, tilesetPath, previewPath,
                    Path.Combine(EntityAtlas.ResolveMapObjectsDir(_outDir), EntityAtlas.FileStem(_content.Repo.Game) + ".png"),
                    Path.Combine(EntityAtlas.ResolveMapObjectsDir(_outDir), EntityAtlas.FileStem(_content.Repo.Game) + ".atlas"),
                    EntityAtlas.BehaviorTablePath(_outDir, _content.Repo.Game),
                    ScriptImporter.TablePath(_outDir, _content.Repo.Game),
                    ScriptImporter.BindingTablePath(_outDir, _content.Repo.Game),
                    EntityPlacements.TablePath(_outDir, _content.Repo.Game),
                    InteractionVisibility.TablePath(_outDir, _content.Repo.Game),
                    EnemyAtlas.PngPath(_outDir, _content.Repo.Game),
                    EnemyAtlas.AtlasPath(_outDir, _content.Repo.Game),
                    OracleDungeons.DungeonTablePath(_outDir, _content.Repo.Game),
                    OracleDungeons.LayoutTablePath(_outDir, _content.Repo.Game),
                }
                .Where(File.Exists)
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
