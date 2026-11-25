using System.Globalization;

namespace Oracles.Tools.AssetBuilder;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            if (Environment.GetEnvironmentVariable("ORACLE_TOOL_VERBOSE") == "1")
                Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var (command, options) = ParseArgs(args);
        if (command is null or "help" or "--help" or "-h")
        {
            PrintHelp();
            return command is null ? 1 : 0;
        }

        var game = Get(options, "game", "ages").ToLowerInvariant();
        if (game is not ("ages" or "seasons"))
            throw new ArgumentException("--game must be 'ages' or 'seasons'.");

        var disasm = Get(options, "disasm", OracleRepo.FindDefaultDisassembly());
        var outDir = Get(options, "out", OracleRepo.FindDefaultOutput());
        var tilesetsDir = Get(options, "tilesets", Path.Combine(outDir, "Tilesets"));
        var group = ParseHex(Get(options, "group", "0"));
        var oracleOptions = new OracleOptions(
            ParseCompanion(options),
            options.ContainsKey("maku-saved"),
            options.ContainsKey("jabu-underwater"));

        var repo = new OracleRepo(disasm, game);
        var content = new OracleContent(repo, group);

        return command switch
        {
            "room" => BuildRoom(content, group, oracleOptions, options, outDir, tilesetsDir),
            "overworld" => BuildOverworld(content, group, oracleOptions, outDir, tilesetsDir),
            "interiors" => BuildInteriors(content, oracleOptions, options, outDir, tilesetsDir),
            "dungeons" => BuildDungeons(content, oracleOptions, options, outDir, tilesetsDir),
            "inspect" => Inspect(content, options, oracleOptions),
            "inspect-anim" => InspectAnim(content, options),
            _ => throw new ArgumentException($"Unknown command '{command}'. Use 'room', 'overworld', 'interiors', 'dungeons' or 'inspect'."),
        };
    }

    private static int BuildRoom(OracleContent content, int group, OracleOptions oracleOptions, Dictionary<string, string> options, string outDir, string tilesetsDir)
    {
        var room = ParseHex(Get(options, "room", "00"));
        if (room is < 0 or > 0xFF)
            throw new ArgumentException("--room must be 00..ff.");

        int? bgPalette = options.TryGetValue("bg-palette", out var bg) ? ParseHex(bg) : null;

        var builder = new RoomBuilder(content, outDir, tilesetsDir);
        var largeRoom = options.ContainsKey("large");
        var result = builder.Build(group, room, largeRoom, bgPalette, oracleOptions);

        Console.WriteLine($"OracleAssetBuilder: {content.Repo.Game} group {group} room 0x{room:x2}");
        Console.WriteLine($"  tileset 0x{result.TilesetId:x2}  layout 0x{result.Layout:x2}  layout group 0x{result.LayoutGroup:x2}");
        Console.WriteLine($"  map      {result.MapPath}");
        Console.WriteLine($"  tileset  {result.TilesetPath}");
        Console.WriteLine($"  preview  {result.PreviewPath}");
        Console.WriteLine($"  manifest {result.ManifestPath}");
        Console.WriteLine($"  distinct metatiles: {result.DistinctMetatiles}");
        if (result.MissingPaletteMetatiles > 0)
            Console.WriteLine($"  warning: {result.MissingPaletteMetatiles} metatile(s) use BG palette 0/1 (not loaded by the tileset header).");
        if (result.MissingTileMetatiles > 0)
            Console.WriteLine($"  warning: {result.MissingTileMetatiles} metatile(s) reference tiles not loaded by the gfx headers.");
        return 0;
    }

    private static int BuildOverworld(OracleContent content, int group, OracleOptions oracleOptions, string outDir, string tilesetsDir)
    {
        var builder = new OverworldBuilder(content, outDir, tilesetsDir);
        var result = builder.Build(group, oracleOptions);
        Console.WriteLine($"OracleAssetBuilder: {content.Repo.Game} group {group} overworld");
        Console.WriteLine($"  rooms {result.Rooms}, skipped non-outdoors {result.SkippedRooms}");
        Console.WriteLine($"  contents: {result.Interactions} interactions, {result.Conditional} conditional, " +
                          $"{result.Enemies} enemies, {result.Parts} parts, {result.Items} items");
        Console.WriteLine($"  size {result.WidthTiles}x{result.HeightTiles} tiles, atlas {result.AtlasMetatiles} metatiles");
        Console.WriteLine($"  map      {result.MapPath}");
        Console.WriteLine($"  tileset  {result.TilesetPath}");
        Console.WriteLine($"  preview  {result.PreviewPath}");
        Console.WriteLine($"  manifest {result.ManifestPath}");
        return 0;
    }

    private static int BuildInteriors(OracleContent content, OracleOptions oracleOptions,
        Dictionary<string, string> options, string outDir, string tilesetsDir)
    {
        int? room = options.TryGetValue("room", out var roomValue) ? ParseHex(roomValue) : null;
        if (room is < 0 or > 0xFF)
            throw new ArgumentException("--room must be 00..ff.");

        // Interiors always live in group 2 (present) or group 3 (past); the
        // --group option belongs to the overworld/room commands.
        var interiorGroup = options.ContainsKey("past") ? 3 : 2;
        var interiorContent = content.Group == interiorGroup
            ? content
            : new OracleContent(content.Repo, interiorGroup);
        var warps = new OracleWarpData(content.Repo);
        var streams = new RoomObjectStreams(content.Repo);
        var builder = new InteriorBuilder(interiorContent, outDir, tilesetsDir, warps, streams);
        var result = builder.BuildAll(oracleOptions, room, interiorGroup);

        Console.WriteLine($"OracleAssetBuilder: {content.Repo.Game} " +
                          $"{(interiorGroup == 3 ? "past" : "present")} interiors");
        Console.WriteLine($"  rooms {result.Rooms}");
        Console.WriteLine($"  contents: {result.Interactions} interactions, {result.Conditional} conditional, " +
                          $"{result.Enemies} enemies, {result.Parts} parts, {result.Items} items");
        foreach (var map in result.MapPaths)
            Console.WriteLine($"  map      {map}");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"  warning: {warning}");
        return 0;
    }

    private static int BuildDungeons(OracleContent content, OracleOptions oracleOptions,
        Dictionary<string, string> options, string outDir, string tilesetsDir)
    {
        var dungeon = ParseHex(Get(options, "dungeon", "00"));
        if (dungeon is < 0 or > 0x0f)
            throw new ArgumentException("--dungeon must be 00..0f.");

        var warps = new OracleWarpData(content.Repo);
        var streams = new RoomObjectStreams(content.Repo);
        var builder = new DungeonBuilder(content, outDir, tilesetsDir, warps, streams);
        var result = builder.Build(dungeon, oracleOptions);

        Console.WriteLine($"OracleAssetBuilder: {content.Repo.Game} dungeon 0x{dungeon:x2}");
        Console.WriteLine($"  group {result.Group}, floors {result.Floors}, rooms {result.Rooms}");
        Console.WriteLine($"  contents: {result.Interactions} interactions, " +
                          $"{result.Enemies} enemies, {result.Parts} parts, {result.Items} items");
        foreach (var map in result.MapPaths)
            Console.WriteLine($"  map      {map}");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"  warning: {warning}");
        return 0;
    }

    private static int InspectAnim(OracleContent content, Dictionary<string, string> options)
    {
        var group = ParseHex(Get(options, "group", "0"));
        Console.WriteLine(content.Animation.Describe(group));
        Console.WriteLine($"animation headers: {content.Animation.Headers.Count}");
        return 0;
    }

    private static int Inspect(OracleContent content, Dictionary<string, string> options, OracleOptions oracleOptions)
    {
        var tilesetId = ParseHex(Get(options, "tileset", "00"));
        var info = content.GetTileset(tilesetId);
        Console.WriteLine($"tileset 0x{info.Index:x2}");
        Console.WriteLine($"  collision mode/dungeon 0x{info.CollisionModeAndDungeon:x2}");
        Console.WriteLine($"  flags 0x{info.Flags:x2}  outdoors={info.IsOutdoors} indoors={info.IsIndoors} dungeon={info.IsDungeon} sidescroll={info.IsSideScroll} underwater={info.IsUnderwater} past/subrosia={info.IsPastOrSubrosia}");
        Console.WriteLine($"  gfx header 0x{info.Gfx:x2}  unique 0x{info.UniqueGfx:x2}  palette 0x{info.Palette:x2}");
        Console.WriteLine($"  layout 0x{info.Layout:x2}  layout group 0x{info.LayoutGroup:x2}  animation 0x{info.Animation:x2}");

        var renderer = content.CreateRenderer(tilesetId, null, 0, oracleOptions);
        Console.WriteLine($"  loaded tiles: bank0 {renderer.LoadedTileCountBank0}/128, bank1 {renderer.LoadedTileCountBank1}/256");
        Console.WriteLine($"  palettes loaded: {string.Join(", ", Enumerable.Range(0, 8).Where(renderer.IsPaletteLoaded).Select(i => i.ToString()))}");
        return 0;
    }

    private static (string? Command, Dictionary<string, string> Options) ParseArgs(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? command = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    options[key] = args[++i];
                else
                    options[key] = "true";
            }
            else
            {
                command ??= arg;
            }
        }

        return (command, options);
    }

    private static int? ParseCompanion(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("companion", out var value))
            return null;

        return value.ToLowerInvariant() switch
        {
            "ricky" => OracleOptions.Ricky,
            "dimitri" => OracleOptions.Dimitri,
            "moosh" => OracleOptions.Moosh,
            _ => throw new ArgumentException("--companion must be ricky, dimitri or moosh."),
        };
    }

    private static string Get(Dictionary<string, string> options, string key, string fallback)
    {
        return options.TryGetValue(key, out var value) ? value : fallback;
    }

    /// <summary>All numeric CLI values are hexadecimal, with an optional "$" or "0x" prefix.</summary>
    private static int ParseHex(string value)
    {
        value = value.Trim();
        if (value.StartsWith('$'))
            value = value[1..];
        else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];
        return int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            OracleAssetBuilder â€” offline importer for Oracles-HD

            Commands:
              room       Render one room and emit a DX-HD .map + tileset atlas
              overworld  Stitch all OUTDOORS rooms of a group into one DX-HD map
              interiors  Build present-day interiors reachable from group 0 + warps
              inspect   Print a tileset's tables

            Options:
              --game ages|seasons       Game pack (default: ages)
              --group N                 Room group (default: 0)
              --room HH                 Room id, hex (default: 00; interiors: optional filter)
              --tileset HH              Tileset id for inspect (default: 00)
              --large                   Use the 15x11 playable area of a 176-byte room
              --bg-palette HH           Optional palette header applied before the tileset palette
              --disasm PATH             oracles-disasm checkout (default: external/oracles-disasm)
              --out PATH                Map output directory (default: Data/Maps)
              --tilesets PATH           Tileset PNG directory (default: <out>/Tilesets)

            Examples:
              OracleAssetBuilder room --game ages --room 00
              OracleAssetBuilder inspect --game ages --tileset 00
              OracleAssetBuilder room --game ages --room 38 --bg-palette 0c
            """);
    }
}
