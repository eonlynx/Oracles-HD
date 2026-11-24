# Content coverage

Source-file counts are an inventory, not a claim that each ID is implemented.
They come from `oracles-disasm/object_code/{ages,seasons,common}`. Update this
ledger with each content milestone; keep per-ID ledgers beside the importer when
a system is implemented.

## Ages

| Area | Source inventory | Implemented | Notes |
|---|---|---|---|
| Overworld rooms (present) | 196 | 196 generated | Group 0, 140x112, collisions/terrain/animations. |
| Overworld rooms (past) | 196 | 196 generated | Group 1. |
| Underwater rooms | groups 2/3 | 0 | Not generated. |
| Dungeon rooms | groups 4/5 | 5 sample rooms | No floor layouts; 10x8 / 15x11 storage. |
| Enemies / bosses (files) | 37 Ages + 71 common = 108 | 0 | Bosses are included in these files. |
| Interactions / NPCs (files) | 152 Ages + 63 common = 215 | 0 | Includes `makuTree.s`, `makuFlower.s`, shops, story actors. |
| Items / treasure behaviours (files) | 3 Ages + 22 common = 25 | 0 | Treasure IDs are `$63` entries in `treasureObjectData` (ROM_DATA_FORMATS section 5.4). |
| Parts / effects (files) | 41 Ages + 45 common = 86 | 0 | Particles, projectiles, helpers. |
| Special objects (files) | 3 Ages + 9 common = 12 | 0 | Link, companions, Maple, ... |
| Rings | source item/ring tables | 0 | Appraisal, box, effects. |
| Scripts / cutscenes | `object_code` + `mainScripts` | 0 | Command runner not implemented. |
| Text | `text/ages/text.yaml` | 0 | LA `.lng` still used. |
| Audio | `audio/ages/`, `code/audio.s` | 0 Oracle tracks | LA `awakening.gbs` still used. |
| Save / progression | SRAM `0x550` x 3 files | 0 | LA save layout still used. |

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
