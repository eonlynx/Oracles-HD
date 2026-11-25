using System;
using System.Collections.Generic;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Map
{
    /// <summary>
    /// Game-specific Oracle constants and policies. Everything that differs
    /// between Ages and Seasons should live here instead of in shared runtime
    /// objects or source parsers.
    /// </summary>
    public sealed class OracleGameProfile
    {
        public static readonly OracleGameProfile Ages = new(
            game: "ages",
            introFlag: 0x0a,
            finishedFlag: 0x14,
            makuTreeSavedFlag: 0x12,
            ralphEnteredPortalFlag: 0x40,
            enterPastDoneFlag: 0x41,
            supportsTimeTravel: true,
            overworldGroups: new[] { 0, 1 });

        public static readonly OracleGameProfile Seasons = new(
            game: "seasons",
            introFlag: 0x0a,
            finishedFlag: 0x28,
            makuTreeSavedFlag: -1,
            ralphEnteredPortalFlag: -1,
            enterPastDoneFlag: -1,
            supportsTimeTravel: false,
            overworldGroups: new[] { 0 });

        public string Game { get; }
        public int IntroFlag { get; }
        public int FinishedFlag { get; }
        public int MakuTreeSavedFlag { get; }
        public int RalphEnteredPortalFlag { get; }
        public int EnterPastDoneFlag { get; }
        public bool SupportsTimeTravel { get; }
        public IReadOnlyList<int> OverworldGroups { get; }
        public int InteriorGroup => 2;

        private OracleGameProfile(string game, int introFlag, int finishedFlag,
            int makuTreeSavedFlag, int ralphEnteredPortalFlag, int enterPastDoneFlag,
            bool supportsTimeTravel, int[] overworldGroups)
        {
            Game = game;
            IntroFlag = introFlag;
            FinishedFlag = finishedFlag;
            MakuTreeSavedFlag = makuTreeSavedFlag;
            RalphEnteredPortalFlag = ralphEnteredPortalFlag;
            EnterPastDoneFlag = enterPastDoneFlag;
            SupportsTimeTravel = supportsTimeTravel;
            OverworldGroups = overworldGroups;
        }

        public static OracleGameProfile For(string game)
        {
            return game switch
            {
                "ages" => Ages,
                "seasons" => Seasons,
                _ => throw new ArgumentException($"Unknown Oracle game '{game}'.", nameof(game)),
            };
        }

        public string OverworldMap(int group) => $"oracle_{Game}_g{group}_overworld.map";

        public string InteriorMap(int room) => $"oracle_{Game}_g{InteriorGroup}_r{room:x2}.map";

        public int ParseFlag(string value)
        {
            var name = value.Trim().ToUpperInvariant();
            var named = name switch
            {
                "FINISHEDGAME" => FinishedFlag,
                "INTRO_DONE" => IntroFlag,
                "MAKU_TREE_SAVED" => MakuTreeSavedFlag,
                "RALPH_ENTERED_PORTAL" => RalphEnteredPortalFlag,
                "ENTER_PAST_DONE" => EnterPastDoneFlag,
                _ => int.MinValue,
            };
            if (named != int.MinValue)
                return named;

            var text = value.Trim().TrimStart('$');
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return int.Parse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Ages portal spawner contract (`timeportalSpawner.s`): bit 7 means the
        /// portal was opened by a warp; subid 1 stays open until the Maku Tree is
        /// saved; subid 2 stays open until the Seed Satchel. Seasons has no time
        /// portals; its Subrosia entrances are normal warps.
        /// </summary>
        public bool IsPortalActive(int subId, OracleSessionState session)
        {
            if (!SupportsTimeTravel)
                return false;
            if ((subId & 0x80) != 0)
                return true;

            return (subId & 0x0F) switch
            {
                1 => MakuTreeSavedFlag < 0 || !session.GetFlag(MakuTreeSavedFlag),
                2 => true,
                _ => false,
            };
        }

        internal IReadOnlyList<IOracleRoomEvent> CreateRoomEvents()
        {
            return Game == "ages"
                ? new IOracleRoomEvent[] { new RalphPortalRoomEvent(), new EnterPastRoomEvent() }
                : Array.Empty<IOracleRoomEvent>();
        }
    }
}
