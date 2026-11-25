# Oracle enemy mapping (Ages/Seasons -> DX-HD)

Snapshot: 2026-11-25. Sources are pinned in
`external/oracles-disasm` (`enemyData.s`, `extraEnemyData.s`, `partData.s`,
`object_code/common/enemies`, `object_code/common/parts`) and the DX-HD
classes in `InGame/GameObjects/Enemies`.

This is the first classified matrix. It exists so new enemies are selected by
source identity and native behaviour, not by room-specific code. The runtime
only instantiates a native host when the species has:

1. a verified Oracle stat row (`EnemyStats`),
2. composed Oracle OAM frames (all required cardinal poses),
3. a state machine translated from `oracles-disasm`,
4. native body/hittable/damage/pushable/shadow components.

Everything else stays as presentation-only `oracleSprite` until its row is
promoted.

## 1. Status legend

| Status | Meaning |
|---|---|
| **Hosted** | Native runtime host with imported stats, frames and behaviour. |
| **Direct** | DX-HD class implements the same species/state machine; reuse with Oracle stats/sprites. |
| **Adapt** | DX-HD has a related class, but Oracle subids/states/projectiles differ. |
| **Oracle-only** | No usable DX-HD class; translate from `oracles-disasm`. |
| **Boss/script** | Boss, cutscene or non-enemy producer; not part of the enemy host pipeline. |
| **Stub/unused** | No live enemy code in the source tables. |

## 2. Oracle common enemy IDs

The table lists every `ENEMY_*` id defined in
`constants/common/enemies.s`. Seasons-only variants must be checked per build
(`.ifdef ROM_SEASONS`) before promotion.

| ID | Oracle symbol | DX-HD candidate | Status | Notes |
|---|---|---|---|---|
| 00 | STUB_00 | - | Stub/unused | |
| 01 | MERGED_TWINROVA | - | Boss/script | |
| 03 | TWINROVA | - | Boss/script | |
| 04 | GANON | - | Boss/script | |
| 08 | RIVER_ZORA | EnemyRiverZora | Direct | Projectile timing to verify. |
| 09 | OCTOROK | EnemyOctorok | **Hosted** | `$09:00..02`, PART18 projectile. |
| 0a | BOOMERANG_MOBLIN | EnemyMoblin | Adapt | Boomerang projectile/return states differ. |
| 0b | LEEVER | EnemyLeever | Direct | |
| 0c | ARROW_MOBLIN | EnemyMoblin | Adapt | Arrow projectile states. |
| 0d | LYNEL | - | Oracle-only | |
| 0e | BLADE_TRAP | EnemyBladeTrap | Direct | |
| 10 | ROPE | EnemyRope | Direct | |
| 12 | GIBDO | EnemyGibdo | Direct | |
| 13 | SPARK | EnemySpark | Direct | |
| 14 | SPIKED_BEETLE | EnemySpikedBeetle | Direct | |
| 15 | BUBBLE | EnemyGiantBubble | Adapt | Oracle bubble is a small flying skull. |
| 16 | BEAMOS | EnemyBeamos | Direct | Beam/projectile wiring must use Oracle part data. |
| 17 | GHINI | EnemyGhini | Direct | |
| 18 | BUZZBLOB | EnemyBuzzBlob | Direct | |
| 19 | WHISP | - | Oracle-only | Flame/orb behaviour. |
| 1a | SAND_CRAB | EnemyCrab | Direct | |
| 1b | SPINY_BEETLE | EnemySpinyBeetle | Direct | |
| 1c | IRON_MASK | EnemyIronMask | Direct | |
| 1d | ARMOS | EnemyArmos | Direct | |
| 1e | PIRANHA | EnemyPiranhaPlant | Direct | |
| 20 | MASKED_MOBLIN | EnemyMoblinPig | Adapt | Mask/shield states. |
| 21 | ARROW_DARKNUT | EnemyDarknutSpear | Adapt | Arrow vs spear. |
| 22 | ARROW_SHROUDED_STALFOS | EnemyShroudedStalfos | Adapt | |
| 23 | POLS_VOICE | EnemyPolsVoice | Direct | |
| 24 | LIKE_LIKE | EnemyLikeLike | Direct | |
| 25 | GOPONGA_FLOWER | EnemyGopongaFlower | Direct | |
| 27 | DEKU_SCRUB | - | Oracle-only | Seed projectile and bush reveal. |
| 28 | WALLMASTER | - | Oracle-only | Ceiling drop/grab. |
| 29 | PODOBOO | EnemyPodoboo | Direct | |
| 2a | GIANT_BLADE_TRAP | EnemyBladeTrap | Adapt | Scale/speed only. |
| 2c | CHEEP_CHEEP | EnemyCheepCheep | Direct | |
| 2d | PODOBOO_TOWER | EnemyPodoboo | Adapt | Spawner/tower variant. |
| 2e | THWIMP | EnemyThwimp | Direct | |
| 2f | THWOMP | EnemyMegaThwomp | Adapt | Oracle smash timing. |
| 30 | TEKTITE | EnemyTektite | Direct | |
| 31 | STALFOS | EnemyStalfosGreen/Orange | Direct | |
| 32 | KEESE | EnemyKeese | Direct | |
| 33 | BABY_CUCCO | - | Oracle-only | Cucco family/scripted. |
| 34 | ZOL | EnemyGreenZol/RedZol | Direct | Split into Gel. |
| 35 | FLOORMASTER | EnemyFloorLayer | Adapt | Floor emergence. |
| 36 | CUCCO | - | Oracle-only | |
| 37 | BUTTERFLY | - | Oracle-only | |
| 38 | GREAT_FAIRY | - | Boss/script | |
| 39 | FIRE_KEESE | EnemyKeese | Adapt | Fire palette/attack. |
| 3a | WATER_TEKTITE | EnemyWaterTektite | Direct | |
| 3b | GIANT_CUCCO | - | Oracle-only | |
| 3d | SWORD_MOBLIN | EnemyMoblinSword | Direct | |
| 3e | PEAHAT | EnemyPeahat | Direct | |
| 40 | WIZZROBE | EnemyWizzrobe | Direct | Oracle projectile/teleport timing to verify. |
| 41 | CROW | EnemyCrow | Direct | |
| 43 | GEL | EnemyGel | Direct | |
| 45 | PINCER | EnemyPincer | Direct | |
| 48 | SWORD_DARKNUT | EnemyDarknutSword | Direct | |
| 49 | SWORD_SHROUDED_STALFOS | EnemyShroudedStalfos | Adapt | |
| 4a | SWORD_MASKED_MOBLIN | EnemyMoblinPigSword | Adapt | |
| 4b | BALL_AND_CHAIN_SOLDIER | - | Oracle-only | |
| 4c | BLUE_CROW | EnemyCrow | Direct | Palette variant. |
| 4d | HARDHAT_BEETLE | EnemyHardhatBeetle | Direct | |
| 4e | ARM_MIMIC | EnemyArmMimic | Direct | |
| 4f | MOLDORM | EnemyMiniMoldorm | Adapt | Multi-segment body. |
| 50 | FIREBALL_SHOOTER | EnemyFireball | Adapt | |
| 51 | BEETLE | EnemyBeetle | Direct | |
| 52 | FLYING_TILE | EnemyFlyingTile | Direct | |
| 53 | DRAGONFLY | - | Oracle-only | |
| 57 | STUB_57 | - | Stub/unused | |
| 58 | BUSH_OR_ROCK | - | Boss/script | Spawner/terrain marker, not a host. |
| 59 | ITEM_DROP_PRODUCER | - | Boss/script | Drop table producer. |
| 5a | SEEDS_ON_TREE | - | Boss/script | |
| 5d | TWINROVA_ICE | - | Boss/script | |
| 5e | TWINROVA_BAT | - | Boss/script | |
| 60 | GANON_REVIVAL_CUTSCENE | - | Boss/script | |
| 65..6f | STUB_65..6f | - | Stub/unused | |
| 75 | VIRE | EnemyVire | Direct | Split bat behaviour. |

Not listed ids (`02,05,06,07,0f,11,1f,26,2b,3c,3f,42,44,46,47,54..56,5b,5c,5f,61..64,70..74,76..7f`)
are unused in the common Ages tables. `26` is the Angler Fish bubble (boss
part), not a standalone enemy.

## 3. First hosted enemy: ENEMY_OCTOROK `$09`

### 3.1 Source data

| Field | `$00` red | `$01` fast red | `$02` blue |
|---|---|---|---|
| `enemy09SubidData` extra/flags | `$08 $20` | `$08 $20` | `$0c $10` |
| `extraEnemyData` row | `$08` | `$08` | `$0c` |
| Radius Y/X | 6/6 | 6/6 | 6/6 |
| Damage (quarters) | 1 | 1 | 2 |
| Health | 2 | 2 | 3 |
| Walk speed (`octorok.s`) | `SPEED_80` = `$14` | `SPEED_c0` = `$1e` | `SPEED_80` = `$14` |
| `@counter1Ranges` / shoot mask | 7 | 7 | 3 |
| Faces Link before firing | no | no | yes |

Generated table: `Data/Map Objects/oracle_ages_enemy_stats.tsv` (gitignored,
regenerated by `overworld`/`dungeons`).

### 3.2 Native state machine (`octorok.s`)

| Native state | Behaviour | Runtime state |
|---|---|---|
| `octorok_state_09` | Stand for `counter1`; then random cardinal angle, 1-in-4 chance toward Link, walk counter from `{25,33,41,49}`. | `idle` -> `walking` |
| `octorok_state_0a` | Apply velocity for `var30` frames; on wall, random cardinal angle. | `walking` (`UpdateWalking`) |
| `octorok_state_08` | `random & var32 == 0` -> shoot; otherwise stand. | `OnDecided` |
| `octorok_state_0b` | Wait 16 frames, spawn `PART_OCTOROK_PROJECTILE`, `counter1 = $20`, return to state 9. | `shoot` -> `idle` (32-frame wait) |

`counter1Values` = `{30,45,60,75,45,60,75,90}`; subids 0/1 use all eight
(mask 7), subid 2 uses the first four (mask 3). `var32` is the same mask.

Room gate: the host only shoots when
`OracleRoomRuntime.ResolveRoom(map.OracleInfo, position) ==
OracleSession.Room`. The camera culling margin no longer lets adjacent-room
Octoroks fire at Link.

### 3.3 Cardinal frames

`enemy09Animations` exports four poses. The rendered order is:

| Exported suffix | Facing | Runtime direction |
|---|---|---|
| `d0` | right | 2 |
| `d1` | down | 3 |
| `d2` | left | 0 |
| `d3` | up | 1 |

Runtime direction order is left, up, right, down, so
`DirectionAnimationIndex = {2,3,0,1}`. This mapping was verified against the
composed frames; `ooa-godot` labels the same entries `up/right/down/left`, which
is rotated relative to the actual art.

### 3.4 PART18 Octorok projectile

`partData.s` row `$18`: gfx header `$8f`, collision `$87`, radius 2/2,
damage `$fc` (2 quarters), tile base `$0c`, palette 3. `octorokProjectile.s`
state 0 sets counter1 `$50` (speed/lifetime source). Runtime
`ObjOracleOctorokShot` uses the imported `part18Animations`/`part18OamData`
frames with native `BodyComponent` (bounces, ignores height), damage field,
hittable component and a small shadow. It is deleted after the native counter
or on sword/shield hit; it is not spawned inside a solid tile.

Generated table: `Data/Map Objects/oracle_ages_part_stats.tsv` (gitignored).

## 4. Promotion checklist

Before adding the next species:

1. Add its `EnemyStats` row (and subid table) to `EnemyStats.cs`; cite the
   source lines.
2. Compose all cardinal animations; verify the visual mapping against the
   exported frames, not the importer labels.
3. Translate only the states used by the host; keep projectile timings from
   `enemy*.s`/`part*.s`.
4. Use native components: body, shadow, hittable, damage field, pushable.
5. Gate AI/actions by the player's Oracle room.
6. Verify movement, facing, projectile, sword hit, knockback, death, shadow
   and room gate before marking it Hosted.
