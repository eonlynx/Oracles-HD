# Oracles-HD

**A native, from-source port of *The Legend of Zelda: Oracle of Ages* and
*Oracle of Seasons*, built on the Links-Awakening-DX-HD engine.**

The engine, renderer, camera, entity/component system, save manager, UI base and
GBS audio player come from Links-Awakening-DX-HD v2.0.6. The Oracle world, maps,
tilesets, palettes, collisions, terrain and tile animations are generated from
the pinned [oracles-disasm](https://github.com/Stewmath/oracles-disasm)
disassembly.

> [!IMPORTANT]
> **No ROMs or original game data are included.** The importer reads the
> disassembly sources at build time and writes local assets under `Data/`.
> This is an unaffiliated fan project; Nintendo and Capcom own the games.

## Status

| Area | State |
|---|---|
| Ages - present overworld | **Playable vertical slice**: 196/196 rooms, walking, DX-HD camera |
| Ages - past overworld | Generated and walkable: 196/196 rooms |
| Tile collisions / terrain | Water, deep water, lava, grass, solid quadrants |
| Tile animations | Baked into the atlas; timeline in the `.map` (no `.ani`) |
| Room objects (NPCs, enemies, doors, Maku Tree eyes) | Not yet - next milestone |
| HUD / inventory / text / audio / save | Still the LA fallback, being replaced |
| Seasons | Tables probe only; seasonal tilesets not yet implemented |
| Platforms | Windows / DirectX 11 today; multiplatform is WIP (`docs/PLATFORM.md`) |

The detailed tracker is [`docs/STATUS.md`](docs/STATUS.md); phases and
acceptance criteria are in [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Quick start

Requirements: Windows 10/11, Git, and the **.NET 8 SDK**.

```powershell
git clone --recurse-submodules <this-repository> Oracles-HD
cd Oracles-HD

# Build (uses a user-local SDK if present; see build.cmd)
.\build.cmd
```

Generate the Ages assets (offline; reads `external/oracles-disasm`):

```powershell
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 0
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 1
```

Run the development entry directly into the generated present overworld:

```powershell
bin\Debug\net8.0-windows\Oracles-HD.exe oracle
```

Without `oracle`, the original LA intro/menu still boots; it is the fallback
until the Oracle title/file-select replaces it (`docs/CLEANUP.md`).

### Controls

DX-HD defaults (configurable in Settings -> Controls):

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | Arrow keys | D-pad |
| A | S | A |
| B | D | B |
| X | A | X |
| Y | W | Y |
| Select | Space | Back |
| Start | Enter | Start |

## Project layout

| Path | Contents |
|---|---|
| `Base/` | Input, UI primitives, math, small engine helpers |
| `InGame/` | Screens, map/camera, entities, overlays, save, pages, editor hooks |
| `Editor/` | DX-HD map/tileset/animation editors |
| `GbsPlayer/` | Game Boy CPU + PSG player used for music |
| `Tools/OracleAssetBuilder/` | Offline importer: rooms, overworlds, atlases, collisions, terrain, animations, manifests |
| `external/oracles-disasm/` | Pinned, read-only data source (`21c924af`) |
| `Data/` | Engine data; generated Oracle assets are gitignored |
| `docs/` | Status, architecture, formats, decisions, roadmap, verification, cleanup |

## Asset pipeline

```
oracles-disasm  ->  Tools/OracleAssetBuilder  ->  Data/Maps/*.map
                                              ->  Data/Maps/Tilesets/*.png
                                              ->  *.manifest.json (SHA-256)
```

- The runtime never parses `.s`, `.bin` or a ROM.
- `Data/Maps/oracle_*`, tilesets and previews are generated and gitignored.
- Manifests are committed so a build records its exact inputs/outputs.
- The import is deterministic; see `docs/VERIFICATION.md`.

## Documentation

- [`docs/README.md`](docs/README.md) - documentation map, task map and code entry points.
- [`docs/PROJECT_PRINCIPLES.md`](docs/PROJECT_PRINCIPLES.md) - fidelity, evidence order, definition of done.
- [`docs/STATUS.md`](docs/STATUS.md) - parity/progress tracker.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) - engine layers, runtime entry, planned contracts.
- [`docs/FORMATS.md`](docs/FORMATS.md) - disassembly to `.map`/atlas formats.
- [`docs/DECISIONS.md`](docs/DECISIONS.md) - ADRs and their evidence.
- [`docs/COVERAGE.md`](docs/COVERAGE.md) - content inventory (enemies, NPCs, items, scripts).
- [`docs/ROADMAP.md`](docs/ROADMAP.md) - phases, acceptance criteria and next tasks.
- [`docs/VERIFICATION.md`](docs/VERIFICATION.md) - checks and recorded evidence.
- [`docs/BUILDING.md`](docs/BUILDING.md) - SDK, build, assets, publishing, troubleshooting.
- [`docs/THIRD_PARTY.md`](docs/THIRD_PARTY.md) - licences and reference-use policy.
- [`docs/CLEANUP.md`](docs/CLEANUP.md) - removing LA legacy incrementally.
- [`docs/PLATFORM.md`](docs/PLATFORM.md) - WindowsDX today; multiplatform WIP.

## Building and publishing

See [`docs/BUILDING.md`](docs/BUILDING.md) for prerequisites, the user-local SDK,
asset generation, publishing and troubleshooting. In short: `.\build.cmd`,
`publish.bat`; WLA-DX is not required for the current map/tileset assets.

## Known limitations

Room objects (Maku Tree eyes, NPCs, enemies), interiors/warps, special
collisions, and the Oracle HUD/text/audio/save are not implemented yet. Seasons
needs seasonal tilesets/layouts and Subrosia. The live list is
[`docs/STATUS.md`](docs/STATUS.md); the content inventory is
[`docs/COVERAGE.md`](docs/COVERAGE.md).

## Credits

Engine base: [Links-Awakening-DX-HD](https://github.com/ihm-tswow/Links-Awakening-DX-HD)
v2.0.6. References: `oracles-disasm`, `oracles-decomp`, `top`, `ooa-godot`,
`epoch-equinox`. Licence and reuse details:
[`docs/THIRD_PARTY.md`](docs/THIRD_PARTY.md). Nintendo/Capcom own *The Legend of
Zelda* and the Oracle games.

Developed with AI assistance; behaviour is checked against the disassembly and
the running game, and every milestone is recorded in `docs/VERIFICATION.md`.
