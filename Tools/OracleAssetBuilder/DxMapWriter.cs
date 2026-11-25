using System.Text;

namespace Oracles.Tools.AssetBuilder;

public sealed record MapAnimationPlacement(int X, int Y, int[] Frames, int[] Durations);

/// <summary>One placed DX-HD map object: template name plus semicolon-separated fields (posX, posY first).</summary>
public sealed record MapObjectPlacement(string Type, IReadOnlyList<string> Fields);

/// <summary>
/// Writes the text map format used by Links-Awakening-DX-HD v2.0.6, plus the
/// optional Oracle sections (collision, terrain, tile animation) appended after
/// the native object list. The DX-HD loader ignores unknown trailing lines; the
/// Oracles-HD loader reads them.
/// </summary>
public static class DxMapWriter
{
    public static void Write(
        string path,
        string tilesetName,
        int width,
        int height,
        IReadOnlyList<int[,]> layers,
        byte[,]? collision = null,
        byte[,]? terrain = null,
        IReadOnlyList<MapAnimationPlacement>? animations = null,
        IReadOnlyList<MapObjectPlacement>? objects = null,
        OracleMapInfo? mapInfo = null)
    {
        if (layers.Count == 0)
            throw new ArgumentException("At least one tile layer is required.", nameof(layers));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine("3");          // map format version
        writer.WriteLine("0");          // MapOffsetX
        writer.WriteLine("0");          // MapOffsetY
        writer.WriteLine(tilesetName);
        writer.WriteLine(width);
        writer.WriteLine(height);
        writer.WriteLine(layers.Count);

        foreach (var layer in layers)
        {
            for (var y = 0; y < height; y++)
            {
                var builder = new StringBuilder();
                for (var x = 0; x < width; x++)
                {
                    var value = layer[x, y];
                    if (value >= 0)
                        builder.Append(value);
                    builder.Append(',');
                }
                writer.WriteLine(builder.ToString());
            }
        }

        WriteObjects(writer, objects);

        if (mapInfo != null)
        {
            writer.WriteLine("#oracle-map v1");
            writer.WriteLine($"game={mapInfo.Game}");
            writer.WriteLine($"group={mapInfo.Group}");
            writer.WriteLine($"era={mapInfo.Era}");
            writer.WriteLine($"kind={mapInfo.Kind}");
            writer.WriteLine($"roomWidth={mapInfo.RoomWidth}");
            writer.WriteLine($"roomHeight={mapInfo.RoomHeight}");
            writer.WriteLine($"gridWidth={mapInfo.GridWidth}");
            writer.WriteLine($"gridHeight={mapInfo.GridHeight}");
            writer.WriteLine($"room={mapInfo.Room}");
            writer.WriteLine($"floor={mapInfo.Floor}");
            writer.WriteLine($"dungeon={mapInfo.Dungeon}");
            writer.WriteLine($"camera={mapInfo.Camera}");
        }

        if (mapInfo != null && mapInfo.RoomEffects.Count > 0)
        {
            writer.WriteLine("#oracle-room-effects v1");
            writer.WriteLine(mapInfo.RoomEffects.Count);
            foreach (var effect in mapInfo.RoomEffects)
                writer.WriteLine($"{effect.Group};{effect.Room};{effect.Handler}");
        }

        if (collision != null)
            WriteByteMap(writer, "#oracle-collision v1", collision);

        if (terrain != null)
            WriteByteMap(writer, "#oracle-terrain v1", terrain);

        if (animations is { Count: > 0 })
        {
            writer.WriteLine("#oracle-anim v1");
            writer.WriteLine(animations.Count);
            foreach (var animation in animations)
            {
                writer.WriteLine(
                    string.Join(',', animation.Frames) + ";" +
                    string.Join(',', animation.Durations) + ";" +
                    animation.X + ";" + animation.Y);
            }
        }
    }

    private static void WriteObjects(StreamWriter writer, IReadOnlyList<MapObjectPlacement>? objects)
    {
        if (objects == null || objects.Count == 0)
        {
            writer.WriteLine("0");
            writer.WriteLine("0");
            return;
        }

        var types = objects
            .Select(o => o.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();
        var typeIndex = types
            .Select((type, index) => (type, index))
            .ToDictionary(pair => pair.type, pair => pair.index, StringComparer.Ordinal);

        writer.WriteLine(types.Length);
        foreach (var type in types)
            writer.WriteLine(type);

        writer.WriteLine(objects.Count);
        foreach (var placement in objects)
        {
            var fields = new List<string> { typeIndex[placement.Type].ToString() };
            fields.AddRange(placement.Fields);
            writer.WriteLine(string.Join(';', fields));
        }
    }

    private static void WriteByteMap(StreamWriter writer, string header, byte[,] map)
    {
        var width = map.GetLength(0);
        var height = map.GetLength(1);

        writer.WriteLine(header);
        writer.WriteLine(width);
        writer.WriteLine(height);

        var builder = new StringBuilder(width * 2);
        for (var y = 0; y < height; y++)
        {
            builder.Clear();
            for (var x = 0; x < width; x++)
                builder.Append(map[x, y].ToString("x2"));
            writer.WriteLine(builder.ToString());
        }
    }
}
