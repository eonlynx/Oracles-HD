namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Species-to-template mapping for runtime enemy hosts. The importer only
/// selects a host when the species has a verified native-component adapter;
/// unsupported enemies keep the presentation-only oracleSprite fallback.
/// </summary>
public static class OracleEntityFactory
{
    public static string? TemplateFor(int id, int subId) =>
        id == 0x09 && subId is >= 0 and <= 2 ? "oracleOctorok" : null;
}
