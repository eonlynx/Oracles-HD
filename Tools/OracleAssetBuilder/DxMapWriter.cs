using System.Text;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Writes the text map format used by Links-Awakening-DX-HD v2.0.6:
/// version, offsets, tileset name, width, height, depth, tile layers, then
/// the per-map object-template list and object list (both empty here).
/// </summary>
public static class DxMapWriter
{
    public static void Write(string path, string tilesetName, int width, int height, IReadOnlyList<int[,]> layers)
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
    }
}
