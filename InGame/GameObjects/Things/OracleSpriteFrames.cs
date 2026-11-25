using System;
using System.Globalization;
using ProjectZ.InGame.SaveLoad;
using ProjectZ.InGame.Things;

namespace ProjectZ.InGame.GameObjects.Things
{
    /// <summary>
    /// Shared parsing for importer-composed Oracle sprite frames. The atlas
    /// entry ids and the per-frame durations are embedded in the .map, so no
    /// sidecar animation file is needed.
    /// </summary>
    internal static class OracleSpriteFrames
    {
        public static DictAtlasEntry[] Load(string value)
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

        public static int[] LoadDurations(string value, int frameCount)
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
    }
}
