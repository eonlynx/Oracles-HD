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
  to 0 for metatiles `$40..$7f`, collision mode 0, room != `$38`.

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
  stitched overworlds, houses/caves of 160x128 and joined dungeon floors.
  `Map.CameraTarget` is only set by gameplay that explicitly requires a fixed
  view; do not derive a fixed camera from map size. This matches the DX-HD code
  path (`ObjDoor` transitions leave `centerCamera` false and houses have no
  camera object).
- **Why:** string map names and pixel positions cannot identify era/room/floor;
  enemies, scripts, save/respawn and dungeon logic need this metadata.
- **Consequence:** the importer owns the conversion; runtime code does not
  derive group/era from file names. `CameraTarget` remains a gameplay override,
  not a second camera system.

## ADR-031 - Semantic object identity and flag-gated spawn

- **Status:** accepted.
- **Decision:** every `oracleSprite` carries its native `interactionId/subid`,
  its optional `requiresFlag`/`forbiddenFlag`, and the native room condition
  masks. `OracleFlagState` evaluates them at map load.
- **Evidence:** the post-game Link statue `$6b:$15` is deleted when
  `GLOBALFLAG_FINISHEDGAME` is clear (`oracles-decomp`
  `miscellaneous1.c:interaction6b_subid15`); it loads `PALH_c7`, which defines
  sprite palette 6. `obj_Condition` and the before/after event pointer gates
  follow `ooa-godot` `NativeObjectStreamReader`.
- **Consequence:** the statue is absent by default and appears with
  `--flag FINISHEDGAME` with its real palette. The eventual save owner only
  needs to populate `OracleFlagState`; presentation objects keep their semantic
  id for future NPC/enemy behaviour.

## ADR-032 - Oracle NPC collision radius

- **Status:** accepted; superseded by ADR-034 for the source of the values.
- **Decision:** `oracleSprite` carries `solidTile` and explicit `radiusY/radiusX`
  values. They come from the interaction's own code:
  `objectMarkSolidPosition`/`wRoomCollisions` -> 16x16 tile box,
  `objectSetCollideRadius/Radii` -> explicit box, otherwise no collision. The box
  is centred on the interaction anchor and clamped to `EntitySize`.
- **Why:** the first presentation-only pass let Link walk through the dog; a
  later blanket 12x12 default then let Link walk through tall actors' feet and
  crashed on 8px sprites. The native rule is per-object, not a global default.
- **Consequence:** NPCs that mark a solid position or set radii block Link;
  effects and script actors that set nothing are walk-through, matching the ROM.
  DX-HD's `BoxCollisionComponent` assertion stays satisfied because the box is
  clamped to the frame.

## ADR-033 - Object-gfx continuation sheets

- **Status:** accepted; supersedes the earlier `$87`-only rule.
- **Decision:** `InteractionSprites` computes the maximum OAM cell referenced by
  **all** animations of an interaction. If it exceeds the base sheet, the
  importer walks consecutive `objectGfxHeaders.s` entries until the stop bit and
  appends each 128x16 sheet, matching `interactionLoadExtraGraphics` +
  `loadGraphics.s:@nextExtraGfxIndex`. No interaction id is involved.
- **Why:** `$87` OAM frames use tiles `$20-$2e` (mouths, eyebrows, cheeks) that
  live in the continuation header `$05`; composing only `$04` dropped those
  blocks. The same rule covers any future interaction with a continuation
  header. The Maku Tree's canopy/lower band are static tileset $22 graphics
  (header `$70`), not `treeGfxHeaderTable` (`TREE_GFXH_07..0a` are the
  scent/pegasus/gale/mystery trees).
- **Consequence:** extra-graphics residency is data-driven from OAM and the
  header stop bit, not an id table.

## ADR-034 - Source-derived interaction behaviour

- **Status:** accepted.
- **Decision:** `InteractionBehaviors` parses each interaction's own code block
  (and its subid jump table) for the native helpers that define behaviour:
  `objectMarkSolidPosition`/`wRoomCollisions` (solid tile),
  `objectSetCollideRadius/Radii` (explicit box), `checkGlobalFlag` +
  `interactionDelete` (`jp z` requires, `jp nz` forbids), `PALH_x` +
  `loadPaletteHeader` (sprite palette), `interactionBeginTimewarp` (portal), and
  `wScreenTransitionDirection` + `cp` + delete (one-shot entry actor).
  `OracleAssetBuilder` emits those values per object; `InGame/` consumes them.
- **Why:** the previous per-id table (`InteractionRules`) was a hardcode: it
  gave every ordinary NPC a 12x12 box even when the source sets no collision,
  gated `$6b:$15` by a literal id, and forced `$8f` on room `$38`. ooa-godot's
  importers parse the same helpers per feature (`Import-MakuTreeData`,
  `Import-MakuRescueData`, `Import-ImpaCutsceneData`); this generalises that
  approach instead of copying one-off constants.
- **Consequence:** collision, flags, palettes, portals and one-shot entry actors
  work for every interaction that uses the native helper. Values that come from
  unparsed scripts (e.g. the past villager's `$41` removal, per-NPC cutscene
  radii) remain explicit gaps until the script runner exists. Room `$38` now
  uses its real tileset palette `$30`, restoring the pink flowers; `$8f` is a
  cutscene load, not a static override.

## ADR-035 - Keep DX-HD runtime ownership; add Oracle state/room seams

- **Status:** accepted.
- **Decision:** keep DX-HD `GameObject`/`ObjectManager`/`MapTransitionSystem` as
  the runtime. Before scripts, add only: an instance `OracleSessionState`
  (global flags, per-group/room flags, room state, entry direction, save hooks),
  an `OracleRoomRuntime` with room-load/entry events and a small room-entry
  effect table, and an `OracleGameProfile` for game-specific flags, portals,
  groups and map names. Do not port `ooa-godot`'s `RoomEntityManager`/ECS.
- **Why:** the structural audit (AUDIT section 7) found that the static
  `OracleFlagState` and the lack of a room lifecycle block scripts, dungeons and
  Seasons, while the importer and DX-HD object model are adequate. `ooa-godot`'s
  `RoomEntryEffectDatabase` and command-table/runner shape are the parts worth
  adapting; its full room/entity manager is unnecessary for this engine.
- **Consequence:** the next block can implement scripts and room-entry events
  without a big-bang rewrite. Typed generated tables for behaviours, scripts and
  enemy placements replace further ad-hoc parsing; `roomGfxChanges` can proceed
  independently.

## ADR-036 - Typed interaction behaviour table

- **Status:** accepted.
- **Decision:** `InteractionBehaviors` emits a sorted TSV plus manifest
  (`oracle_<game>_interaction_behaviors.*`) containing solid tile/radii, required
  or forbidden global flag, sprite palette header, time-warp and entry-direction
  facts. Portal activation, game flag tables and actor events stay in
  `OracleGameProfile`/`OracleRoomRuntime`; the table is facts, not policy.
- **Why:** the generic parser replaced the per-id `InteractionRules` table, but
  its output was only in memory. A typed, manifest-backed artifact makes the
  behaviour reviewable, deterministic and reusable by the script/enemy importers
  without adding regex to each builder. `.ifdef ROM_AGES/ROM_SEASONS` filtering
  keeps Ages values out of Seasons.
- **Consequence:** the runtime keeps consuming the per-object `.map` fields; the
  table is the tooling/audit contract. New facts must be added to the parser and
  the table together.

## ADR-037 - Room presentation is separate from script activation

- **Status:** accepted.
- **Context:** native one-shot actors such as Ralph `$37:$0d` are created only
  when `wScreenTransitionDirection` matches the edge in `ralph.s:@initSubid0d`.
  A screen-at-a-time engine hides the room until the scroll and therefore never
  shows the actor before it exists. Our overworld is stitched and the camera can
  already see the next room; hiding the actor until Link crosses its boundary
  makes him pop into view.
- **Decision:** split the two questions.
  1. `OracleRoomRuntime` tracks the current stitched-map room every frame.
  2. `OracleScriptRuntime.UpdatePresentation` presents every entry-gated actor
     as the native initializer would for the edge Link would use from the
     current room, including actors in visible neighbouring rooms. The
     prospective edge is the room-grid direction (left/right/up/down), not the
     crossing frame.
  3. Scripts start only on the real room entry, and only when the actual entry
     direction matches. Presentation never runs a script early.
  4. A one-shot guard at the script root
     (`jumpifglobalflagset FLAG, stubScript`) is emitted as `completed_flag`;
     once set, presentation and activation are suppressed immediately.
  5. An active script owns its actor's visibility (flicker, hide, delete). When
     it ends, the binding's `delete_on_end` or the presentation pass takes over.
- **Why:** this is the same contract `top` implements with its neighbour ghost:
  the neighbour is captured in the state the game creates for the edge the
  player would cross, so it is already composed before the scroll. `ooa-godot`
  enforces the same invariant for screen transitions: an incoming entity must
  resolve its presentation during preload, and hidden entities must say so
  explicitly; otherwise it fails instead of allowing a pop after the scroll.
- **Consequence:** approaching `$0:$39` from the left shows Ralph in the next
  screen before the boundary; crossing starts `ralphSubid0dScript` with no
  visual change. Approaching from another edge leaves him absent both before
  and after the crossing, as the native initializer would. Future gate facts
  (global flags, room state, subid conditions) must feed presentation, and
  animated neighbours should be posed/looped rather than frozen. Enemies and
  parts will use the same presentation/activation split.
