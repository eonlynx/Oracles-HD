namespace Oracles.Tools.AssetBuilder;

public sealed record AnimationVariant(RgbaImage Image, int DurationMs);

/// <summary>
/// Resolves a metatile's animation timeline from OracleAnimationData and renders
/// each distinct visual state. Frames live in the existing atlas; no extra files.
/// </summary>
public sealed class AnimationComposer
{
    private const int MaxSegments = 256;

    private readonly OracleAnimationData _animation;
    private readonly Dictionary<long, IReadOnlyList<AnimationVariant>?> _cache = new();

    public AnimationComposer(OracleAnimationData animation) => _animation = animation;

    public static long VariantKey(int tilesetId, int metatile, int group, int variant) =>
        0x4000_0000_0000L | ((long)tilesetId << 32) | ((long)metatile << 16) | (uint)variant;

    public IReadOnlyList<AnimationVariant>? GetVariants(
        int tilesetId, int metatile, int group, byte[] mappings, TilesetRenderer renderer)
    {
        if (group is < 0 or > 0xFF)
            return null;

        var key = ((long)tilesetId << 32) | ((long)metatile << 16) | (uint)group;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = Build(metatile, group, mappings, renderer);
        _cache.Add(key, result);
        return result;
    }

    private IReadOnlyList<AnimationVariant>? Build(
        int metatile, int group, byte[] mappings, TilesetRenderer renderer)
    {
        var data = _animation.GetGroup(group);
        if (data == null || data.Tracks.Count == 0)
            return null;

        var slots = new int[4];
        for (var i = 0; i < 4; i++)
            slots[i] = (mappings[metatile * 8 + i] + 0x80) & 0xFF;

        var tracks = data.Tracks.Where(track => Affects(track, slots)).Select(track => new TrackState(track)).ToList();
        if (tracks.Count == 0)
            return null;

        var variants = new List<AnimationVariant>();
        RgbaImage? pendingImage = null;
        var pendingHash = 0u;
        var pendingDuration = 0;

        for (var segment = 0; segment < MaxSegments; segment++)
        {
            var overrides = new Dictionary<int, byte[]>();
            foreach (var state in tracks)
            {
                var frame = state.Track.Frames[state.FrameIndex];
                var header = _animation.GetHeader(frame.GfxIndex);

                foreach (var slot in slots)
                {
                    if (slot < header.DestinationTile || slot >= header.DestinationTile + header.TileCount)
                        continue;

                    var tile = _animation.GetTile(frame.GfxIndex, slot - header.DestinationTile);
                    if (tile != null)
                        overrides[slot] = tile;
                }
            }

            var image = renderer.RenderMetatile(mappings, metatile, overrides);
            var hash = Hash(image);
            var duration = tracks.Min(state => state.Remaining);

            if (pendingImage != null && hash == pendingHash)
            {
                pendingDuration += duration;
            }
            else
            {
                if (pendingImage != null)
                    variants.Add(new AnimationVariant(pendingImage, ToMilliseconds(pendingDuration)));

                pendingImage = image;
                pendingHash = hash;
                pendingDuration = duration;
            }

            var reset = true;
            foreach (var state in tracks)
            {
                state.Remaining -= duration;
                while (state.Remaining <= 0)
                {
                    state.FrameIndex = (state.FrameIndex + 1) % state.Track.Frames.Length;
                    state.Remaining += Math.Max(1, state.Track.Frames[state.FrameIndex].Delay);
                }

                if (state.FrameIndex != 0)
                    reset = false;
            }

            if (reset && segment > 0)
                break;
        }

        if (pendingImage != null)
            variants.Add(new AnimationVariant(pendingImage, ToMilliseconds(pendingDuration)));

        if (variants.Count > 1 && Hash(variants[0].Image) == Hash(variants[^1].Image))
        {
            variants[0] = new AnimationVariant(variants[0].Image, variants[0].DurationMs + variants[^1].DurationMs);
            variants.RemoveAt(variants.Count - 1);
        }

        return variants.Count > 1 ? variants : null;
    }

    private bool Affects(AnimationTrack track, int[] slots)
    {
        foreach (var frame in track.Frames)
        {
            var header = _animation.GetHeader(frame.GfxIndex);
            foreach (var slot in slots)
                if (slot >= header.DestinationTile && slot < header.DestinationTile + header.TileCount)
                    return true;
        }

        return false;
    }

    private static int ToMilliseconds(int ticks)
    {
        return Math.Max(16, (int)Math.Round(ticks * 1000.0 / 60.0));
    }

    private static uint Hash(RgbaImage image)
    {
        var hash = 2166136261u;
        foreach (var value in image.Data)
        {
            hash ^= value;
            hash *= 16777619u;
        }
        return hash;
    }

    private sealed class TrackState
    {
        public AnimationTrack Track { get; }
        public int FrameIndex { get; set; }
        public int Remaining { get; set; }

        public TrackState(AnimationTrack track)
        {
            Track = track;
            FrameIndex = 0;
            Remaining = Math.Max(1, track.Frames[0].Delay);
        }
    }
}
