using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Oracles.Tools.AssetBuilder;

public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static readonly Rgba Black = new(0, 0, 0, 255);
}

public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        Width = width;
        Height = height;
        Data = new byte[width * height * 4];
    }

    public Rgba this[int x, int y]
    {
        get
        {
            var i = (y * Width + x) * 4;
            return new Rgba(Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
        }
        set
        {
            var i = (y * Width + x) * 4;
            Data[i] = value.R;
            Data[i + 1] = value.G;
            Data[i + 2] = value.B;
            Data[i + 3] = value.A;
        }
    }

    public void CopyFrom(RgbaImage source, int destinationX, int destinationY)
    {
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                this[destinationX + x, destinationY + y] = source[x, y];
    }
}

/// <summary>
/// The disassembly's tileset PNGs are 2bpp palette-indexed (bit depth 2, colour
/// type 3, non-interlaced). Each pixel is already the Game Boy colour index, so
/// the decoder exposes those indices directly instead of resolving colours.
/// </summary>
public sealed class Indexed2bppImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Indices { get; }

    public Indexed2bppImage(int width, int height, byte[] indices)
    {
        Width = width;
        Height = height;
        Indices = indices;
    }

    public byte this[int x, int y] => Indices[y * Width + x];
}

/// <summary>
/// Minimal, dependency-free PNG reader/writer for the two shapes this tool needs:
/// read bit-depth-2 palette images, write 8-bit RGBA images.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static Indexed2bppImage LoadIndexed2bpp(string path)
    {
        var file = File.ReadAllBytes(path);
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException($"{path}: not a PNG file.");
        if (file.Length < 12)
            throw new InvalidDataException($"{path}: truncated PNG.");

        var offset = 8;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        var idat = new MemoryStream();

        while (offset + 12 <= file.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(file, offset + 4, 4);
            if (length < 0 || offset + 12 + length > file.Length)
                throw new InvalidDataException($"{path}: corrupt PNG chunk '{type}'.");

            var data = file.AsSpan(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    if (length != 13)
                        throw new InvalidDataException($"{path}: invalid IHDR length.");
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
            }

            offset += 12 + length;
            if (type == "IEND")
                break;
        }

        if (bitDepth != 2 || colorType != 3 || interlace != 0)
            throw new InvalidDataException(
                $"{path}: expected 2bpp palette PNG (bit depth 2, colour type 3, non-interlaced), got depth={bitDepth} type={colorType} interlace={interlace}.");

        var bytesPerRow = (width * 2 + 7) / 8;
        var expected = (bytesPerRow + 1) * height;
        idat.Position = 0;
        var raw = new byte[expected];
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            var read = 0;
            while (read < expected)
            {
                var count = zlib.Read(raw, read, expected - read);
                if (count == 0)
                    break;
                read += count;
            }
            if (read != expected)
                throw new InvalidDataException($"{path}: decompressed {read} bytes, expected {expected}.");
        }

        var scanlines = Unfilter(raw, bytesPerRow, height);
        var indices = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = scanlines.AsSpan(y * bytesPerRow, bytesPerRow);
            for (var x = 0; x < width; x++)
            {
                var shift = 6 - 2 * (x & 3);
                indices[y * width + x] = (byte)((row[x >> 2] >> shift) & 0x03);
            }
        }

        return new Indexed2bppImage(width, height, indices);
    }

    public static void SaveRgba(string path, RgbaImage image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var bytesPerRow = image.Width * 4;
        var filtered = new byte[(bytesPerRow + 1) * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            filtered[y * (bytesPerRow + 1)] = 0; // filter: none
            Buffer.BlockCopy(image.Data, y * bytesPerRow, filtered, y * (bytesPerRow + 1) + 1, bytesPerRow);
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(filtered, 0, filtered.Length);
            compressed = output.ToArray();
        }

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        file.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)image.Height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: RGBA
        header[10] = 0; // compression
        header[11] = 0; // filter
        header[12] = 0; // interlace
        WriteChunk(file, "IHDR", header);

        WriteChunk(file, "IDAT", compressed);
        WriteChunk(file, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static byte[] Unfilter(byte[] raw, int bytesPerRow, int height)
    {
        var output = new byte[bytesPerRow * height];
        var previous = new byte[bytesPerRow];

        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (bytesPerRow + 1)];
            var source = raw.AsSpan(y * (bytesPerRow + 1) + 1, bytesPerRow);
            var target = output.AsSpan(y * bytesPerRow, bytesPerRow);

            switch (filter)
            {
                case 0:
                    source.CopyTo(target);
                    break;
                case 1:
                    for (var i = 0; i < bytesPerRow; i++)
                        target[i] = (byte)(source[i] + (i >= 1 ? target[i - 1] : 0));
                    break;
                case 2:
                    for (var i = 0; i < bytesPerRow; i++)
                        target[i] = (byte)(source[i] + previous[i]);
                    break;
                case 3:
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var left = i >= 1 ? target[i - 1] : 0;
                        target[i] = (byte)(source[i] + ((left + previous[i]) >> 1));
                    }
                    break;
                case 4:
                    for (var i = 0; i < bytesPerRow; i++)
                    {
                        var left = i >= 1 ? target[i - 1] : 0;
                        var upLeft = i >= 1 ? previous[i - 1] : 0;
                        target[i] = (byte)(source[i] + Paeth(left, previous[i], upLeft));
                    }
                    break;
                default:
                    throw new InvalidDataException($"Unsupported PNG filter type {filter}.");
            }

            target.CopyTo(previous);
        }

        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }

        public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in first)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (var b in second)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
