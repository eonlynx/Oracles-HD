using System.Text;
using System.Text.Json;

namespace Oracles.Tools.AssetBuilder;

/// <summary>
/// Source-cited story-state rules for room-placed actors whose initializer
/// checks progress flags. The runtime evaluates the emitted table generically;
/// it never branches on an interaction id. Rows are added from the native
/// initializer and are extended as more initializers are decoded.
/// </summary>
public static class InteractionVisibility
{
    public sealed record Rule(
        int Id, int SubId, int Var03, int Flag, bool Expected, string Source);

    public static readonly Rule[] Rules =
    {
        // bear.s:@initSubid02, var03=$00: INTRO_DONE, then children, then
        // FINISHEDGAME / MAKU_TREE_SAVED gates.
        new(0x5d, 0x02, 0x00, 0x0a, true, "bear.s:@initSubid02"),
        new(0x5d, 0x02, 0x00, 0x14, false, "bear.s:@initSubid02"),
        new(0x5d, 0x02, 0x00, 0x12, true, "bear.s:@initSubid02"),
        // bear.s:@initSubid02, var03!=0: FINISHEDGAME.
        new(0x5d, 0x02, 0x01, 0x14, true, "bear.s:@initSubid02"),
        // monkey.s subid07 (var03 0/2) and rabbit.s subid07, spawned by
        // parseGivenObjectData from objectData.animalsWaitingForNayru.
        new(0x39, 0x07, 0x00, 0x14, false, "monkey.s:subid07"),
        new(0x39, 0x07, 0x00, 0x11, true, "monkey.s:subid07"),
        new(0x39, 0x07, 0x02, 0x14, false, "monkey.s:subid07"),
        new(0x39, 0x07, 0x02, 0x12, true, "monkey.s:subid07"),
        new(0x39, 0x07, 0x01, 0x14, true, "monkey.s:subid07"),
        new(0x4b, 0x07, 0x00, 0x14, false, "rabbit.s:subid07"),
        // 6b.s:interaction6b_subid01: before INTRO_DONE it spawns
        // objectData.nayruAndAnimalsInIntro and deletes itself.
        new(0x6b, 0x01, 0x00, 0x0a, false, "miscellaneous1.s:interaction6b_subid01"),
        new(0x36, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x37, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x5d, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x39, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x4b, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x3c, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x4c, 0x00, 0x00, 0x0a, false, "objectData.nayruAndAnimalsInIntro"),
        new(0x4b, 0x07, 0x00, 0x12, true, "rabbit.s:subid07"),
        // ralph.s:@initSubid0d is a later story variant; the $6b:$01 intro
        // event owns the audience Ralph ($37:$00) and deletes this one.
        new(0x37, 0x0d, 0x00, 0x0a, true, "ralph.s:@initSubid0d"),
        // ralph.s:@initSubid11 requires FINISHEDGAME.
        new(0x37, 0x11, 0x00, 0x14, true, "ralph.s:@initSubid11"),
    };

    public static string TablePath(string mapsOutDir, string game) =>
        Path.Combine(mapsOutDir, "Visibility", $"oracle_{game}_visibility.tsv");

    public static void Build(OracleRepo repo, string mapsOutDir)
    {
        var path = TablePath(mapsOutDir, repo.Game);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new StringBuilder();
        builder.Append("id\tsubid\tvar03\tflag\texpected\tsource\n");
        foreach (var rule in Rules.OrderBy(rule => rule.Id)
                     .ThenBy(rule => rule.SubId).ThenBy(rule => rule.Var03)
                     .ThenBy(rule => rule.Flag))
        {
            builder.Append($"0x{rule.Id:x2}\t0x{rule.SubId:x2}\t0x{rule.Var03:x2}\t");
            builder.Append($"0x{rule.Flag:x2}\t{(rule.Expected ? 1 : 0)}\t{rule.Source}\n");
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));

        var manifestPath = Path.ChangeExtension(path, ".manifest.json");
        var manifest = new
        {
            tool = "OracleAssetBuilder",
            format = 1,
            game = repo.Game,
            entry = "interaction_visibility",
            rows = Rules.Length,
            outputs = new[]
            {
                new
                {
                    path = Path.GetRelativePath(mapsOutDir, path).Replace('\\', '/'),
                    sha256 = OracleRepo.Sha256(path),
                },
            },
        };
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    /// <summary>
    /// Child block owned by a parent initializer. The runtime places the
    /// parsed children as ordinary room objects; their own rows gate them.
    /// </summary>
    public static int PositionOffsetY(int id, int subId, int var03) =>
        (id, subId, var03) switch
        {
            // bear.s:@initSubid00 adds $10 when room flag $80 is clear.
            (0x5d, 0x00, 0x00) => 16,
            _ => 0,
        };

    public static bool HasRules(int id, int subId, int var03) =>
        Rules.Any(rule => rule.Id == id && rule.SubId == subId && rule.Var03 == var03);

    public static string? ChildGroup(int id, int subId, int var03) =>
        (id, subId, var03) switch
        {
            (0x5d, 0x02, 0x00) => "animalsWaitingForNayru",
            (0x6b, 0x01, 0x00) => "nayruAndAnimalsInIntro",
            _ => null,
        };
}


