using System;
using Microsoft.Xna.Framework;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
using ProjectZ.InGame.Map;
using ProjectZ.InGame.SaveLoad;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// Native-backed Oracle actor. The interaction anchor is the actor's feet,
    /// matching ObjPersonNew/EnemyOctorok; the composed OAM is drawn with a
    /// CSprite draw offset above that anchor. Body/Draw/Shadow components are
    /// the DX-HD ones, so Y-sorting, water/grass feet effects, gravity, tile
    /// collision and the native shadow pass are inherited instead of reimplemented.
    /// </summary>
    internal class ObjOracleSprite : GameObject
    {
        public int InteractionId { get; }
        public int SubId { get; }
        public int Var03 { get; }
        public int RequiresEntryDirection { get; }

        public BodyComponent Body => _body;

        // Script-visible actor state. Runtime scripts own these values; the
        // sprite only exposes the native object bytes the commands address.
        public int ScriptAnimation { get; set; }
        public int ScriptSpeed { get; set; }
        public int ScriptAngle { get; set; }
        public int Var3F { get; set; }
        public bool ScriptBound { get; set; }
        public bool ScriptCompleted { get; set; }
        public bool ScriptVisible => _sprite?.IsVisible ?? false;

        public void SetScriptVisible(bool visible)
        {
            if (_sprite == null)
                return;

            _sprite.IsVisible = visible;
            _body.IsActive = visible;
            _draw.IsActive = visible;
            _shadow.IsActive = visible;
            if (_collision != null)
                _collision.IsActive = visible;
        }

        public void BeginScriptJump()
        {
            _body.IgnoresZ = true;
            _body.Position.Z = 0;
            _body.Velocity.Z = 0;
        }

        public void SetScriptZ(float z)
        {
            _body.Position.Z = z;
            _body.Velocity.Z = 0;
        }

        public void EndScriptJump()
        {
            _body.IgnoresZ = false;
            _body.Position.Z = 0;
            _body.Velocity.Z = 0;
        }

        private readonly DictAtlasEntry[] _frames;
        private readonly int[] _durations;
        private readonly int _loopStart;
        private readonly CSprite _sprite;
        private readonly BodyComponent _body;
        private readonly BodyDrawComponent _draw;
        private readonly BodyDrawShadowComponent _shadow;
        private readonly BodyCollisionComponent _collision;

        private int _frame;
        private float _elapsed;

        public ObjOracleSprite(Map.Map map, int posX, int posY, string frames,
            string durations, int offsetX, int offsetY, int layer, int loopStart,
            int interactionId, int subId, int var03, int requiresFlag, int forbiddenFlag, int roomStateMask,
            int roomFlagMask, int roomFlagValue, bool solidTile, int radiusY, int radiusX,
            int requiresEntryDirection) : base(map)
        {
            InteractionId = interactionId;
            SubId = subId;
            Var03 = var03;
            RequiresEntryDirection = requiresEntryDirection;

            if (!OracleVisibilityEvaluator.IsVisible(OracleFlagState.Current,
                    interactionId, subId, var03, requiresFlag, forbiddenFlag, roomStateMask,
                    roomFlagMask, roomFlagValue))
            {
                IsDead = true;
                return;
            }

            // One-shot room-entry actors (ralph.s:@initSubid0d) are created
            // only for the native screen-entry direction. A stitched map can
            // cross rooms without reloading, so keep the object dormant and
            // let OracleScriptRuntime activate it when that room is entered
            // from the required direction.
            _frames = OracleSpriteFrames.Load(frames);
            if (_frames.Length == 0)
            {
                IsDead = true;
                return;
            }

            _durations = OracleSpriteFrames.LoadDurations(durations, _frames.Length);
            _loopStart = Math.Clamp(loopStart, 0, _frames.Length - 1);

            // Native convention: EntityPosition is the feet/anchor. The OAM
            // canvas is drawn above it through CSprite.DrawOffset. EntitySize
            // uses the same negative offset so culling sees the full frame.
            EntityPosition = new CPosition(posX, posY, 0);
            var width = _frames[0].SourceRectangle.Width;
            var height = _frames[0].SourceRectangle.Height;

            _sprite = new CSprite(_frames[0], EntityPosition, new Vector2(offsetX, offsetY));

            // Source collision describes the body footprint. A solid tile is
            // the native objectMarkSolidPosition contract; explicit radii use
            // objectSetCollideRadius/Radii. Actors without either still get a
            // small body so they inherit field state, gravity and shadows.
            int bodyX;
            int bodyY;
            int bodyWidth;
            int bodyHeight;
            if (solidTile)
            {
                bodyX = -8;
                bodyY = -8;
                bodyWidth = 16;
                bodyHeight = 16;
            }
            else if (radiusY > 0 || radiusX > 0)
            {
                bodyX = -Math.Max(1, radiusX);
                bodyY = -Math.Max(1, radiusY);
                bodyWidth = Math.Max(2, radiusX * 2);
                bodyHeight = Math.Max(2, radiusY * 2);
            }
            else
            {
                bodyX = -4;
                bodyY = -8;
                bodyWidth = 8;
                bodyHeight = 8;
            }

            _body = new BodyComponent(EntityPosition, bodyX, bodyY,
                bodyWidth, bodyHeight, Math.Max(8, bodyHeight))
            {
                Gravity = -0.15f,
                Drag = 0.85f,
                DragAir = 0.99f,
            };

            // Culling must cover both the composed OAM and the body footprint.
            var minX = Math.Min(offsetX, bodyX);
            var minY = Math.Min(offsetY, bodyY);
            var maxX = Math.Max(offsetX + width, bodyX + bodyWidth);
            var maxY = Math.Max(offsetY + height, bodyY + bodyHeight);
            EntitySize = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            _draw = new BodyDrawComponent(_body, _sprite, layer);
            _shadow = new BodyDrawShadowComponent(_body, _sprite);
            AddComponent(BodyComponent.Index, _body);
            AddComponent(DrawComponent.Index, _draw);
            AddComponent(DrawShadowComponent.Index, _shadow);

            if (solidTile || radiusY > 0 || radiusX > 0)
            {
                _collision = new BodyCollisionComponent(_body,
                    Values.CollisionTypes.Enemy | Values.CollisionTypes.PushIgnore);
                AddComponent(CollisionComponent.Index, _collision);
            }

            if (requiresEntryDirection >= 0)
                _sprite.IsVisible = false;

            if (_frames.Length > 1)
                AddComponent(UpdateComponent.Index, new UpdateComponent(Update));
        }

        private void Update()
        {
            if (_frames.Length <= 1)
                return;

            _elapsed += Game1.DeltaTime;
            while (_elapsed >= _durations[_frame])
            {
                _elapsed -= _durations[_frame];
                _frame++;
                if (_frame >= _frames.Length)
                    _frame = _loopStart;
                _sprite.SetSprite(_frames[_frame]);
            }
        }
    }
}
