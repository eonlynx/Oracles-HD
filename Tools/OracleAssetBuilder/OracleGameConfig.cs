namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Per-game content layout used by the importer. This is intentionally small:
/// the runtime counterpart is InGame/Map/OracleGameProfile.cs.
/// </summary>
public static class OracleGameConfig
{
    public static IReadOnlyList<int> OverworldGroups(string game)
    {
        return game switch
        {
            "ages" => new[] { 0, 1 },
            "seasons" => new[] { 0 },
            _ => throw new ArgumentException($"Unknown Oracle game '{game}'.", nameof(game)),
        };
    }

    public static int InteriorGroup(string game) => 2;

    public static bool SupportsTimeTravel(string game) => game == "ages";
}
