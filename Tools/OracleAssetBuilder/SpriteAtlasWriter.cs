using System.Text;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Packs composed interaction sprites into one RGBA atlas plus a DX-HD .atlas
/// sidecar. Entry ids are stable and sorted so the generated output is
/// deterministic. The runtime loads the pair through the existing
/// Data/Map Objects folder scan.
/// </summary>
public sealed class SpriteAtlasWriter
{
    private readonly SortedDictionary<string, RgbaImage> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public void Add(string id, RgbaImage image)
    {
        if (!_entries.ContainsKey(id))
            _entries.Add(id, image);
    }

    public void Write(string pngPath, string atlasPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);

        if (_entries.Count == 0)
        {
            Png.SaveRgba(pngPath, new RgbaImage(8, 8));
            File.WriteAllText(atlasPath, "1\n1\n", new UTF8Encoding(false));
            return;
        }

        var maxWidth = _entries.Values.Max(image => image.Width);
        var maxHeight = _entries.Values.Max(image => image.Height);
        var cellWidth = maxWidth + 1;
        var cellHeight = maxHeight + 1;
        var columns = Math.Max(1, Math.Min(_entries.Count, 2048 / cellWidth));
        var rows = (_entries.Count + columns - 1) / columns;

        var width = NextPowerOfTwo(columns * cellWidth);
        var height = NextPowerOfTwo(rows * cellHeight);
        var atlas = new RgbaImage(width, height);
        var positions = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);

        var index = 0;
        foreach (var pair in _entries)
        {
            var x = index % columns * cellWidth;
            var y = index / columns * cellHeight;
            positions[pair.Key] = (x, y);
            atlas.CopyFrom(pair.Value, x, y);
            index++;
        }

        Png.SaveRgba(pngPath, atlas);

        var builder = new StringBuilder();
        builder.Append("1\n1\n");
        foreach (var pair in _entries)
        {
            var position = positions[pair.Key];
            builder.Append(pair.Key).Append(':')
                .Append(position.X).Append(',')
                .Append(position.Y).Append(',')
                .Append(pair.Value.Width).Append(',')
                .Append(pair.Value.Height).Append(",0,0\n");
        }

        File.WriteAllText(atlasPath, builder.ToString(), new UTF8Encoding(false));
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
            result <<= 1;
        return result;
    }
}
