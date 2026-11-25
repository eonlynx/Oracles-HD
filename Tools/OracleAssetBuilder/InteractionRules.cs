namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Presentation/spawn rules for interactions whose state lives in object code
/// rather than in the room object stream. The initial case is the post-game
/// Link statue `$6b:$15`: it is gated by GLOBALFLAG_FINISHEDGAME and loads
/// PALH_c7 (sprite palette 6).
/// </summary>
public readonly record struct InteractionRule(
    int RequiresFlag,
    int ForbiddenFlag,
    int SpritePaletteHeader);

public static class InteractionRules
{
    public static InteractionRule For(int id, int subId)
    {
        return (id, subId) switch
        {
            (0x6b, 0x15) => new InteractionRule(0x14, -1, 0xc7),
            _ => new InteractionRule(-1, -1, -1),
        };
    }
}
