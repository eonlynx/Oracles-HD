# Content coverage

Source-file counts are an inventory, not a claim that each ID is implemented.
They come from `oracles-disasm/object_code/{ages,seasons,common}`. Update this
ledger with each content milestone; keep per-ID ledgers beside the importer when
a system is implemented.

## Ages

| Area | Source inventory | Implemented | Notes |
|---|---|---|---|
| Overworld rooms (present) | 196 | 196 generated | Group 0, 140x112, collisions/terrain/animations. |
| Overworld rooms (past) | 196 | 196 generated | Group 1, 140x112, past cliffs (`$40..$7f`, room `$38` excluded), collisions/terrain/animations; group-specific room objects. Past `1:$38` keeps teal Maku water. |
| Present interiors / underwater (group 2) | 256 room slots | 22 maps | Reachable from group-0 warps; 10x8; group 3 not generated. |
| Dungeon rooms | groups 4/5 | 5 sample rooms | No floor layouts; 10x8 / 15x11 storage. |
| Enemies / bosses (files) | 37 Ages + 71 common = 108 | 0 | Bosses are included in these files. |
| Interactions / NPCs (files) | 152 Ages + 63 common = 215 | source-derived, 150+ placements | Positioned interactions with default animation; collision/flags/palette/entry direction parsed from each interaction's code and emitted as `interaction_behaviors.tsv` + manifest. Maku `$87` renders/blinks. Scripts and facing pending. |
| Room-entry effects | `roomSpecificCode.s` | 4 group-0 + 4 group-1 rooms | `OracleRoomRuntime` dispatches supported handlers; script bindings + prospective-edge presentation cover Ralph/EnterPast. Music/minimap/respawn owners pending. |
| Warps / doors | `warpSources.s`, `warpDestinations.s`, `warpTiles.s` | 13 entry + 11 exit | Group-0 tile/pointer/edge sources; present interiors only. |
| Time portals | `$e1`, `$de`, `timeportalSpawner.s` | 14 spawners + dynamic return | Present <-> past at the same world position through `MapTransitionSystem`. |
| Items / treasure behaviours (files) | 3 Ages + 22 common = 25 | 0 | Treasure IDs are `$63` entries in `treasureObjectData` (ROM_DATA_FORMATS section 5.4). |
| Parts / effects (files) | 41 Ages + 45 common = 86 | 0 | Particles, projectiles, helpers. |
| Special objects (files) | 3 Ages + 9 common = 12 | 0 | Link, companions, Maple, ... |
| Rings | source item/ring tables | 0 | Appraisal, box, effects. |
| Scripts / cutscenes | `object_code` + `mainScripts` | 0 | Command runner not implemented. |
| Text | `text/ages/text.yaml` | 0 | LA `.lng` still used. |
| Audio | `audio/ages/`, `code/audio.s` | 0 Oracle tracks | LA `awakening.gbs` still used. |
| Save / progression | SRAM `0x550` x 3 files | 0 | LA save layout still used. |

### Present interaction pass (37 ids)

The first pass composes every positioned interaction reachable from the present
overworld and the 22 present interiors. IDs are listed for traceability; names
come from `objectGfxHeaders.s`.

```
2e 36 37 39 3a 3b 3c 3d 3f 41 44 46 47 48 49 4c 53 54 59 5c 5d
65 66 6b 72 80 83 87 89 9a b6 c2 c8 cd d5 e1 e5
```

`$34` and some `$71` variants are skipped for now because their OAM selects
sprite palette 6/7 and the room/object sprite palette is not imported yet; this
is a palette-availability gate, not an id rule. `$6b:$15` (post-game Link
statue) is imported with its `PALH_c7` sprite palette 6 and is gated by
`GLOBALFLAG_FINISHEDGAME`; it appears only when that flag is set.

- `87` is the Maku Tree face (6 OAM frames, animated).
- `53`/`54` are the Mamamu dog and owner in `2:e7`.
- `5c` is the past lady/generic guy; `89` is a flame; `e5` is the shared
  Maku flower/Book of Seals/Vineseed sheet.
- `2e`, `3c`, `3d`, `c2`, `c8`, `cd`, `d5` are story/cutscene actors or props
  that happen to be positioned; they render with their default animation.
- `e1` is the time-portal spawner; its source `interactionBeginTimewarp` makes
  it an `oracleTimePortal`.
- Facing/script animations, enemies, parts and item drops are not in this pass.

## Seasons

| Area | Source inventory | Implemented | Notes |
|---|---|---|---|
| Overworld rooms | 16x16 grid | 0 | Seasonal layouts not modelled. |
| Subrosia | group 1, 11x8 | 0 | Different grid/tilesets. |
| Enemies / bosses (files) | 32 Seasons + 71 common = 103 | 0 | |
| Interactions / NPCs (files) | 130 Seasons + 63 common = 193 | 0 | |
| Items / treasure behaviours (files) | 2 Seasons + 22 common = 24 | 0 | |
| Parts / effects (files) | 31 Seasons + 45 common = 76 | 0 | |
| Special objects (files) | 3 Seasons + 9 common = 12 | 0 | |
| Seasonal tilesets (`m_SeasonalTileset`) | 4 per tileset | 0 | Needs `wRoomStateModifier`. |
| Text / audio / save | as Ages | 0 | |

## Update rules

1. Counts come from the disassembly or the importer, not from memory.
2. A row is **Implemented** only when the source path is reachable from the
   game and the check in `VERIFICATION.md` passes.
3. Record partial systems explicitly (e.g., "5 sample rooms"), not as a
   percentage of a file count.
4. When an NPC/interaction/cutscene system is implemented, create per-ID
   ledgers modeled on `ooa-godot/docs/{ages,seasons}/*-coverage.md`.
5. Keep the tables short; source-specific constants belong beside the importer.
