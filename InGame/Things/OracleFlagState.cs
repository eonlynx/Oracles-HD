using System;
using System.Collections.Generic;

namespace ProjectZ.InGame.Things
{
    /// <summary>
    /// Lightweight development facade over <see cref="OracleSessionState"/>.
    /// New runtime code should take the session instance; this facade remains so
    /// the development entry and existing object constructors do not need a
    /// service locator.
    /// </summary>
    public static class OracleFlagState
    {
        public static OracleSessionState Current { get; private set; } = new();

        public static int RoomStateModifier => Current.RoomStateModifier;
        public static int EntryDirection => Current.EntryDirection;

        public static byte RoomFlags
        {
            get => Current.CurrentRoomFlags;
            set => Current.SetRoomFlags(Current.Group, Current.Room, value);
        }

        public static void Reset(IEnumerable<int> initialFlags, int roomStateModifier = 0)
        {
            Current.Reset(initialFlags, roomStateModifier);
        }

        public static void Use(OracleSessionState session)
        {
            Current = session ?? throw new ArgumentNullException(nameof(session));
        }

        public static bool Get(int flag) => Current.GetFlag(flag);
        public static void Set(int flag, bool value) => Current.SetFlag(flag, value);

        public static bool IsVisible(int requiresFlag, int forbiddenFlag, int roomStateMask,
            int roomFlagMask = 0, int roomFlagValue = 0)
        {
            return Current.IsVisible(requiresFlag, forbiddenFlag, roomStateMask,
                roomFlagMask, roomFlagValue);
        }
    }
}
