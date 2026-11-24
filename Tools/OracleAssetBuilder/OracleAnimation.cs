using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public readonly record struct AnimationHeader(int Sheet, int DestinationTile, int TileCount, int SourceTile);
public readonly record struct AnimationFrame(int GfxIndex, int Delay);

public sealed class AnimationTrack
{
    public AnimationFrame[] Frames { get; }
    public int Period { get; }

    public AnimationTrack(AnimationFrame[] frames)
    {
        Frames = frames;
        Period = frames.Sum(frame => Math.Max(1, frame.Delay));
    }

    /// <summary>Steady-state frame at a tick; delays count engine updates.</summary>
    public int FrameIndexAt(int tick)
    {
        if (Frames.Length == 1)
            return 0;

        var remaining = tick % Math.Max(1, Period);
        for (var i = 0; i < Frames.Length; i++)
        {
            var delay = Math.Max(1, Frames[i].Delay);
            if (remaining < delay)
                return i;
            remaining -= delay;
        }
        return 0;
    }
}

public sealed class AnimationGroup
{
    public int InitialState { get; }
    public List<AnimationTrack> Tracks { get; } = new();

    public AnimationGroup(int initialState) => InitialState = initialState;
}

/// <summary>
/// Oracle background-DMA animations. The original dumper names the pair fields
/// explicitly: oracles-disasm/tools/dump/dumpAnimationData.py reads
/// `counter = rom[address]` then `gfxIndex = rom[address + 1]` and writes
/// `.db counter gfxIndex`. So each pair is (delay, gfxIndex). `ooa-godot`
/// uses the same order; its regressions pin waterfall headers 0,24,32.
/// </summary>
public sealed class OracleAnimationData
{
    private static readonly Regex HeaderRegex = new(
        @"m_GfxHeaderAnim\s+gfx_animations_(?<sheet>[1-3]),\s*\$(?<destination>[0-9a-fA-F]{4}),\s*\$(?<count>[0-9a-fA-F]{2}),\s*\$(?<source>[0-9a-fA-F]{3})",
        RegexOptions.Compiled);

    private static readonly Regex PairRegex = new(
        @"\.db\s+\$(?<gfx>[0-9a-fA-F]{2})\s+\$(?<delay>[0-9a-fA-F]{2})",
        RegexOptions.Compiled);

    private static readonly Regex LabelRegex = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):",
        RegexOptions.Compiled);

    private static readonly Regex GroupRegex = new(
        @"^animationGroup(?<index>[0-9a-fA-F]{2}):",
        RegexOptions.Compiled);

    private static readonly Regex GroupStateRegex = new(
        @"^\s*\.db\s+\$(?<state>8[0-9a-fA-F])",
        RegexOptions.Compiled);

    private readonly OracleRepo _repo;
    private readonly List<AnimationHeader> _headers = new();
    private readonly Dictionary<string, AnimationTrack> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<int, AnimationGroup> _groups = new();
    private readonly Indexed2bppImage?[] _sheets = new Indexed2bppImage?[4];

    public IReadOnlyList<AnimationHeader> Headers => _headers;
    public IReadOnlyDictionary<int, AnimationGroup> Groups => _groups;

    public OracleAnimationData(OracleRepo repo)
    {
        _repo = repo;
        LoadHeaders();
        LoadTracks();
        LoadGroups();
        LoadSheets();
    }

    public AnimationHeader GetHeader(int index) => _headers[index];

    public AnimationGroup? GetGroup(int group) => _groups.TryGetValue(group, out var value) ? value : null;

    public byte[]? GetTile(int headerIndex, int slotOffset)
    {
        if (headerIndex < 0 || headerIndex >= _headers.Count)
            return null;

        var header = _headers[headerIndex];
        if (header.Sheet is < 1 or > 3)
            return null;

        var sheet = _sheets[header.Sheet];
        if (sheet == null)
            return null;

        var tileIndex = header.SourceTile + slotOffset;
        var tilesX = sheet.Width / 8;
        var maxTile = tilesX * (sheet.Height / 8);
        if (tileIndex < 0 || tileIndex >= maxTile)
            return null;

        var tile = new byte[64];
        var sourceX = (tileIndex % tilesX) * 8;
        var sourceY = (tileIndex / tilesX) * 8;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                tile[y * 8 + x] = sheet[sourceX + x, sourceY + y];
        return tile;
    }

    private void LoadHeaders()
    {
        var path = Path.Combine(_repo.DataDir, "animationGfxHeaders.s");
        foreach (var line in File.ReadLines(path))
        {
            var match = HeaderRegex.Match(line);
            if (!match.Success)
                continue;

            var destination = int.Parse(match.Groups["destination"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var address = destination & 0xFFF0;
            var destinationTile = address >= 0x9000
                ? 128 + (address - 0x9000) / 16
                : (address - 0x8800) / 16;
            var tileCount = int.Parse(match.Groups["count"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) + 1;
            var sourceTile = int.Parse(match.Groups["source"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 16;
            var sheet = int.Parse(match.Groups["sheet"].Value, CultureInfo.InvariantCulture);

            _headers.Add(new AnimationHeader(sheet, destinationTile, tileCount, sourceTile));
        }

        if (_headers.Count == 0)
            throw new InvalidDataException($"{path}: no animation headers parsed.");
    }

    private void LoadTracks()
    {
        var path = Path.Combine(_repo.DataDir, "animationData.s");
        string? label = null;
        var frames = new List<AnimationFrame>();

        void Flush()
        {
            if (label != null && frames.Count > 0)
                _tracks[label] = new AnimationTrack(frames.ToArray());
            frames.Clear();
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw;
            var labelMatch = LabelRegex.Match(line);
            if (labelMatch.Success)
            {
                Flush();
                label = labelMatch.Groups["label"].Value;
                continue;
            }

            if (label == null)
                continue;

            if (line.Contains("m_AnimationLoop", StringComparison.Ordinal))
                continue;

            foreach (Match pair in PairRegex.Matches(line))
            {
                // .db <delay> <gfxIndex> (dumpAnimationData.py: counter, gfxIndex)
                var delay = int.Parse(pair.Groups["gfx"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var gfx = int.Parse(pair.Groups["delay"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                frames.Add(new AnimationFrame(gfx, delay));
            }
        }

        Flush();
    }

    private void LoadGroups()
    {
        var path = Path.Combine(_repo.DataDir, "animationGroups.s");
        var pending = new List<int>();
        var current = new Dictionary<int, AnimationGroup>();
        var reading = false;

        void Flush()
        {
            if (!reading)
                return;

            foreach (var pair in current)
                _groups[pair.Key] = pair.Value;

            pending.Clear();
            current.Clear();
            reading = false;
        }

        foreach (var line in File.ReadLines(path))
        {
            var groupMatch = GroupRegex.Match(line);
            if (groupMatch.Success)
            {
                Flush();
                pending.Add(int.Parse(groupMatch.Groups["index"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                continue;
            }

            if (pending.Count > 0 && GroupStateRegex.IsMatch(line))
            {
                var state = int.Parse(GroupStateRegex.Match(line).Groups["state"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                foreach (var id in pending)
                    current[id] = new AnimationGroup(state);
                reading = true;
                continue;
            }

            if (!reading)
                continue;

            var trackMatch = Regex.Match(line, @"\.dw\s+(?<label>animationData[A-Za-z0-9_]+)");
            if (trackMatch.Success)
            {
                var trackLabel = trackMatch.Groups["label"].Value;
                if (!_tracks.TryGetValue(trackLabel, out var track))
                    throw new InvalidDataException($"{path}: animation track '{trackLabel}' was not parsed from animationData.s.");

                foreach (var group in current.Values)
                    group.Tracks.Add(track);
                continue;
            }

            if (LabelRegex.IsMatch(line))
                Flush();
        }

        Flush();
    }

    private void LoadSheets()
    {
        for (var sheet = 1; sheet <= 3; sheet++)
        {
            var png = _repo.FindPng($"gfx_animations_{sheet}");
            if (png != null)
                _sheets[sheet] = Png.LoadIndexed2bpp(png);
        }
    }

    public string Describe(int group)
    {
        if (!_groups.TryGetValue(group, out var data))
            return $"animation group {group:x2}: not found";

        var lines = new List<string>
        {
            $"animation group {group:x2}: initial state ${data.InitialState:x2}, {data.Tracks.Count} track(s)"
        };

        for (var i = 0; i < data.Tracks.Count; i++)
        {
            var track = data.Tracks[i];
            var frames = string.Join(", ", track.Frames.Select(f => $"{f.GfxIndex:x2}/{f.Delay}"));
            lines.Add($"  track {i}: period {track.Period}  [{frames}]");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
