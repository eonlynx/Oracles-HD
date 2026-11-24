using System.Text;

namespace Oracles.Tools.AssetBuilder;

public sealed record MapAnimationPlacement(int X, int Y, int[] Frames, int[] Durations);

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
        IReadOnlyList<MapAnimationPlacement>? animations = null)
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

        writer.WriteLine("0");          // object templates used by this map
        writer.WriteLine("0");          // placed objects

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
