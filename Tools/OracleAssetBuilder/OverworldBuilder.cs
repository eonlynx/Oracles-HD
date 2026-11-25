using System.Text;
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
    int AtlasMetatiles,
    int Interactions,
    int Enemies,
    int Parts,
    int Items,
    int Conditional);

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
        var interactions = 0;
        var enemies = 0;
        var parts = 0;
        var items = 0;
        var conditional = 0;
        var composer = new AnimationComposer(_content.Animation);
        var collisionMap = new byte[width, height];
        var terrainMap = new byte[width, height];
        var placements = new List<MapAnimationPlacement>();
        var mapObjects = new List<MapObjectPlacement>();

        // One shared entity atlas and one object plan for both the overworld
        // and the interior command.
        var warps = new OracleWarpData(_content.Repo);
        var streams = new RoomObjectStreams(_content.Repo);
        var spritePlan = EntityAtlas.Build(_content.Repo, streams, warps, _outDir, out var spriteWarnings);
        var warnings = new List<string>(spriteWarnings);

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
                // Present Maku Tree rooms keep the wood palette installed by
                // the native interaction (makuTree.s:PALH_8f). The past uses
                // its own tileset/palette and must not be rewritten.
                if (group == 0 && _content.UsesMakuTreeWoodPalette(info))
                    _content.ApplyMakuTreePalette(renderer);
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
                    var atlasIndex = atlas.GetOrAdd(tilesetId, metatile,
                        () => renderer.RenderMetatile(mappings, metatile));

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

            var roomData = streams.GetRoom(group, room);
            interactions += roomData.Interactions.Count;
            enemies += roomData.Enemies.Count;
            parts += roomData.Parts.Count;
            items += roomData.Items.Count;
            conditional += roomData.ConditionalCount;

            AddRoomObjects(mapObjects, group, room, roomData, gridX, gridY, layout, collisionMode,
                spritePlan, warps, warnings);
        }

        var tileChanges = new OracleTileChanges(_content.Repo);
        tileChanges.WriteRawTable(_content.Repo, _outDir);
        BakeTileChanges(group, tileChanges, usedRooms, renderers, atlas, options, warnings);

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

        DxMapWriter.Write(mapPath, tilesetName, width, height, layers, collisionMap, terrainMap,
            placements, mapObjects, OracleMapInfo.Overworld(_content.Repo.Game, group, grid,
                _content.GetRoomEntryEffects(group)));

        foreach (var warning in warnings)
            Console.WriteLine($"  warning: {warning}");

        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
        var preview = RenderPreview(layer, atlas);
        Png.SaveRgba(previewPath, preview);

        WriteManifest(manifestPath, stem, group, usedRooms, skipped, mapPath, tilesetPath, previewPath, atlas.Count);

        return new OverworldBuildResult(
            mapPath, tilesetPath, previewPath, manifestPath,
            usedRooms.Count, skipped.Count, width, height, atlas.Count,
            interactions, enemies, parts, items, conditional);
    }

    private void BakeTileChanges(
        int group,
        OracleTileChanges changes,
        IReadOnlyList<int> usedRooms,
        IReadOnlyDictionary<int, TilesetRenderer> renderers,
        MetatileAtlas atlas,
        OracleOptions options,
        List<string> warnings)
    {
        var used = new HashSet<int>(usedRooms);
        var rows = new List<string>();
        foreach (var change in changes.Changes
                     .Where(c => c.Group == group && used.Contains(c.Room))
                     .OrderBy(c => c.Room).ThenBy(c => c.Position))
        {
            var tilesetId = _content.GetRoomTileset(change.Room, options);
            if (!renderers.TryGetValue(tilesetId, out var renderer))
            {
                warnings.Add($"tile change room {change.Room:x2}: renderer for tileset " +
                             $"{tilesetId:x2} is missing; skipped.");
                continue;
            }

            var info = _content.GetTileset(tilesetId);
            var layout = _content.GetRoomLayout(
                _content.GetLayoutGroup(change.Room, info, options), change.Room,
                largeRoom: false);
            var localX = change.Position & 0x0F;
            var localY = change.Position >> 4;
            if (localX >= layout.Width || localY >= layout.Height)
            {
                warnings.Add($"tile change room {change.Room:x2}: position " +
                             $"{change.Position:x2} is outside {layout.Width}x{layout.Height}.");
                continue;
            }

            var mappings = _content.GetMappings(info.Layout);
            var collisions = _content.GetCollisions(info.Layout);
            var collisionMode = info.CollisionModeAndDungeon >> 4;
            var baseMetatile = layout.Tiles[localY * layout.Width + localX];
            var baseAtlas = atlas.GetOrAdd(tilesetId, baseMetatile,
                () => renderer.RenderMetatile(mappings, baseMetatile));
            var replacementAtlas = atlas.GetOrAdd(tilesetId, change.Tile,
                () => renderer.RenderMetatile(mappings, change.Tile));
            var baseTerrain = _content.GetTerrain(collisionMode, baseMetatile);
            var replacementTerrain = _content.GetTerrain(collisionMode, change.Tile);

            byte Collision(int metatile, int terrain)
            {
                var value = collisions[metatile];
                return terrain is 1 or 2 or 3 or 5
                    ? (byte)0xFF
                    : value < 0x10 ? value : (byte)0;
            }

            var mapX = (change.Room & 0x0F) * RoomWidth + localX;
            var mapY = (change.Room >> 4) * RoomHeight + localY;
            rows.Add(string.Join("\t",
                $"0x{change.Room:x2}", $"0x{change.Mask:x2}",
                $"0x{mapX:x2}", $"0x{mapY:x2}",
                $"0x{baseMetatile:x2}", $"0x{change.Tile:x2}",
                $"0x{baseAtlas:x}", $"0x{replacementAtlas:x}",
                $"0x{Collision(baseMetatile, baseTerrain):x2}",
                $"0x{Collision(change.Tile, replacementTerrain):x2}",
                $"0x{baseTerrain:x2}", $"0x{replacementTerrain:x2}",
                change.Source));
        }

        var path = OracleTileChanges.BakedTablePath(_outDir, _content.Repo.Game, group);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new StringBuilder();
        builder.Append("room\tmask\tx\ty\tbase_metatile\treplacement_metatile\t");
        builder.Append("base_atlas\treplacement_atlas\tbase_collision\t");
        builder.Append("replacement_collision\tbase_terrain\treplacement_terrain\tsource\n");
        foreach (var row in rows)
            builder.Append(row).Append('\n');
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private void AddRoomObjects(
        List<MapObjectPlacement> mapObjects,
        int group,
        int room,
        RoomObjectData roomData,
        int gridX,
        int gridY,
        OracleContent.RoomLayout layout,
        int collisionMode,
        IReadOnlyDictionary<(int Id, int SubId), ComposedInteraction> spritePlan,
        OracleWarpData warps,
        List<string> warnings)
    {
        const int roomWidth = RoomWidth;
        const int roomHeight = RoomHeight;

        foreach (var interaction in roomData.Interactions)
        {
            if (interaction.X < 0 || interaction.Y < 0)
                continue;
            if (interaction.Id >= 0x200 ||
                !spritePlan.TryGetValue((interaction.Id, interaction.SubId), out var sprite))
                continue;

            // Any interaction whose source calls interactionBeginTimewarp is a
            // time portal; the runtime owns activation and the era transition,
            // so it gets a dedicated object instead of a presentation sprite.
            if (sprite.TimeWarp)
            {
                mapObjects.Add(new MapObjectPlacement("oracleTimePortal", new[]
                {
                    (gridX * roomWidth * 16 + interaction.X).ToString(),
                    (gridY * roomHeight * 16 + interaction.Y + InteractionVisibility.PositionOffsetY(interaction.Id, interaction.SubId, interaction.Var03)).ToString(),
                    string.Join(',', sprite.FrameIds),
                    string.Join(',', sprite.Durations),
                    sprite.OffsetX.ToString(),
                    sprite.OffsetY.ToString(),
                    "2",
                    sprite.LoopStart.ToString(),
                    interaction.SubId.ToString(),
                }));
                continue;
            }

            var hasRules = InteractionVisibility.HasRules(
                interaction.Id, interaction.SubId, interaction.Var03);
            var requires = hasRules ? -1 : sprite.RequiresFlag;
            var forbidden = hasRules ? -1 : sprite.ForbiddenFlag;

            mapObjects.Add(new MapObjectPlacement("oracleSprite", new[]
            {
                (gridX * roomWidth * 16 + interaction.X).ToString(),
                (gridY * roomHeight * 16 + interaction.Y + InteractionVisibility.PositionOffsetY(interaction.Id, interaction.SubId, interaction.Var03)).ToString(),
                string.Join(',', sprite.FrameIds),
                string.Join(',', sprite.Durations),
                sprite.OffsetX.ToString(),
                sprite.OffsetY.ToString(),
                "2",
                sprite.LoopStart.ToString(),
                sprite.Id.ToString(),
                sprite.SubId.ToString(),
                interaction.Var03.ToString(),
                requires.ToString(),
                forbidden.ToString(),
                interaction.ConditionMask.ToString(),
                interaction.RoomFlagMask.ToString(),
                interaction.RoomFlagValue.ToString(),
                sprite.SolidTile ? "1" : "0",
                sprite.RadiusY.ToString(),
                sprite.RadiusX.ToString(),
                sprite.RequiresEntryDirection.ToString(),
            }));
        }

        EnemyAtlas.AddPlacements(mapObjects, _content.Repo, _outDir, roomData,
            gridX * roomWidth * 16, gridY * roomHeight * 16);

        // Warps that leave the stitched overworld for an interior, past
        // interior, or dungeon. Edge warps between overworld groups stay
        // continuous and need no object.
        var tileWarp = warps.FindTileWarp(group, room);
        if (tileWarp != null && tileWarp.DestinationGroup is >= 2 and <= 5)
        {
            if (!warps.TryResolve(tileWarp, out var tileDestination, out var tileError))
            {
                warnings.Add(tileError);
            }
            else
            {
                var target = EntityAtlas.MapName(_content.Repo, tileWarp.DestinationGroup, tileDestination.Room);
                if (target != null)
                {
                    var spawn = InteriorBuilder.SpawnForMap(tileDestination,
                        tileWarp.DestinationGroup, tileDestination.Room, fallbackDirection: 3);
                    for (var y = 0; y < layout.Height; y++)
                        for (var x = 0; x < layout.Width; x++)
                        {
                            var metatile = layout.Tiles[y * layout.Width + x];
                            if (!warps.IsWarpTile(collisionMode, metatile))
                                continue;
                            AddWarp(mapObjects,
                                gridX * roomWidth * 16 + x * 16,
                                gridY * roomHeight * 16 + y * 16,
                                16, 16, target, spawn,
                                tileWarp.SourceTransition | (tileDestination.Transition << 4));
                        }
                }
            }
        }

        foreach (var positionSource in warps.FindPositionWarps(group, room))
        {
            if (positionSource.DestinationGroup is < 2 or > 5)
                continue;
            if (!warps.TryResolve(positionSource, out var destination, out var error))
            {
                warnings.Add(error);
                continue;
            }
            var target = EntityAtlas.MapName(_content.Repo, positionSource.DestinationGroup, destination.Room);
            if (target == null)
                continue;
            AddWarp(mapObjects,
                gridX * roomWidth * 16 + (positionSource.Position & 0x0f) * 16,
                gridY * roomHeight * 16 + ((positionSource.Position >> 4) & 0x0f) * 16,
                16, 16, target,
                InteriorBuilder.SpawnForMap(destination, positionSource.DestinationGroup,
                    destination.Room, fallbackDirection: 3),
                positionSource.SourceTransition | (destination.Transition << 4));
        }

        foreach (var edgeSource in warps.FindEdgeWarps(group, room))
        {
            if (edgeSource.DestinationGroup is < 2 or > 5)
                continue;
            if (!warps.TryResolve(edgeSource, out var destination, out var error))
            {
                warnings.Add(error);
                continue;
            }
            var target = EntityAtlas.MapName(_content.Repo, edgeSource.DestinationGroup, destination.Room);
            if (target == null)
                continue;
            var spawn = InteriorBuilder.SpawnForMap(destination, edgeSource.DestinationGroup,
                destination.Room, fallbackDirection: 3);
            var transition = edgeSource.SourceTransition | (destination.Transition << 4);
            var half = roomWidth * 16 / 2;
            var bottom = gridY * roomHeight * 16 + roomHeight * 16 - 8;
            if ((edgeSource.EdgeMask & 1) != 0)
                AddWarp(mapObjects, gridX * roomWidth * 16, gridY * roomHeight * 16, half, 8, target, spawn, transition);
            if ((edgeSource.EdgeMask & 2) != 0)
                AddWarp(mapObjects, gridX * roomWidth * 16 + half, gridY * roomHeight * 16, half, 8, target, spawn, transition);
            if ((edgeSource.EdgeMask & 4) != 0)
                AddWarp(mapObjects, gridX * roomWidth * 16, bottom, half, 8, target, spawn, transition);
            if ((edgeSource.EdgeMask & 8) != 0)
                AddWarp(mapObjects, gridX * roomWidth * 16 + half, bottom, half, 8, target, spawn, transition);
        }
    }

    private static void AddWarp(List<MapObjectPlacement> mapObjects, int x, int y,
        int width, int height, string target, (int X, int Y, int Direction) spawn, int transition)
    {
        mapObjects.Add(new MapObjectPlacement("oracleWarp", new[]
        {
            x.ToString(), y.ToString(), target,
            spawn.X.ToString(), spawn.Y.ToString(), spawn.Direction.ToString(),
            width.ToString(), height.ToString(), transition.ToString(),
        }));
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
            inputs = _content.GetImportedSourcePaths()
                .Append(Path.Combine(_content.Repo.DataDir, "singleTileChanges.s"))
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
                    OracleTileChanges.RawTablePath(_outDir, _content.Repo.Game),
                    OracleTileChanges.BakedTablePath(_outDir, _content.Repo.Game, group),
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



