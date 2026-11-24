using System.Security.Cryptography;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Read-only access to a pinned oracles-disasm checkout.
/// Nothing here is used by the game at runtime; this tool runs offline.
/// </summary>
public sealed class OracleRepo
{
    public string Root { get; }
    public string Game { get; }

    public string DataDir => Path.Combine(Root, "data", Game);
    public string RoomsDir => Path.Combine(Root, "rooms", Game);
    public string TilesetLayoutsDir => Path.Combine(Root, "tileset_layouts", Game);
    public string GfxCompressibleDir => Path.Combine(Root, "gfx_compressible", Game);
    public string GfxDir => Path.Combine(Root, "gfx", Game);
    public string GfxCommonDir => Path.Combine(Root, "gfx", "common");

    private readonly Dictionary<string, string> _pngByName = new(StringComparer.Ordinal);

    public OracleRepo(string root, string game)
    {
        Root = Path.GetFullPath(root);
        Game = game;

        foreach (var dir in new[] { GfxCompressibleDir, GfxDir, GfxCommonDir })
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
                _pngByName.TryAdd(Path.GetFileNameWithoutExtension(file), file);
        }
    }

    public string RequireFile(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required disassembly file not found: {relativePath}", path);
        return path;
    }

    public string RequireGameFile(string relativeToGame)
    {
        var path = Path.Combine(Root, relativeToGame.Replace("{game}", Game));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required disassembly file not found: {relativeToGame}", path);
        return path;
    }

    public byte[] ReadBytes(string path)
    {
        return File.ReadAllBytes(path);
    }

    public string? FindPng(string baseName)
    {
        return _pngByName.TryGetValue(baseName, out var path) ? path : null;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Locate the repository root: the nearest ancestor that contains
    /// external/oracles-disasm. Falls back to the current directory.
    /// </summary>
    public static string FindDefaultDisassembly()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "external", "oracles-disasm");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "external/oracles-disasm not found. Pass --disasm <path> or run from the Oracles-HD root.");
    }

    public static string FindDefaultOutput()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Oracles-HD.csproj")))
                return Path.Combine(dir.FullName, "Data", "Maps");
            dir = dir.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Maps");
    }
}
