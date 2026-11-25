using System;
using System.Globalization;
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
    /// Draws a pre-composed Oracle sprite animation. The importer bakes the OAM
    /// composition into the generated atlas and embeds the frame list and
    /// timeline in the .map, so no .ani or sidecar animation file is needed.
    /// </summary>
    internal class ObjOracleSprite : GameObject
    {
        public int InteractionId { get; }
        public int SubId { get; }

        private readonly DictAtlasEntry[] _frames;
        private readonly int[] _durations;
        private readonly int _loopStart;
        private readonly CSprite _sprite;

        private int _frame;
        private float _elapsed;

        public ObjOracleSprite(Map.Map map, int posX, int posY, string frames,
            string durations, int offsetX, int offsetY, int layer, int loopStart,
            int interactionId, int subId, int requiresFlag, int forbiddenFlag, int roomStateMask,
            int roomFlagMask, int roomFlagValue, bool solidTile, int radiusY, int radiusX,
            int requiresEntryDirection) : base(map)
        {
            InteractionId = interactionId;
            SubId = subId;

            if (!OracleFlagState.IsVisible(requiresFlag, forbiddenFlag, roomStateMask,
                    roomFlagMask, roomFlagValue))
            {
                IsDead = true;
                return;
            }

            // One-shot room-entry actors (ralph.s:@initSubid0d) delete themselves
            // unless Link entered from the direction the source checks.
            if (requiresEntryDirection >= 0 &&
                MapManager.ObjLink != null &&
                MapManager.ObjLink.DirectionEntry != requiresEntryDirection)
            {
                IsDead = true;
                return;
            }

            _frames = OracleSpriteFrames.Load(frames);
            if (_frames.Length == 0)
            {
                IsDead = true;
                return;
            }

            _durations = OracleSpriteFrames.LoadDurations(durations, _frames.Length);
            _loopStart = Math.Clamp(loopStart, 0, _frames.Length - 1);

            // DX-HD's draw pool sorts by EntityPosition and expects the sprite
            // top-left there; negative EntitySize offsets cull incorrectly.
            // Keep the composed top-left for now and record the tall-actor
            // depth limitation in the audit instead of adding a second anchor.
            EntityPosition = new CPosition(posX + offsetX, posY + offsetY, 0);

            EntitySize = new Rectangle(0, 0,
                _frames[0].SourceRectangle.Width, _frames[0].SourceRectangle.Height);

            _sprite = new CSprite(_frames[0], EntityPosition);
            AddComponent(DrawComponent.Index, new DrawCSpriteComponent(_sprite, layer));

            // Native object collision: objectMarkSolidPosition marks the object's
            // tile solid (16x16); objectSetCollideRadius/Radii give an explicit
            // Y/X radius. The body is always centred on the interaction anchor
            // (the object's feet), not on the OAM top-left.
            if (solidTile || radiusY > 0 || radiusX > 0)
            {
                var boxHeight = solidTile ? 16 : radiusY * 2;
                var boxWidth = solidTile ? 16 : radiusX * 2;
                boxHeight = Math.Min(boxHeight, EntitySize.Height);
                boxWidth = Math.Min(boxWidth, EntitySize.Width);

                var anchorX = posX - (int)EntityPosition.X;
                var anchorY = posY - (int)EntityPosition.Y;
                var boxX = Math.Clamp(anchorX - boxWidth / 2, 0, EntitySize.Width - boxWidth);
                var boxY = Math.Clamp(anchorY - boxHeight / 2, 0, EntitySize.Height - boxHeight);

                if (boxWidth > 0 && boxHeight > 0)
                    AddComponent(CollisionComponent.Index, new BoxCollisionComponent(
                        new CBox(EntityPosition, boxX, boxY, 0,
                            boxWidth, boxHeight, 16),
                        Values.CollisionTypes.Normal));
            }

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
