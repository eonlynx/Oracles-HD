using System;
using System.Collections.Generic;

namespace ProjectZ.InGame.Things
{
    /// <summary>
    /// Lightweight Oracle global/room flag state for the current session. The
    /// eventual save owner will replace this; the rest of the runtime only uses
    /// this API so the transition is contained.
    /// </summary>
    public static class OracleFlagState
    {
        public const int IntroDone = 0x0a;
        public const int MakuTreeSaved = 0x12;
        public const int FinishedGame = 0x14;

        private static readonly bool[] Flags = new bool[256];

        public static int RoomStateModifier { get; set; }
        public static byte RoomFlags { get; set; }

        public static void Reset(IEnumerable<int> initialFlags, int roomStateModifier = 0)
        {
            Array.Clear(Flags, 0, Flags.Length);
            RoomStateModifier = Math.Clamp(roomStateModifier, 0, 7);

            // The development entry boots after the intro; keep the common
            // intro flag set so ordinary NPCs are available by default.
            Flags[IntroDone] = true;

            if (initialFlags != null)
                foreach (var flag in initialFlags)
                    if (flag is >= 0 and < 256)
                        Flags[flag] = true;
        }

        public static bool Get(int flag)
        {
            return flag is >= 0 and < 256 && Flags[flag];
        }

        public static void Set(int flag, bool value)
        {
            if (flag is >= 0 and < 256)
                Flags[flag] = value;
        }

        /// <summary>
        /// Native obj_Condition semantics: a non-zero mask hides the object when
        /// the current room-state modifier bit is set. requiresFlag/forbiddenFlag
        /// are the interaction-level gate (global flags).
        /// </summary>
        public static bool IsVisible(int requiresFlag, int forbiddenFlag, int roomStateMask,
            int roomFlagMask = 0, int roomFlagValue = 0)
        {
            if (requiresFlag >= 0 && !Get(requiresFlag))
                return false;
            if (forbiddenFlag >= 0 && Get(forbiddenFlag))
                return false;
            if (roomStateMask != 0 && (roomStateMask & (1 << RoomStateModifier)) != 0)
                return false;
            if (roomFlagMask != 0 && (RoomFlags & roomFlagMask) != roomFlagValue)
                return false;
            return true;
        }
    }
}
