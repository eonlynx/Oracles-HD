using System.Globalization;
using System.Text.RegularExpressions;

namespace Oracles.Tools.AssetBuilder;

public sealed record OamBlock(int Y, int X, int Tile, int Flags);

/// <summary>
/// Native animation row. The engine reads byte 1 into the OAM table index;
/// byte 2 is Interaction.animParameter, which is currently unused.
/// </summary>
public sealed record InteractionFrame(int Duration, int PointerOffset);

public sealed record InteractionAnimation(InteractionFrame[] Frames, int LoopStart, string Label);

public sealed record InteractionGraphics(int GfxHeader, int TileBase, int Palette, int DefaultAnimation);

public sealed record ComposedSprite(RgbaImage Image, int OffsetX, int OffsetY);

/// <summary>
/// Interaction presentation data. The parsing and OAM composition follow
/// ooa-godot's tools/import_oracles/Shared/Read-InteractionPresentation.ps1 and
/// src/Features/Graphics/OracleOamCompositor.cs: animation tables, one
/// contiguous OAM-pointer stream, 8x16 OAM cells and XOR palette selection.
/// </summary>
public sealed class InteractionSprites
{
    private static readonly Regex InteractionDataRegex = new(
        @"/\*\s*\$(?<id>[0-9a-fA-F]{2})\s*\*/\s*m_InteractionData\s+(?<a>[^;\r\n]+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex SubidLineRegex = new(
        @"^m_InteractionSubidData\s+\$(?<gfx>[0-9a-fA-F]+)\s+\$(?<mid>[0-9a-fA-F]+)\s+\$(?<flags>[0-9a-fA-F]+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex GfxHeaderRegex = new(
        @"/\*\s*\$(?<id>[0-9a-fA-F]{2})\s*\*/\s*m_ObjectGfxHeader\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<rest>[^;\r\n]*)",
        RegexOptions.Compiled);

    private static readonly Regex LabelRegex = new(
        @"^(?<label>[A-Za-z_][A-Za-z0-9_]*):\s*$", RegexOptions.Compiled);

    private static readonly Regex DwRegex = new(
        @"\.dw\s+(?<label>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex AnimationLabelRegex = new(
        @"^interactionAnimation(?<id>[0-9a-fA-F]+)(?<loop>Loop)?:\s*$", RegexOptions.Compiled);

    private static readonly Regex HexByteRegex = new(
        @"\$(?<value>[0-9a-fA-F]{1,2})", RegexOptions.Compiled);

    private readonly string _gfxDir;
    private readonly Dictionary<int, InteractionGraphics> _inlineData = new();
    private readonly Dictionary<string, List<InteractionGraphics>> _subidData = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string[]> _gfxHeaders = new();
    private readonly Dictionary<string, List<string>> _animationTables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _oamPointerStarts = new(StringComparer.Ordinal);
    private readonly List<string> _oamPointerStream = new();
    private readonly Dictionary<string, InteractionAnimation> _animations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OamBlock[]> _oamBlocks = new(StringComparer.Ordinal);
    private readonly Dictionary<int, SpriteSheet> _sheets = new();
    private readonly Rgba[][] _palettes;

    public InteractionSprites(OracleRepo repo)
    {
        _gfxDir = repo.GfxCompressibleDir;

        LoadInteractionData(Path.Combine(repo.DataDir, "interactionData.s"));
        LoadGfxHeaders(Path.Combine(repo.DataDir, "objectGfxHeaders.s"));
        LoadAnimations(Path.Combine(repo.DataDir, "interactionAnimations.s"));
        LoadOamBlocks(Path.Combine(repo.DataDir, "interactionOamData.s"));
        _palettes = LoadStandardPalettes(Path.Combine(repo.DataDir, "paletteData.s"));
    }

    public bool TryCompose(int interactionId, int subid, out ComposedSprite[] frames,
        out int[] durations, out int loopStart,
        IReadOnlyDictionary<int, Rgba[]>? paletteOverrides = null)
    {
        frames = Array.Empty<ComposedSprite>();
        durations = Array.Empty<int>();
        loopStart = 0;
        if (!TryGetGraphics(interactionId, subid, out var graphics) || graphics.GfxHeader == 0)
            return false;

        // Palettes 6/7 are room/object sprite palettes loaded at runtime; the
        // importer only has the standard 0-5 set. Do not fall back to a wrong
        // palette (e.g. the post-game Link statue $6b:$15 uses palette 6).
        if (graphics.Palette >= _palettes.Length &&
            (paletteOverrides == null || !paletteOverrides.ContainsKey(graphics.Palette)))
            return false;

        var animationTable = $"interaction{interactionId:x2}Animations";
        if (!_animationTables.TryGetValue(animationTable, out var animations) || animations.Count == 0)
            return false;

        var animationIndex = Math.Clamp(graphics.DefaultAnimation, 0, animations.Count - 1);
        var animationLabel = animations[animationIndex];
        if (!_animations.TryGetValue(animationLabel, out var animation))
            return false;

        var pointerKey = $"interaction{interactionId:x2}OamDataPointers";
        if (!_oamPointerStarts.TryGetValue(pointerKey, out var pointerStart))
            return false;

        var sheet = GetSheet(graphics.GfxHeader);
        if (sheet == null)
            return false;

        var blocksByFrame = new List<OamBlock[]>();
        var frameDurations = new List<int>();
        foreach (var frame in animation.Frames)
        {
            var pointerIndex = pointerStart + frame.PointerOffset / 2;
            if (pointerIndex < 0 || pointerIndex >= _oamPointerStream.Count)
                continue;
            var oamLabel = _oamPointerStream[pointerIndex];
            if (!_oamBlocks.TryGetValue(oamLabel, out var blocks) || blocks.Length == 0)
                continue;
            blocksByFrame.Add(blocks);
            frameDurations.Add(Math.Max(1, frame.Duration) * 1000 / 60);
        }

        if (blocksByFrame.Count == 0)
            return false;

        // Skip the whole interaction if any OAM block selects palette 6/7;
        // a partial sprite would be worse than omitting it.
        foreach (var blocks in blocksByFrame)
            foreach (var block in blocks)
            {
                var palette = (graphics.Palette ^ (block.Flags & 0x07)) & 0x07;
                if (palette >= _palettes.Length &&
                    (paletteOverrides == null || !paletteOverrides.ContainsKey(palette)))
                    return false;
            }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var blocks in blocksByFrame)
        {
            foreach (var block in blocks)
            {
                var x = block.X;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, block.Y);
                maxX = Math.Max(maxX, x + 8);
                maxY = Math.Max(maxY, block.Y + 16);
            }
        }

        var composed = new List<ComposedSprite>(blocksByFrame.Count);
        foreach (var blocks in blocksByFrame)
        {
            var image = new RgbaImage(maxX - minX, maxY - minY);
            // Lower OAM indices win: compose in reverse.
            for (var i = blocks.Length - 1; i >= 0; i--)
                DrawBlock(image, sheet, graphics, blocks[i], minX, minY, paletteOverrides);
            composed.Add(new ComposedSprite(image, minX, minY));
        }

        frames = composed.ToArray();
        durations = frameDurations.ToArray();
        loopStart = Math.Clamp(animation.LoopStart, 0, frames.Length - 1);
        return frames.Length > 0;
    }

    private void DrawBlock(RgbaImage image, SpriteSheet sheet, InteractionGraphics graphics, OamBlock block,
        int originX, int originY, IReadOnlyDictionary<int, Rgba[]>? paletteOverrides)
    {
        var cellsPerRow = Math.Max(1, sheet.Width / 8);
        var cell = ((graphics.TileBase + block.Tile) & 0xfe) / 2;
        var sourceX = cell % cellsPerRow * 8;
        var sourceY = cell / cellsPerRow * 16;
        if (sourceX < 0 || sourceX + 8 > sheet.Width || sourceY < 0 || sourceY + 16 > sheet.Height)
            return;

        var flipX = (block.Flags & 0x20) != 0;
        var flipY = (block.Flags & 0x40) != 0;
        var palette = (graphics.Palette ^ (block.Flags & 0x07)) & 0x07;
        var colors = paletteOverrides != null && paletteOverrides.TryGetValue(palette, out var overrideColors)
            ? overrideColors
            : palette >= _palettes.Length ? null : _palettes[palette];
        if (colors == null)
            return;

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var readX = sourceX + (flipX ? 7 - x : x);
                var readY = sourceY + (flipY ? 15 - y : y);
                var shade = sheet[readX, readY];
                if (shade == 0)
                    continue;

                var writeX = block.X + x - originX;
                var writeY = block.Y + y - originY;
                if (writeX < 0 || writeX >= image.Width || writeY < 0 || writeY >= image.Height)
                    continue;
                image[writeX, writeY] = colors[shade];
            }
        }
    }

    private SpriteSheet? GetSheet(int gfxHeader)
    {
        if (_sheets.TryGetValue(gfxHeader, out var cached))
            return cached;

        if (!_gfxHeaders.TryGetValue(gfxHeader, out var sheetNames) || sheetNames.Length == 0)
            return null;

        // A continuation chain is rare for interactions; compose the sheets
        // vertically using the same 128-pixel slot model as ooa-godot. If the
        // widths differ, keep the first sheet and reject the rest.
        var sheet = LoadSheet(sheetNames[0]);
        if (sheet == null)
            return null;

        for (var i = 1; i < sheetNames.Length; i++)
        {
            var next = LoadSheet(sheetNames[i]);
            if (next == null || next.Width != sheet.Width)
                break;
            sheet = SpriteSheet.Vertical(sheet, next);
        }

        _sheets[gfxHeader] = sheet;
        return sheet;
    }

    private SpriteSheet? LoadSheet(string name)
    {
        var path = Path.Combine(_gfxDir, name + ".png");
        if (!File.Exists(path))
            return null;
        var image = Png.LoadIndexed2bpp(path);
        return new SpriteSheet(image.Width, image.Height, image.Indices);
    }

    private bool TryGetGraphics(int interactionId, int subid, out InteractionGraphics graphics)
    {
        // Pointer entries have a subid table; inline entries have one row.
        var key = $"interaction{interactionId:x2}SubidData";
        if (_subidData.TryGetValue(key, out var list) && list.Count > 0)
        {
            graphics = list[Math.Clamp(subid, 0, list.Count - 1)];
            return true;
        }

        if (_inlineData.TryGetValue(interactionId, out var inline))
        {
            graphics = inline;
            return true;
        }

        graphics = default!;
        return false;
    }

    private void LoadInteractionData(string path)
    {
        var lines = File.ReadAllLines(path);
        string? pendingSubidLabel = null;
        var pendingSubidValues = new List<InteractionGraphics>();

        void FlushSubid()
        {
            if (pendingSubidLabel != null && pendingSubidValues.Count > 0)
                _subidData[pendingSubidLabel] = new List<InteractionGraphics>(pendingSubidValues);
            pendingSubidLabel = null;
            pendingSubidValues.Clear();
        }

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                FlushSubid();
                if (label.Groups["label"].Value.StartsWith("interaction", StringComparison.Ordinal) &&
                    label.Groups["label"].Value.EndsWith("SubidData", StringComparison.Ordinal))
                    pendingSubidLabel = label.Groups["label"].Value;
                continue;
            }

            var data = InteractionDataRegex.Match(line);
            if (data.Success)
            {
                var id = ParseHex(data.Groups["id"].Value);
                var args = data.Groups["a"].Value.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 1)
                {
                    if (args[0].StartsWith("$", StringComparison.Ordinal))
                        throw new InvalidDataException($"{path}: m_InteractionData with one numeric argument is invalid.");
                    // Pointer entries are resolved through interaction<id>SubidData.
                }
                else if (args.Length >= 3)
                {
                    var flags = ParseNumber(args[2]);
                    _inlineData[id] = new InteractionGraphics(
                        ParseNumber(args[0]),
                        ParseNumber(args[1]) & 0x7f,
                        (flags >> 4) & 0x07,
                        flags & 0x0f);
                }
                continue;
            }

            if (pendingSubidLabel != null)
            {
                var subid = SubidLineRegex.Match(line);
                if (subid.Success)
                {
                    var flags = ParseNumber(subid.Groups["flags"].Value);
                    pendingSubidValues.Add(new InteractionGraphics(
                        ParseNumber(subid.Groups["gfx"].Value),
                        ParseNumber(subid.Groups["mid"].Value) & 0x7f,
                        (flags >> 4) & 0x07,
                        flags & 0x0f));
                    continue;
                }

                if (line.StartsWith("m_InteractionSubidDataEnd", StringComparison.Ordinal))
                    FlushSubid();
            }
        }

        FlushSubid();
    }

    private void LoadGfxHeaders(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var match = GfxHeaderRegex.Match(raw);
            if (!match.Success)
                continue;

            var id = ParseHex(match.Groups["id"].Value);
            var names = new List<string> { match.Groups["name"].Value };
            var rest = match.Groups["rest"].Value;
            if (Regex.IsMatch(rest, @",\s*1\s*$"))
            {
                // The next header is the continuation sheet.
                var next = FindNextGfxName(path, id + 1);
                if (next != null)
                    names.Add(next);
            }
            _gfxHeaders[id] = names.ToArray();
        }
    }

    private static string? FindNextGfxName(string path, int id)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var match = GfxHeaderRegex.Match(raw);
            if (match.Success && ParseHex(match.Groups["id"].Value) == id)
                return match.Groups["name"].Value;
        }
        return null;
    }

    private void LoadAnimations(string path)
    {
        var lines = File.ReadAllLines(path);

        // Per-label animation tables. Empty labels share the next non-empty
        // block, matching labels declared at the same assembler address.
        var tableOrder = new List<string>();
        var tableValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? currentTable = null;
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                currentTable = label.Groups["label"].Value;
                if (currentTable.StartsWith("interaction", StringComparison.Ordinal) &&
                    currentTable.EndsWith("Animations", StringComparison.Ordinal) &&
                    currentTable != "interactionAnimationTable")
                {
                    tableOrder.Add(currentTable);
                    tableValues[currentTable] = new List<string>();
                }
                else
                {
                    currentTable = null;
                }
                continue;
            }

            if (currentTable == null)
                continue;

            var dw = DwRegex.Match(line);
            if (dw.Success && dw.Groups["label"].Value.StartsWith("interactionAnimation", StringComparison.Ordinal))
                tableValues[currentTable].Add(dw.Groups["label"].Value);
        }

        string? nextNonEmpty = null;
        for (var i = tableOrder.Count - 1; i >= 0; i--)
        {
            var key = tableOrder[i];
            if (tableValues[key].Count > 0)
                nextNonEmpty = key;
            else if (nextNonEmpty != null)
                tableValues[key] = new List<string>(tableValues[nextNonEmpty]);
            if (tableValues[key].Count > 0)
                _animationTables[key] = tableValues[key];
        }

        // One contiguous OAM-pointer stream. A table label is a view into the
        // stream, so an animation may index past the next label.
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                var name = label.Groups["label"].Value;
                if (name.StartsWith("interaction", StringComparison.Ordinal) &&
                    name.EndsWith("OamDataPointers", StringComparison.Ordinal))
                    _oamPointerStarts[name] = _oamPointerStream.Count;
                continue;
            }

            var dw = DwRegex.Match(line);
            if (dw.Success && dw.Groups["label"].Value.StartsWith("interactionOamData", StringComparison.Ordinal))
                _oamPointerStream.Add(dw.Groups["label"].Value);
        }

        ParseAnimationDefinitions(lines);
    }

    private void ParseAnimationDefinitions(string[] lines)
    {
        InteractionAnimation? current = null;
        var currentLabel = "";
        var frames = new List<InteractionFrame>();
        var loopTarget = "";
        var labels = new Dictionary<string, (InteractionAnimation Animation, int FrameIndex)>(StringComparer.Ordinal);

        void Flush()
        {
            if (current == null)
                return;
            var loopStart = 0;
            if (!string.IsNullOrEmpty(loopTarget) && labels.TryGetValue(loopTarget, out var target) &&
                ReferenceEquals(target.Animation, current))
                loopStart = target.FrameIndex;
            var finished = current with { Frames = frames.ToArray(), LoopStart = loopStart };
            _animations[currentLabel] = finished;
            labels[currentLabel] = (finished, 0);
            current = null;
            frames.Clear();
            loopTarget = "";
        }

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            var label = AnimationLabelRegex.Match(line);
            if (label.Success)
            {
                if (!label.Groups["loop"].Success)
                {
                    Flush();
                    current = new InteractionAnimation(Array.Empty<InteractionFrame>(), 0, label.Value);
                    currentLabel = label.Value[..^1];
                    labels[currentLabel] = (current, 0);
                }
                else
                {
                    if (current != null)
                        labels[label.Value[..^1]] = (current, frames.Count);
                }
                continue;
            }

            if (current == null)
                continue;

            if (line.StartsWith("m_AnimationLoop", StringComparison.Ordinal))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    loopTarget = parts[1];
                continue;
            }

            if (line.StartsWith(".db", StringComparison.Ordinal))
            {
                var values = HexByteRegex.Matches(line)
                    .Select(match => ParseHex(match.Groups["value"].Value))
                    .ToArray();
                // byte 2 (animParameter) is intentionally ignored for now.
                for (var i = 0; i + 2 < values.Length; i += 3)
                    frames.Add(new InteractionFrame(values[i], values[i + 1]));
            }
        }

        Flush();
    }

    private void LoadOamBlocks(string path)
    {
        var lines = File.ReadAllLines(path);
        var currentLabel = "";
        var blocks = new List<OamBlock>();
        var expected = -1;
        var pendingLabel = false;

        void Flush()
        {
            if (currentLabel.Length > 0 && expected >= 0 && blocks.Count > 0)
                _oamBlocks[currentLabel] = blocks.ToArray();
            blocks.Clear();
            expected = -1;
        }

        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            var label = LabelRegex.Match(line);
            if (label.Success)
            {
                Flush();
                currentLabel = label.Groups["label"].Value;
                pendingLabel = true;
                continue;
            }

            if (!line.StartsWith(".db", StringComparison.Ordinal))
                continue;

            var values = HexByteRegex.Matches(line)
                .Select(match => ParseHex(match.Groups["value"].Value))
                .ToArray();
            if (pendingLabel)
            {
                if (values.Length >= 1)
                    expected = values[0];
                pendingLabel = false;
                continue;
            }

            for (var i = 0; i + 3 < values.Length && blocks.Count < expected; i += 4)
                blocks.Add(new OamBlock(Signed(values[i]), Signed(values[i + 1]), values[i + 2], values[i + 3]));
        }

        Flush();
    }

    private static Rgba[][] LoadStandardPalettes(string path)
    {
        var data = AsmParser.ParsePaletteData(path);
        if (!data.TryGetValue("standardSpritePaletteData", out var colors) || colors.Length < 12)
            throw new InvalidDataException($"{path}: standardSpritePaletteData is missing or too short.");

        // Palettes 0-5 are the standard sprite set. 6/7 are room/object
        // palettes and are rejected by TryCompose until that context exists.
        var palettes = new Rgba[6][];
        for (var i = 0; i < palettes.Length; i++)
        {
            if ((i + 1) * 4 > colors.Length)
                throw new InvalidDataException($"{path}: standardSpritePaletteData has no palette {i}.");

            palettes[i] = new[]
            {
                new Rgba(0, 0, 0, 0),
                ToRgba(colors[i * 4 + 1]),
                ToRgba(colors[i * 4 + 2]),
                ToRgba(colors[i * 4 + 3]),
            };
        }

        return palettes;
    }

    private static Rgba ToRgba(Rgb555 color) => new(
        (byte)(color.R * 255 / 31),
        (byte)(color.G * 255 / 31),
        (byte)(color.B * 255 / 31),
        255);

    private static int Signed(int value) => value >= 0x80 ? value - 0x100 : value;

    private static string StripComment(string line)
    {
        var index = line.IndexOf(';');
        return (index >= 0 ? line[..index] : line).Trim();
    }

    private static int ParseHex(string value) =>
        int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static int ParseNumber(string value)
    {
        if (value.StartsWith('$'))
            value = value[1..];
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private sealed class SpriteSheet
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Indices { get; }

        public SpriteSheet(int width, int height, byte[] indices)
        {
            Width = width;
            Height = height;
            Indices = indices;
        }

        public byte this[int x, int y] => Indices[y * Width + x];

        public static SpriteSheet Vertical(SpriteSheet top, SpriteSheet bottom)
        {
            var indices = new byte[top.Width * (top.Height + bottom.Height)];
            Array.Copy(top.Indices, 0, indices, 0, top.Indices.Length);
            Array.Copy(bottom.Indices, 0, indices, top.Indices.Length, bottom.Indices.Length);
            return new SpriteSheet(top.Width, top.Height + bottom.Height, indices);
        }
    }
}
