# Platform status

## Base

`Oracles-HD` starts from **Links-Awakening-DX-HD v2.0.6** (`2e68849`).

## Actual state of the inherited code

The v2.0.6 source tree is **Windows-only**:

- `TargetFramework`: `net6.0-windows` (migrated to `net8.0-windows` in Phase 1).
- Graphics backend: `MonoGame.Framework.WindowsDX 3.8.1.303` (**DirectX 11**).
- `UseWindowsForms=true` (editor dialogs, `MessageBox`, window management).
- There are no packages, folders or `#if` blocks for Linux, macOS, OpenGL,
  Vulkan or DX12.

Verified against the full repository history: all three existing commits
(initial, `origin/upstream` and v2.0.6) declare the same `TargetFramework` and
the same `WindowsDX` package. The `KNI`/`Vulkan`/`DX12` strings appear only
inside the prebuilt `.exe` files shipped in the repository, never in the source.

## Current status

| Platform | Backend | Status |
|---|---|---|
| Windows x64 | DirectX 11 (WindowsDX) | **Working** (Phase 1) |
| Linux x64 | OpenGL (DesktopGL) | **WIP** — waiting for sources/port |
| macOS (Intel/ARM) | OpenGL/Metal (DesktopGL) | **WIP** — waiting for sources/port |
| Windows | Vulkan | **WIP** — waiting for sources/port |
| Windows | DirectX 12 | **WIP** — waiting for sources/port |

No speculative dependency has been added: the `.csproj` keeps
`MonoGame.Framework.WindowsDX` so the only verified build stays green.

## Plan when the multiplatform sources arrive

1. Add per-platform conditions to `Oracles-HD.csproj`:
   `net8.0-windows` + `MonoGame.Framework.WindowsDX` for DX11,
   `net8.0` + `MonoGame.Framework.DesktopGL` for Linux/macOS,
   and the KNI/Vulkan/DX12 packages that match the received sources.
2. Isolate the WinForms dependencies that are still unconditional
   (`Program.cs` uses `MessageBox` without `#if WINDOWS`; the rest is already
   guarded).
3. Build content (`.fx` → MGFX) per platform; today `Content/Content.mgfx` is
   produced with the Windows pipeline.
4. Verify startup and rendering on every backend before closing the matrix.

## Note on the official binary

The official v2.0.6 `.exe` references KNI, Vulkan, DX12, WindowsDX and OpenGL,
which suggests it was built from a different tree (probably KNI) that is not
included in this repository. It cannot be reproduced from these sources without
that tree.
