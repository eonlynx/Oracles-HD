using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using ProjectZ.InGame.GameObjects.Base;
using ProjectZ.InGame.GameObjects.Base.CObjects;
using ProjectZ.InGame.GameObjects.Base.Components;
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
            int roomFlagMask, int roomFlagValue) : base(map)
        {
            InteractionId = interactionId;
            SubId = subId;

            if (!OracleFlagState.IsVisible(requiresFlag, forbiddenFlag, roomStateMask,
                    roomFlagMask, roomFlagValue))
            {
                IsDead = true;
                return;
            }

            _frames = LoadFrames(frames);
            if (_frames.Length == 0)
            {
                IsDead = true;
                return;
            }

            _durations = LoadDurations(durations, _frames.Length);
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

            if (_frames.Length > 1)
                AddComponent(UpdateComponent.Index, new UpdateComponent(Update));
        }

        private static DictAtlasEntry[] LoadFrames(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<DictAtlasEntry>();

            var ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var frames = new DictAtlasEntry[ids.Length];
            var count = 0;
            foreach (var id in ids)
            {
                var sprite = Resources.GetSprite(id.Trim());
                if (sprite != null)
                    frames[count++] = sprite;
            }

            if (count == ids.Length)
                return frames;

            var trimmed = new DictAtlasEntry[count];
            Array.Copy(frames, trimmed, count);
            return trimmed;
        }

        private static int[] LoadDurations(string value, int frameCount)
        {
            var durations = new int[frameCount];
            for (var i = 0; i < durations.Length; i++)
                durations[i] = 100;

            if (string.IsNullOrWhiteSpace(value))
                return durations;

            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length && i < durations.Length; i++)
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration))
                    durations[i] = Math.Max(16, duration);

            return durations;
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
