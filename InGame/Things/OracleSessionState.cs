using System;
using System.Collections.Generic;

namespace ProjectZ.InGame.Things
{
    /// <summary>
    /// Per-session Oracle state: native-sized global flags, one flag byte per
    /// (group, room), room-state modifier and the room-entry direction. This is
    /// the instance that scripts, dungeons and saves will share; the static
    /// <see cref="OracleFlagState"/> is only a development facade.
    /// </summary>
    public sealed class OracleSessionState
    {
        public const int GlobalFlagCount = 0x80;
        public const int GroupCount = 8;
        public const int RoomsPerGroup = 0x100;

        private readonly bool[] _globalFlags = new bool[GlobalFlagCount];
        private readonly byte[][] _roomFlags = new byte[GroupCount][];

        public int RoomStateModifier { get; private set; }
        public int Group { get; private set; } = -1;
        public int Room { get; private set; } = -1;
        public int EntryDirection { get; private set; } = -1;

        /// <summary>Native wDisabledObjects. Bit 0 disables Link, bit 7 disables input/menu.</summary>
        public int DisabledObjects { get; set; }

        public bool LinkInputDisabled => (DisabledObjects & 0x81) != 0;

        /// <summary>Development override applied to the next entered room.</summary>
        public int PendingRoomFlags { get; set; } = -1;

        public OracleSessionState()
        {
            for (var group = 0; group < GroupCount; group++)
                _roomFlags[group] = new byte[RoomsPerGroup];
        }

        public void Reset(IEnumerable<int> initialFlags, int roomStateModifier = 0, int introFlag = 0x0a)
        {
            Array.Clear(_globalFlags, 0, _globalFlags.Length);
            foreach (var roomFlags in _roomFlags)
                Array.Clear(roomFlags, 0, roomFlags.Length);

            RoomStateModifier = Math.Clamp(roomStateModifier, 0, 7);
            Group = -1;
            Room = -1;
            EntryDirection = -1;
            PendingRoomFlags = -1;
            DisabledObjects = 0;

            if (introFlag is >= 0 and < GlobalFlagCount)
                _globalFlags[introFlag] = true;

            if (initialFlags != null)
                foreach (var flag in initialFlags)
                    if (flag is >= 0 and < GlobalFlagCount)
                        _globalFlags[flag] = true;
        }

        public bool GetFlag(int flag)
        {
            return flag is >= 0 and < GlobalFlagCount && _globalFlags[flag];
        }

        public void SetFlag(int flag, bool value)
        {
            if (flag is >= 0 and < GlobalFlagCount)
                _globalFlags[flag] = value;
        }

        public byte GetRoomFlags(int group, int room)
        {
            return group is >= 0 and < GroupCount && room is >= 0 and < RoomsPerGroup
                ? _roomFlags[group][room]
                : (byte)0;
        }

        public void SetRoomFlags(int group, int room, byte value)
        {
            if (group is >= 0 and < GroupCount && room is >= 0 and < RoomsPerGroup)
                _roomFlags[group][room] = value;
        }

        public byte CurrentRoomFlags => GetRoomFlags(Group, Room);

        public void EnterRoom(int group, int room, int entryDirection)
        {
            Group = group;
            Room = room;
            EntryDirection = entryDirection;

            if (PendingRoomFlags >= 0)
            {
                SetRoomFlags(group, room, (byte)PendingRoomFlags);
                PendingRoomFlags = -1;
            }
        }

        /// <summary>
        /// Native obj_Condition semantics: a non-zero mask hides the object when
        /// the current room-state modifier bit is set. requiresFlag/forbiddenFlag
        /// are the interaction-level gate (global flags); roomFlagMask/Value use
        /// the current room's flag byte.
        /// </summary>
        public bool IsVisible(int requiresFlag, int forbiddenFlag, int roomStateMask,
            int roomFlagMask = 0, int roomFlagValue = 0)
        {
            if (requiresFlag >= 0 && !GetFlag(requiresFlag))
                return false;
            if (forbiddenFlag >= 0 && GetFlag(forbiddenFlag))
                return false;
            if (roomStateMask != 0 && (roomStateMask & (1 << RoomStateModifier)) != 0)
                return false;
            if (roomFlagMask != 0 && (CurrentRoomFlags & roomFlagMask) != roomFlagValue)
                return false;
            return true;
        }

        /// <summary>Deep snapshot for the future native save image.</summary>
        public OracleSessionSnapshot Capture()
        {
            var globals = new bool[GlobalFlagCount];
            Array.Copy(_globalFlags, globals, GlobalFlagCount);
            var rooms = new byte[GroupCount][];
            for (var group = 0; group < GroupCount; group++)
            {
                rooms[group] = new byte[RoomsPerGroup];
                Array.Copy(_roomFlags[group], rooms[group], RoomsPerGroup);
            }
            return new OracleSessionSnapshot(globals, rooms, RoomStateModifier, Group, Room, EntryDirection);
        }

        public void Restore(OracleSessionSnapshot snapshot)
        {
            Array.Clear(_globalFlags, 0, _globalFlags.Length);
            Array.Copy(snapshot.GlobalFlags, _globalFlags, Math.Min(GlobalFlagCount, snapshot.GlobalFlags.Length));
            for (var group = 0; group < GroupCount; group++)
            {
                Array.Clear(_roomFlags[group], 0, RoomsPerGroup);
                if (group < snapshot.RoomFlags.Length)
                    Array.Copy(snapshot.RoomFlags[group], _roomFlags[group],
                        Math.Min(RoomsPerGroup, snapshot.RoomFlags[group].Length));
            }
            RoomStateModifier = Math.Clamp(snapshot.RoomStateModifier, 0, 7);
            Group = snapshot.Group;
            Room = snapshot.Room;
            EntryDirection = snapshot.EntryDirection;
        }
    }

    public readonly record struct OracleSessionSnapshot(
        bool[] GlobalFlags,
        byte[][] RoomFlags,
        int RoomStateModifier,
        int Group,
        int Room,
        int EntryDirection);
}
