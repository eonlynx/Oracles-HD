using System;
using Microsoft.Xna.Framework;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
using ProjectZ.InGame.GameSystems;
using ProjectZ.InGame.Map;
using ProjectZ.InGame.SaveLoad;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// Ages time portal (INTERAC_TIMEPORTAL_SPAWNER $e1 / INTERAC_TIMEPORTAL $de).
    /// Touching an active portal toggles the overworld group at the same world
    /// position through the native DX-HD MapTransitionSystem. The arrival portal
    /// is spawned after the destination map finishes loading, matching
    /// link.s:@substate3 (wPortalGroup/wPortalRoom/wPortalPos).
    /// </summary>
    internal class ObjOracleTimePortal : GameObject
    {
        private readonly string _frames;
        private readonly string _durations;
        private readonly int _offsetX;
        private readonly int _offsetY;
        private readonly int _layer;
        private readonly int _loopStart;
        private readonly int _subId;
        private readonly int _anchorX;
        private readonly int _anchorY;

        private readonly DictAtlasEntry[] _loaded;
        private readonly int[] _frameDurations;
        private readonly int _loopFrame;
        private readonly CSprite _sprite;

        private int _frame;
        private float _elapsed;
        private bool _armed;
        private bool _wasInside;
        private bool _triggered;

        public ObjOracleTimePortal(Map.Map map, int posX, int posY, string frames,
            string durations, int offsetX, int offsetY, int layer, int loopStart,
            int subId) : base(map)
        {
            _frames = frames;
            _durations = durations;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _layer = layer;
            _loopStart = loopStart;
            _subId = subId;
            _anchorX = posX;
            _anchorY = posY;

            if (!IsPortalActive(subId))
            {
                IsDead = true;
                return;
            }

            _loaded = OracleSpriteFrames.Load(frames);
            if (_loaded.Length == 0)
            {
                IsDead = true;
                return;
            }

            _frameDurations = OracleSpriteFrames.LoadDurations(durations, _loaded.Length);
            _loopFrame = Math.Clamp(loopStart, 0, _loaded.Length - 1);

            EntityPosition = new CPosition(posX + offsetX, posY + offsetY, 0);
            EntitySize = new Rectangle(0, 0,
                _loaded[0].SourceRectangle.Width, _loaded[0].SourceRectangle.Height);

            _sprite = new CSprite(_loaded[0], EntityPosition);
            AddComponent(DrawComponent.Index, new DrawCSpriteComponent(_sprite, layer));
            AddComponent(UpdateComponent.Index, new UpdateComponent(Update));
        }

        /// <summary>
        /// INTERAC_TIMEPORTAL_SPAWNER state 0: subid 1 is open until the Maku
        /// Tree is saved; subid 2 is open until the Seed Satchel is obtained
        /// (this dev build has no satchel, so it stays open). Ordinary subid 0
        /// portals wait for the Tune of Echoes and its room flag. Bit 7 marks a
        /// portal created by a timewarp, which is always open.
        /// </summary>
        private static bool IsPortalActive(int subId)
        {
            if ((subId & 0x80) != 0)
                return true;

            return (subId & 0x0F) switch
            {
                1 => !OracleFlagState.Get(OracleFlagState.MakuTreeSaved),
                2 => true,
                _ => false,
            };
        }

        private void Update()
        {
            if (_loaded.Length > 1)
                Animate();

            var link = MapManager.ObjLink;
            if (link == null || _triggered)
                return;

            var transitionSystem = (MapTransitionSystem)Game1.GameManager.GameSystems[typeof(MapTransitionSystem)];
            if (transitionSystem.CurrentState != MapTransitionSystem.TransitionState.Idle)
                return;

            var position = link.EntityPosition.Position;
            var feetX = (int)(position.X / Values.TileSize);
            var feetY = (int)(position.Y / Values.TileSize);
            var portalTileX = _anchorX / Values.TileSize;
            var portalTileY = _anchorY / Values.TileSize;
            var frontX = feetX + (link.Direction == 2 ? 1 : link.Direction == 0 ? -1 : 0);
            var frontY = feetY + (link.Direction == 3 ? 1 : link.Direction == 1 ? -1 : 0);
            var inside =
                (feetX == portalTileX && feetY == portalTileY) ||
                (frontX == portalTileX && frontY == portalTileY);

            // The native portal waits in state 1 until Link stops colliding, so
            // arriving on the return portal must not bounce him straight back.
            if (!_armed)
            {
                if (!inside)
                    _armed = true;
                return;
            }

            if (inside && !_wasInside)
            {
                _triggered = true;
                BeginTimewarp(link, transitionSystem);
            }

            _wasInside = inside;
        }

        private void Animate()
        {
            _elapsed += Game1.DeltaTime;
            while (_elapsed >= _frameDurations[_frame])
            {
                _elapsed -= _frameDurations[_frame];
                _frame++;
                if (_frame >= _loaded.Length)
                    _frame = _loopFrame;
                _sprite.SetSprite(_loaded[_frame]);
            }
        }

        private void BeginTimewarp(ObjLink link, MapTransitionSystem transitionSystem)
        {
            var info = Map.OracleInfo;
            if (info == null || !info.IsOverworld)
                return;

            var nextMap = OracleMapInfo.OverworldMap(info.Game, info.Group ^ 1);

            // The native warp destination is a tile position; Link's feet sit on
            // the bottom row of that tile. Using the interaction anchor directly
            // leaves his body inside the tile above, which is often a wall.
            var feetY = (_anchorY & ~15) + 15;
            link.SetNextMapPosition(new Vector2(_anchorX, feetY));
            link.DirectionEntry = 3;

            // link.s:@substate3 creates a return portal at the arrival spot.
            // A portal that was itself created by a timewarp (bit 7) already
            // mirrors a placed portal, so spawning another would duplicate it.
            if ((_subId & 0x80) == 0)
            {
                var worldX = _anchorX;
                var worldY = _anchorY;
                var frames = _frames;
                var durations = _durations;
                var offsetX = _offsetX;
                var offsetY = _offsetY;
                var layer = _layer;
                var loopStart = _loopStart;
                var subId = _subId | 0x80;
                transitionSystem.PostLoadActions.Add(map => map.Objects.SpawnObject(
                    new ObjOracleTimePortal(map, worldX, worldY,
                        frames, durations, offsetX, offsetY, layer, loopStart, subId)));
            }

            transitionSystem.AppendMapChange(nextMap, null, false, false,
                Values.MapTransitionColor, false);
        }
    }
}
