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
    int SpritePaletteHeader,
    int CollisionRadius);

public static class InteractionRules
{
    public static InteractionRule For(int id, int subId)
    {
        return (id, subId) switch
        {
            // Post-game Link statue: solid, PALH_c7 sprite palette 6.
            (0x6b, 0x15) => new InteractionRule(0x14, -1, 0xc7, 6),

            // Background/effect interactions: visible but not solid.
            (0x87, _) => new InteractionRule(-1, -1, -1, 0), // Maku Tree eyes
            (0x89, _) => new InteractionRule(-1, -1, -1, 0), // flame
            (0xe5, _) => new InteractionRule(-1, -1, -1, 0), // flower/book/seedling
            (0x71, _) => new InteractionRule(-1, -1, -1, 0), // tree/seed decoration

            // Ordinary NPC/actor default from ooa-godot NpcCharacter.
            _ => new InteractionRule(-1, -1, -1, 6),
        };
    }
}
