using System;
using System.Collections.Generic;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.Map
{
    public readonly record struct OracleRoomContext(
        OracleSessionState Session, OracleGameProfile Profile,
        int Group, int Room, int EntryDirection);

    /// <summary>
    /// Code-hosted room-entry event (the equivalent of ooa-godot's
    /// IRoomEntryEvent). The event can inspect flags/entry direction and leave
    /// state for the future script runner.
    /// </summary>
    internal interface IOracleRoomEvent
    {
        bool Matches(int group, int room);
        void RoomEntered(OracleRoomContext context);
    }

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
                        // Music is owned by the audio system; the Ralph event
                        // itself is code-hosted (RalphPortalRoomEvent).
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
        private readonly List<IOracleRoomEvent> _events = new();

        public event Action<OracleRoomContext> RoomEntered;

        public OracleRoomRuntime(OracleSessionState session, OracleGameProfile profile)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _events.AddRange(profile.CreateRoomEvents());
        }

        internal void OnMapLoading(Map map)
        {
            var info = map.OracleInfo;
            if (info == null)
                return;

            var link = MapManager.ObjLink;
            var direction = link == null ? -1 : link.DirectionEntry;
            var spawn = link == null
                ? Microsoft.Xna.Framework.Vector2.Zero
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
                ? Microsoft.Xna.Framework.Vector2.Zero
                : link.EntityPosition.Position;
            var room = ResolveRoom(info, position);
            if (_session.Group != info.Group || _session.Room != room)
                _session.EnterRoom(info.Group, room, _session.EntryDirection);

            var context = new OracleRoomContext(_session, _profile, info.Group, room, _session.EntryDirection);
            _effects.Apply(info.RoomEffects, context);

            foreach (var roomEvent in _events)
                if (roomEvent.Matches(info.Group, room))
                    roomEvent.RoomEntered(context);

            RoomEntered?.Invoke(context);
        }

        private static int ResolveRoom(OracleMapInfo info, Microsoft.Xna.Framework.Vector2 position)
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

    /// <summary>room $0:$39 Ralph departure: only from the right, once.</summary>
    internal sealed class RalphPortalRoomEvent : IOracleRoomEvent
    {
        public bool Matches(int group, int room) => group == 0 && room == 0x39;

        public void RoomEntered(OracleRoomContext context)
        {
            var profile = context.Profile;
            context.Session.RalphPortalEventActive =
                context.EntryDirection == 1 &&
                (profile.RalphEnteredPortalFlag < 0 ||
                 !context.Session.GetFlag(profile.RalphEnteredPortalFlag));
        }
    }

    /// <summary>room $1:$39 first past arrival; the villager leaves after flag $41.</summary>
    internal sealed class EnterPastRoomEvent : IOracleRoomEvent
    {
        public bool Matches(int group, int room) => group == 1 && room == 0x39;

        public void RoomEntered(OracleRoomContext context)
        {
            var profile = context.Profile;
            context.Session.EnterPastEventActive =
                profile.EnterPastDoneFlag < 0 ||
                !context.Session.GetFlag(profile.EnterPastDoneFlag);
        }
    }
}
