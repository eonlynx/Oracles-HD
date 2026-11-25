using System;
using Microsoft.Xna.Framework;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
using ProjectZ.InGame.GameSystems;
using ProjectZ.InGame.Map;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// Oracle room warp. The importer emits one of these over each native warp
    /// tile (or screen edge), with the destination already resolved from
    /// warpSources.s/warpDestinations.s. Map changes go through the DX-HD
    /// MapTransitionSystem, so no second transition mechanism is introduced.
    /// </summary>
    internal class ObjOracleWarp : GameObject
    {
        private readonly Rectangle _trigger;
        private readonly string _nextMap;
        private readonly Vector2 _spawnPosition;
        private readonly int _direction;
        private readonly int _transition;
        private readonly int _tileX;
        private readonly int _tileY;

        private bool _armed;
        private bool _wasInside;
        private bool _triggered;

        public ObjOracleWarp(Map.Map map, int posX, int posY, string nextMap,
            int spawnX, int spawnY, int direction, int width, int height, int transition = 0) : base(map)
        {
            if (string.IsNullOrEmpty(nextMap))
            {
                IsDead = true;
                return;
            }

            EntityPosition = new CPosition(posX, posY, 0);
            EntitySize = new Rectangle(0, 0, Math.Max(1, width), Math.Max(1, height));

            _trigger = new Rectangle(posX, posY, Math.Max(1, width), Math.Max(1, height));
            _tileX = posX / Values.TileSize;
            _tileY = posY / Values.TileSize;
            _nextMap = nextMap;
            _spawnPosition = new Vector2(spawnX, spawnY);
            _direction = Math.Clamp(direction, 0, 3);
            _transition = transition;

            AddComponent(UpdateComponent.Index, new UpdateComponent(Update));
        }

        private void Update()
        {
            if (_triggered)
                return;

            var link = MapManager.ObjLink;
            if (link == null)
                return;

            var transitionSystem = (MapTransitionSystem)Game1.GameManager.GameSystems[typeof(MapTransitionSystem)];
            if (transitionSystem.CurrentState != MapTransitionSystem.TransitionState.Idle)
                return;

            var position = link.EntityPosition.Position;
            var feetX = (int)(position.X / Values.TileSize);
            var feetY = (int)(position.Y / Values.TileSize);
            var onWarpTile = feetX == _tileX && feetY == _tileY;

            // A door tile is often only half-walkable (the top half is the
            // wall). Check the tile Link is facing as well, so walking into
            // the doorway triggers even when his body cannot stand on it.
            var frontX = feetX + (link.Direction == 2 ? 1 : link.Direction == 0 ? -1 : 0);
            var frontY = feetY + (link.Direction == 3 ? 1 : link.Direction == 1 ? -1 : 0);
            var facingWarp = frontX == _tileX && frontY == _tileY;
            var inside = onWarpTile || facingWarp || _trigger.Contains((int)position.X, (int)position.Y);

            // Entering a room on top of its exit door must not bounce Link back.
            if (!_armed)
            {
                if (!onWarpTile && !_trigger.Contains((int)position.X, (int)position.Y))
                    _armed = true;
                return;
            }

            if (inside && !_wasInside)
            {
                _triggered = true;
                link.SetNextMapPosition(_spawnPosition);
                link.DirectionEntry = _direction;
                var source = _transition & 0x0f;
                var destination = (_transition >> 4) & 0x0f;
                if (source == 9 || destination == 5)
                    transitionSystem.StartKnockoutTransition = true;
                var instant = source == 4;
                transitionSystem.AppendMapChange(_nextMap, null, false, instant,
                    Values.MapTransitionColor, false);
            }

            _wasInside = inside;
        }
    }
}
