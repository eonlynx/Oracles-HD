# Building

## Requirements

- Windows 10/11 x64.
- Git.
- .NET **8 SDK** (runtime alone is not enough).
- The pinned `external/oracles-disasm` submodule (no ROM required for the
  current map/tileset pipeline).

WLA-DX is **not** required for maps, tilesets, palettes, collisions, terrain or
tile animations. It will be needed later for byte-exact ROM checks and for the
Oracle `.gbs`.

## SDK and `build.cmd`

This machine has the SDK installed per-user at
`%LOCALAPPDATA%\Microsoft\dotnet`. `build.cmd` prepends that directory to `PATH`
and calls `dotnet build`:

```powershell
.\build.cmd
```

A normal `dotnet build` works if your shell resolves a .NET 8 SDK.

## Build and run

```powershell
git clone --recurse-submodules <repository> Oracles-HD
cd Oracles-HD
.\build.cmd

# normal LA fallback boot
bin\Debug\net8.0-windows\Oracles-HD.exe

# development entry: generated Ages present overworld
bin\Debug\net8.0-windows\Oracles-HD.exe oracle

# present interior (group 2 room id)
bin\Debug\net8.0-windows\Oracles-HD.exe oracle --room e7

# spawn override in map pixels
bin\Debug\net8.0-windows\Oracles-HD.exe oracle --pos 1360,470

# development flag/room-condition overrides
bin\Debug\net8.0-windows\Oracles-HD.exe oracle --flag FINISHEDGAME
bin\Debug\net8.0-windows\Oracles-HD.exe oracle --room-state 3 --room-flags 80
```

Publish:

```powershell
.\publish.bat
```

The `Publish` folder is produced by the DX-HD publish profile.

## Generate assets

```powershell
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 0
dotnet run --project Tools/OracleAssetBuilder -- overworld --game ages --group 1
dotnet run --project Tools/OracleAssetBuilder -- interiors --game ages
dotnet run --project Tools/OracleAssetBuilder -- room --game ages --group 4 --room 01
dotnet run --project Tools/OracleAssetBuilder -- inspect --game ages --tileset 22
dotnet run --project Tools/OracleAssetBuilder -- inspect-anim --game ages --group 0
```

`interiors` always builds the present-day interior group (2), regardless of
`--group`; `--room e7` limits it to one map. The overworld and interiors share
`Data/Map Objects/oracle_ages_entities.png/.atlas`.

Outputs are written under `Data/Maps/`, `Data/Maps/Tilesets/`, and
`Data/Map Objects/`; previews under `Data/Maps/Previews/`. They are gitignored;
the manifests are committed.

## Content pipeline

`Content/Content.mgcb` is built by `MonoGame.Content.Builder.Task` 3.8.1.303.
The game's `.xnb` content is the DX-HD set; Oracle maps/atlases are raw files
copied via the `Data/**` item group.

## Troubleshooting

- **`dotnet` has no SDK**: use `.\build.cmd` or prepend
  `%LOCALAPPDATA%\Microsoft\dotnet` to `PATH`.
- **`dotnet tool restore` fails**: it must run with the .NET 8 SDK on `PATH`.
- **46 warnings on build**: upstream v2.0.6 warnings; errors are the gate.
- **Game exits on launch**: check `bin/.../stderr` when running through
  `dotnet Oracles-HD.dll`; the base ships a duplicate `.lng` fix.
- **Black map cell**: check the room inclusion flag (`OUTDOORS | MAKU`) and the
  map's collision/terrain sections.
- **Multiplatform**: see `PLATFORM.md`; the v2.0.6 tree is WindowsDX-only.
