# Roadmap

Acceptance is evidence-based: a phase is done only when its checks are recorded
in `VERIFICATION.md`.

## Phase 0 - Prerequisites

- [x] .NET 8 SDK (user-local install; `build.cmd`).
- [x] Git repository, clean initial commit.
- [x] `external/oracles-disasm` pinned at `21c924af`.
- [ ] WLA-DX (only needed for byte-exact ROM/trace checks and audio).

## Phase 1 - Base build parity

- [x] Import v2.0.6.
- [x] `net8.0-windows`; 0 errors.
- [x] Game launches; resources/GBS load.
- [x] Fix duplicate `.lng` keys.
- [x] Document multiplatform WIP.

Acceptance: LA base runs identically from source.

## Phase 2 - Asset layer (Ages)

- [x] Tilesets, metatiles, attributes, bank 0/1.
- [x] Palettes 2-7 + BG0.
- [x] Layout-label aliases.
- [x] Past cliff palette rewrite.
- [x] `checkTilesetOverride` (companion, Maku, Jabu).
- [x] Animated tiles (atlas variants + `.map` timeline).
- [x] Collisions/terrain sections.
- [x] Present overworld 196/196 rooms.
- [x] Past overworld 196/196 rooms.
- [x] Present interiors (group 2) reachable from group 0.
- [ ] Past interiors (group 3) and underwater groups.
- [ ] Full dungeon floors and large-room selection.
- [ ] `roomGfxChanges` / room-specific graphics.
- [x] Room object stream: positioned interactions and counts for the rest.

Acceptance: `overworld --group 0` and `--group 1` are deterministic; previews show
no corrupt tiles; manifests match.

## Phase 3 - Runtime traversal (Ages)

- [x] Boot into the generated overworld (`--oracle`).
- [x] Link walks; DX-HD camera follows.
- [x] Tile collision blocks water/cliffs.
- [x] Terrain flags (water/deep/lava/grass).
- [x] Tile animation runs.
- [x] `#oracle-map v1` metadata and data-driven camera policy.
- [x] Interiors as separate maps + warps (present group 2, 22 maps).
- [x] Door/tile and screen-edge warps through `MapTransitionSystem`.
- [ ] Doors, stairs, holes, overworld teleporters (native LA objects).
- [ ] Special collisions (bridges, rails, one-way).
- [ ] Swimming, diving, falling.
- [ ] Camera policies for rooms that need a locked view.

Acceptance: walk the whole present overworld, enter/exit the Maku Tree and a
house, cross a bridge, and fall into a hole without leaving the map.

## Phase 4 - Objects, enemies and inventory

- [x] Room object stream: positioned interactions + default animation.
- [ ] Enemies, parts, item drops and scripted interactions.
- [x] Maku Tree object (`INTERAC_MAKU_TREE $87`) and animated eyes.
- [ ] Enemy AI (use `ooa-godot`, verify with `oracles-decomp`).
- [ ] Items, chests, doors, switches, platforms, water, mines.
- [ ] Inventory, rings, HUD, minimap, dungeon map.
- [ ] Save/progression model.

Acceptance: complete Ages dungeon 1 (keys, map, compass, boss, essence).

## Phase 5 - Scripts, events, bosses, progression

- [ ] Script/cutscene interpreter and event hosts.
- [ ] NPCs, bosses, mid-bosses, companions, Maple, Gasha.
- [ ] Harp of Ages / Rod of Seasons, time/season transitions.
- [ ] Secrets, passwords, linked game.
- [ ] Full Ages completion.

## Phase 6 - Seasons, audio, text, polish

- [ ] Seasonal tilesets and four layouts.
- [ ] Subrosia (11x8).
- [ ] Seasons runtime and content.
- [ ] Oracle `.gbs` and SFX.
- [ ] Text conversion and dialogue codes.
- [ ] Replace LA HUD/menus/save; remove legacy data.
- [ ] Regression suite and release packaging.

## Immediate next tasks

1. Import `roomGfxChanges`; remove the black Maku canopy/lower band and restore
   other room-specific graphics.
2. Extend the object stream to enemies, parts and item drops; add their OAM
   presentation and simple movement.
3. Generate past interiors (group 3) and wire their warps.
4. Align door triggers with the native walkable strip; reduce the facing-tile
   approximation after the body collision is compared with the GB tile check.
5. Parse `dungeonLayouts.s`; generate one map per dungeon floor.
6. Replace the LA HUD with the Oracle status bar.
