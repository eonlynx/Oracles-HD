# Architecture decision records

Each record keeps the context, the decision, the evidence and the consequence.
When a record is superseded, add a new one instead of editing history.

## ADR-001 - Base engine: Links-Awakening-DX-HD v2.0.6

- **Status:** accepted.
- **Decision:** fork v2.0.6 (`2e68849`) and keep the engine, renderer, camera,
  entities, overlays, save manager, UI base and GBS player.
- **Target:** `net8.0-windows`, MonoGame 3.8.1.303 WindowsDX.
- **Consequence:** Windows-only today. v2.0.6 has no Linux/macOS/KNI/Vulkan/DX12
  sources; the official `.exe` references them but the tree does not. See
  `PLATFORM.md`.
- **Multiplatform:** deferred at the project owner's request until those sources
  are provided. Do not invent KNI/Vulkan/DX12 dependencies or break the verified
  WindowsDX build.

## ADR-002 - oracles-disasm is a pinned, offline-only input

- **Status:** accepted.
- **Decision:** `external/oracles-disasm` is a submodule pinned at `21c924af`.
  `Tools/OracleAssetBuilder` reads it offline; the runtime never does.
- **Why:** determinism, performance, no ROM at runtime, no parser in the game.
- **Consequence:** generated assets are reproducible; the importer is the only
  component that knows the disassembly.

## ADR-003 - Generated assets are local; manifests are committed

- **Status:** accepted.
- **Decision:** `Data/Maps/oracle_*`, tilesets, previews and other generated data
  are gitignored. `*.manifest.json` files with input/output SHA-256 are committed.
- **Why:** keep the repository free of derived game data while proving what was
  generated from which commit.

## ADR-004 - No `.ani` and no extra data files for animations

- **Status:** accepted (user constraint).
- **Decision:** animation frames are baked into the **existing atlas PNG** and
  the timeline is embedded in the **existing `.map`** under `#oracle-anim v1`.
  No `.ani`, no per-animation PNG, no sidecar file.
- **Consequence:** the engine gains `TileAnimation` and
  `SaveLoadMap.LoadOracleSections`; the DX-HD loader ignores the trailing lines.

## ADR-005 - `animationData` pair order is `(delay, gfxIndex)`

- **Status:** accepted.
- **Evidence:** `oracles-disasm/tools/dump/dumpAnimationData.py`:
  `counter = rom[address]`, `gfxIndex = rom[address + 1]`, then
  `.db counter gfxIndex`. `ooa-godot` uses the same order and its regression
  pins waterfall headers `0,24,32` (the second bytes of `.db $03 $00`,
  `.db $01 $18`, `.db $04 $20`). The water/flower track then uses headers
  `4,5,6,7` (distinct frames), not a repeated header.
- **Supersedes:** the earlier reading of the comment `e = animation gfx index`
  and of the decomp register names; the data/dumper and visible frames win.

## ADR-006 - PNG palette index is the CGB shade; do not invert

- **Status:** accepted.
- **Evidence:** the disassembly PNG PLTE is `[255,170,85,0]`; index 0 = white =
  CGB shade 0. `ooa-godot` reads RGBA and computes `(255 - red + 42)/85`, which
  returns the same index.
- **Consequence:** `TilesetRenderer` uses the raw index. An earlier `3 - raw`
  produced inverted water/grass.

## ADR-007 - BG palette 0 is the common engine palette

- **Status:** accepted.
- **Decision:** initialize palette 0 from `paletteData48e0` before applying the
  tileset palette (slots 2-7).
- **Evidence:** `ooa-godot/BackgroundPaletteState` and `HudPalette`;
  `paletteData48e0` is `PALH_0f`'s BG0.

## ADR-008 - Maku Tree is part of the overworld grid

- **Status:** accepted.
- **Decision:** include a room when its tileset has `TILESETFLAG_OUTDOORS ($01)`
  **or** `TILESETFLAG_MAKU ($02)`.
- **Evidence:** room `$38` has flags `$02`; `ooa-godot/MapDataDatabase` maps MAKU
  to room `$38`; `oracles-decomp/wide/room.c` maps `(flags & 2)` to `$38`.
- **Consequence:** 196/196 Ages rooms in groups 0 and 1; the black cell is gone.

## ADR-009 - Tile collisions and terrain live in the `.map`

- **Status:** accepted.
- **Decision:** generate `#oracle-collision` and `#oracle-terrain` sections and
  use `Map.IsSolid` in `SystemBody.Collision`; do not emit thousands of
  collider objects.
- **Why:** Oracle collision is tile-based; a per-tile layer is compact,
  deterministic and supports future dynamic tile changes.
- **Limits:** special collisions (`>=0x10`, bridges/rails) are not exact and are
  treated as free. Water/hole/lava are forced full-solid until swimming and
  falling exist; this is deliberately conservative, not source parity.

## ADR-010 - Source hierarchy

- **Status:** accepted.
- **Decision:** executed game/trace > `oracles-disasm`/`oracles-decomp`/`top` on
  contradictions > `ooa-godot` as the default accelerator > assumptions.
- **Consequence:** `ooa-godot` speeds up enemies/bosses/scripts/inventory/UI;
  its data interpretation must yield to the disassembly when they disagree.

## ADR-011 - Legacy Link's Awakening cleanup is incremental

- **Status:** accepted (user policy).
- **Decision:** remove LA-specific code/data when its Oracle replacement exists
  and is verified; keep only the generic engine. Never leave commented-out
  legacy blocks. See `CLEANUP.md`.
- **Consequence:** the LA fallback boot/menu remains until the Oracle boot/UI
  replaces it; removing it earlier would break the only boots.

## ADR-012 - `--oracle` is a development entry, not a feature

- **Status:** accepted.
- **Decision:** boot directly into the generated Ages overworld with a CLI flag.
- **Why:** no new menu option; feature parity with DX-HD is preserved.
- **Consequence:** the normal LA intro/menu still runs without the flag.

## ADR-013 - Base `.lng` duplicate-key crash fix

- **Status:** accepted.
- **Context:** v2.0.6 data repeats keys (`choice_*` in `{lang}.lng` and
  `dialog_{lang}.lng`; `settings_controls_onscreen` twice in `swe.lng`) and uses
  single-slash comments in `dialog_chn.lng`. `Dictionary.Add` crashed on launch.
- **Decision:** deterministic file order (UI first, dialog last), skip any line
  starting with `/`, and keep the last definition.
- **Consequence:** the game starts; unique keys are unchanged.

## ADR-014 - Project language is English

- **Status:** accepted (project owner).
- **Decision:** source comments, documentation, commit messages and diagnostics
  are English. Conversation with the owner may be Spanish.

## ADR-015 - No external image library in the importer

- **Status:** accepted.
- **Decision:** implement the 2bpp indexed PNG reader and RGBA writer in
  `Tools/OracleAssetBuilder/Png.cs`.
- **Why:** the disassembly PNGs are exactly bit-depth 2 / palette-indexed; a
  custom codec avoids ImageSharp licensing/audit noise and keeps the tool
  dependency-free and deterministic.
- **Consequence:** the tool supports only the shapes it needs (2bpp palette in,
  RGBA8 out).

## ADR-016 - Keep the DX-HD project structure and namespaces

- **Status:** accepted.
- **Decision:** rename the project/solution/assembly to Oracles-HD, keep the
  `ProjectZ` namespaces, folder layout and startup object.
- **Why:** minimize the diff against the verified base; namespaces are internal.

## ADR-017 - Keep the engine's 4 save slots

- **Status:** accepted (project owner).
- **Decision:** the DX-HD 4-slot menu remains. Oracle files are 3 per game; the
  fourth slot is engine capacity, not a new menu feature.
- **Consequence:** the Oracle save menu will map/limit slots when it replaces the
  LA menu; do not add a new slot UI.

## ADR-018 - Reference reuse is authorized for this personal project

- **Status:** accepted (project owner, 2026-11-25).
- **Decision:** the owner authorizes adapting/porting/reusing code from
  `ooa-godot`, `oracles-decomp`, `top` and `epoch-equinox` for this personal,
  non-commercial project.
- **Distribution caveat:** if distributed, re-evaluate: `oracles-decomp` is
  GPLv3 (copied code forces the combined work to GPLv3), `top`/`epoch-equinox`
  are MIT (keep notices), and the no-licence repositories (`ooa-godot`,
  `oracles-disasm`, the DX-HD fork) need permission. See `THIRD_PARTY.md`.

## ADR-019 - Ages first, Seasons prepared but not claimed

- **Status:** accepted (project owner).
- **Decision:** implement and validate Oracle of Ages end-to-end first; keep the
  importer/data model game-agnostic where cheap.
- **Consequence:** Seasons requires seasonal tilesets (`m_SeasonalTileset`), four
  layouts per room and Subrosia; do not report Seasons as playable before those
  checks pass.

## ADR-020 - Feature parity with DX-HD; Oracle systems are content

- **Status:** accepted (project owner, non-negotiable).
- **Decision:** the engine, camera, renderer, audio, UI base and architecture are
  DX-HD's. Do not add filters, shaders, extra menu entries or engine features.
  Rings, secrets, the Harp, the Rod, etc. are required Oracle content and are
  implemented with existing engine mechanisms.

## ADR-021 - World composition

- **Status:** accepted.
- **Decision:** stitch OUTDOORS (and MAKU) rooms into one map per world: Ages
  present group 0 and past group 1; Seasons 16x16 and Subrosia 11x8 later.
  Interiors are separate maps entered by warps. Large rooms (15x11 playable) are
  separate maps or explicit regions when they do not fit the 10x8 grid.
- **Consequence:** no black cells; interiors stay opt-in; large rooms are a
  Phase 3/4 item.

## ADR-022 - Camera policy

- **Status:** accepted.
- **Decision:** keep the DX-HD follow camera. If a room needs a locked view, use
  the existing `Map.CameraTarget`; do not add a second camera system.
- **Risk:** puzzles that assume a fixed 160x128 view must be tested when their
  rooms are implemented.

## ADR-023 - Save-dependent tileset overrides

- **Status:** accepted.
- **Decision:** expose `checkTilesetOverride` as importer flags
  (`--companion`, `--maku-saved`, `--jabu-underwater`) with no override by
  default. The companion branch is modeled as a layout-group change (Ricky 0,
  Dimitri 1, Moosh/other 3), matching `ooa-godot`. Past cliffs rewrite palette 6
  to 0 for metatiles `$40..$ff`, collision mode 0, room != `$38`.

## ADR-024 - Defer WLA-DX; validate animations visually

- **Status:** accepted (project owner).
- **Decision:** do not install/build WLA-DX yet. Validate the animation frame
  sequence by rendering the atlas and comparing runtime frames. Add WLA-DX only
  for byte-exact ROM checks and the Oracle `.gbs`.
- **Consequence:** current parity evidence is visual/structural, not a bit-exact
  trace.

## ADR-025 - Validation strategy

- **Status:** accepted.
- **Decision:** use deterministic importer hashes, build/run checks, preview
  inspection, screenshot diffs, movement/collision probes and manifest hashes.
  Do not add a separate validation assembly until content systems justify it.
- **Consequence:** checks are recorded in `VERIFICATION.md`; a suite pass is not
  claimed as universal parity.

## ADR-026 - Documentation policy

- **Status:** accepted (project owner).
- **Decision:** `docs/` records durable contracts (status, architecture, formats,
  decisions, coverage, validation, cleanup). Update the affected document in the
  same commit as a milestone; commit at each phase. No implementation diaries.

## ADR-027 - Save, content and audio contracts live in architecture until implemented

- **Status:** accepted.
- **Decision:** do not maintain separate speculative documents for systems that
  do not exist yet. The planned save/state, object/script and audio contracts are
  summarized in `ARCHITECTURE.md` and `ROADMAP.md`; they get their own document
  when implementation starts.
- **Why:** avoid duplicated plans and stale docs.

## ADR-028 - Prefer high-level reference implementations before raw disassembly

- **Status:** accepted (project owner, 2026-11-25).
- **Decision:** before writing a low-level parser or entity behaviour from
  `oracles-disasm`, inspect and adapt the existing implementations:
  `ooa-godot` first (`tools/import_oracles` and runtime C#), `top` and
  `oracles-decomp` for data readers, C specifications and edge cases, and
  `oracles-disasm` as the raw data source and final arbiter.
- **Why:** re-deriving `warpSources`, object streams, interaction presentation or
  OAM composition from `.s` duplicates working code and adds avoidable risk.
  `ooa-godot` already encodes the control flow, ordering and validation for these
  formats.
- **Consequence:** adapted algorithms keep their source reference in comments and
  manifests. The runtime still uses DX-HD's objects, transitions, atlas and map
  format; no `ooa-godot` architecture or Godot dependency is imported.
- **Applied to:** warp lookup (`Warps.cs`), object-stream reading
  (`RoomObjects.cs`), interaction presentation and OAM composition
  (`InteractionSprites.cs`). Existing tile/tileset, collision and background
  animation code already matches the high-level importers and was not rewritten.

## ADR-029 - Oracle object anchor, OAM origin and warp trigger

- **Status:** accepted.
- **Decision:** treat the native interaction position as the centre of a 16x16
  object cell (`ooa-godot` `SpriteBounds`). The generated entity atlas stores
  `rawOamMinX - 8, rawOamMinY - 16` as the draw origin. Do not apply the GB
  engine's `+$10` screen Y again: it is the status-bar offset, and DX-HD map
  coordinates already start at the playfield.
- **Evidence:** `getObjectPositionOnScreen` (`Object.yh - cameraY + $10`),
  `ooa-godot` `NpcCharacter.SpriteBounds`/`BuildPositionedOamTexture`, and the
  Maku Tree room `$38` result (eyes on the upper trunk, mouth below).
- **Palette:** room `0:$38` uses `PALH_8f` (BG 2-7 from `paletteData4500`), not
  the tileset `$30`; `makuTree.s` loads it. This fixes the right-side
  sign/flower colours.
- **Warp trigger:** the runtime checks the tile Link stands on **and** the tile
  he faces. DX-HD's 8x10 body collision is stricter than the GB tile check, and
  a door metatile is commonly only half-walkable; without the facing check Link
  stops one pixel short of the warp tile.
- **Atlas:** one shared `oracle_<game>_entities.png/.atlas` for the overworld
  and interior commands, built from a stable plan. The timeline is embedded in
  the `oracleSprite` map object; no `.ani`.
- **Limit:** this is a first pass for positioned interactions with their default
  animation. Enemies, parts, item drops, scripted animation/facing and
  `roomGfxChanges` remain separate work.

## ADR-030 - Map metadata and camera policy

- **Status:** accepted (project owner).
- **Decision:** every Oracle `.map` starts its Oracle sections with
  `#oracle-map v1`: game, group, era, kind, room size, grid, room, floor,
  dungeon and camera. The runtime reads it into `Map.OracleInfo`.
- **Camera:** keep DX-HD's smooth follow camera everywhere it works, including
  stitched overworlds and joined dungeon floors. Use `Map.CameraTarget` only for
  isolated one-screen rooms (160x128) or a puzzle that explicitly requires a
  fixed view. This preserves the DX-HD feel while giving Oracle room identity.
- **Why:** string map names and pixel positions cannot identify era/room/floor;
  enemies, scripts, save/respawn and dungeon logic need this metadata.
- **Consequence:** the importer owns the conversion; runtime code does not
  derive group/era from file names. The fixed camera is data-driven, not a
  second camera system.
