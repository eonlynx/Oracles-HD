using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ProjectZ.Base;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Map
{
    public readonly record struct OracleRoomContext(
        OracleSessionState Session, OracleGameProfile Profile,
        int Group, int Room, int EntryDirection);

    /// <summary>
    /// Room-wide entry handlers from `roomSpecificCode.s`. The native handler
    /// number is the jump-table index; unsupported handlers are explicit no-ops
    /// until their owners (music, minimap, respawn) exist.
    /// </summary>
    internal sealed class OracleRoomEntryEffects
    {
        private const int PalaceMinimap = 0x04;
        private const int NayruHouseMusic = 0x05;
        private const int DeathRespawn = 0x06;
        private const int RalphPortalMusic = 0x08;
        private const int FinishedGameMusic = 0x09;
        private const int PresentMermaidMinimap = 0x0a;
        private const int PastMermaidMinimap = 0x0b;
        private const int VireMusic = 0x0c;

        public void Apply(IReadOnlyList<OracleRoomEffectInfo> effects, OracleRoomContext context)
        {
            foreach (var effect in effects)
            {
                if (effect.Group != context.Group || effect.Room != context.Room)
                    continue;

                switch (effect.Handler)
                {
                    case RalphPortalMusic:
                        // Music is owned by the audio system; the Ralph script
                        // itself is bound by OracleScriptRuntime.
                        break;
                    case NayruHouseMusic:
                    case FinishedGameMusic:
                    case VireMusic:
                        // Oracle music tracks are not imported yet.
                        break;
                    case PalaceMinimap:
                    case PresentMermaidMinimap:
                    case PastMermaidMinimap:
                        // Oracle minimap state is not imported yet.
                        break;
                    case DeathRespawn:
                        // Death/respawn presets are not wired yet.
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Owns the current Oracle room identity and the load/enter lifecycle. Hooks
    /// are invoked by MapTransitionSystem: MapLoading before objects are built,
    /// MapLoaded after Link is placed.
    /// </summary>
    public sealed class OracleRoomRuntime
    {
        private readonly OracleSessionState _session;
        private readonly OracleGameProfile _profile;
        private readonly OracleRoomEntryEffects _effects = new();
        private readonly OracleScriptRuntime _scripts;

        private OracleMapInfo _mapInfo;
        private int _lastRoom = -1;
        private Vector2 _lastPosition;
        private bool _trackingRoom;

        public event Action<OracleRoomContext> RoomEntered;

        public OracleRoomRuntime(OracleSessionState session, OracleGameProfile profile)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _scripts = new OracleScriptRuntime(session, profile);
        }

        internal void OnMapLoading(Map map)
        {
            _scripts.OnMapLoading();
            _mapInfo = map?.OracleInfo;
            _trackingRoom = false;
            _lastRoom = -1;

            var info = map.OracleInfo;
            if (info == null)
                return;

            var link = MapManager.ObjLink;
            var direction = Game1.OracleEntryDirection >= 0
                ? Game1.OracleEntryDirection
                : link == null ? -1 : link.DirectionEntry;
            Game1.OracleEntryDirection = -1;
            var spawn = link == null
                ? Vector2.Zero
                : link.NextMapPositionStart ?? link.EntityPosition.Position;
            _session.EnterRoom(info.Group, ResolveRoom(info, spawn), direction);
        }

        internal void OnMapLoaded(Map map)
        {
            var info = map.OracleInfo;
            if (info == null)
                return;

            var link = MapManager.ObjLink;
            var position = link == null
                ? Vector2.Zero
                : link.EntityPosition.Position;
            var room = ResolveRoom(info, position);
            if (_session.Group != info.Group || _session.Room != room)
                _session.EnterRoom(info.Group, room, _session.EntryDirection);

            _mapInfo = info;
            _lastRoom = room;
            _lastPosition = position;
            _trackingRoom = true;
            OracleScriptRuntime.Trace(
                $"map {info.Game} group {info.Group:x} room {room:x2} dir {_session.EntryDirection} pos {position.X:0},{position.Y:0}");

            var context = new OracleRoomContext(_session, _profile, info.Group, room, _session.EntryDirection);
            OraclePerf.Measure("  Oracle room effects",
                () => _effects.Apply(info.RoomEffects, context));

            RoomEntered?.Invoke(context);
            OraclePerf.Measure("  Oracle script bind",
                () => _scripts.OnMapLoaded(map));
        }

        /// <summary>
        /// Called once per game update, before map object movement. Tracks
        /// stitched-map room crossings as well as script counters so room-entry
        /// actors work without a screen load.
        /// </summary>
        internal void Update()
        {
            TrackRoom();
            _scripts.Update();
        }

        private void TrackRoom()
        {
            if (!_trackingRoom || _mapInfo == null)
                return;

            var link = MapManager.ObjLink;
            if (link == null)
                return;

            var position = link.EntityPosition.Position;
            var room = ResolveRoom(_mapInfo, position);
            if (room == _lastRoom)
            {
                _lastPosition = position;
                return;
            }

            var direction = DirectionFromDelta(position - _lastPosition, link.DirectionEntry);
            _session.EnterRoom(_mapInfo.Group, room, direction);
            _lastRoom = room;
            _lastPosition = position;
            OracleScriptRuntime.Trace(
                $"room {_mapInfo.Group:x}:{room:x2} dir {direction} pos {position.X:0},{position.Y:0}");

            var context = new OracleRoomContext(_session, _profile, _mapInfo.Group, room, direction);
            _effects.Apply(_mapInfo.RoomEffects, context);
            RoomEntered?.Invoke(context);
            _scripts.RoomEntered(_mapInfo.Group, room, direction);
        }

        private static int DirectionFromDelta(Vector2 delta, int fallback)
        {
            if (Math.Abs(delta.X) > Math.Abs(delta.Y) && Math.Abs(delta.X) > 0.01f)
                return delta.X > 0 ? 1 : 3;
            if (Math.Abs(delta.Y) > 0.01f)
                return delta.Y > 0 ? 2 : 0;
            return fallback is >= 0 and <= 3 ? fallback : 0;
        }

        internal static int ResolveRoom(OracleMapInfo info, Microsoft.Xna.Framework.Vector2 position)
        {
            if (info.Room >= 0)
                return info.Room;

            var tileWidth = Math.Max(1, info.RoomWidth * Values.TileSize);
            var tileHeight = Math.Max(1, info.RoomHeight * Values.TileSize);
            var x = Math.Clamp((int)(position.X / tileWidth), 0, 0x0f);
            var y = Math.Clamp((int)(position.Y / tileHeight), 0, 0x0f);
            return (y << 4) | x;
        }
    }
}
