# Legacy cleanup policy

Oracles-HD targets *Oracle of Ages* and *Oracle of Seasons* only. It does not
need Link's Awakening compatibility. As each system is replaced, remove the LA
code/data that becomes orphaned; keep only the generic engine.

## Rules

1. **Delete, do not comment.** Git history is the archive. No
   `// legacy` blocks, no `if (false)`.
2. **Remove when the replacement exists and is verified.** Do not delete a
   subsystem while it is still the only working path.
3. **Keep the generic engine:** renderer, camera, physics/components, GBS
   player, asset loading, UI widgets, save manager, editor.
4. **Remove data with its code:** when an LA map, animation, atlas, sound or
   language file is no longer read, delete it and its `.csproj`/`Content`
   reference.
5. **One subsystem per commit.** State what was removed and what replaced it.

## Current inventory

Still present and still used by the fallback boot/menu:

| Area | Examples | Replacement |
|---|---|---|
| Intro/menu | `IntroScreen`, `MenuScreen`, `MainMenuPage`, `NewGamePage` | Oracle title/file select |
| HUD/inventory | `HudOverlay`, `InventoryOverlay`, `ItemSlotOverlay`, `ItemManager` | Oracle status bar, inventory, rings |
| Maps | `Data/Maps/*.map` (LA), LA tilesets, `MapOverlay` | Oracle generated maps |
| Objects | LA enemies, bosses, NPCs, things, dungeon objects | Oracle object stream + AI |
| Text | `Data/Languages/*.lng` LA dialogue | Oracle `text.yaml` |
| Audio | `Data/Music/awakening.gbs`, LA SFX | Oracle GBS/SFX |
| Save | LA save keys/layout | Oracle save/progression |
| Sequences | LA cutscene sequences | Oracle scripts/events |

Keep indefinitely (engine):

- `Base/` (input, UI primitives, math), `InGame/Map` (camera, tilemap, pools),
  `InGame/GameObjects/Base` (components, AI primitives, systems),
  `GbsPlayer`, `InGame/Interface`, `InGame/SaveLoad/SaveManager` (generic KV),
  `Editor/` (asset authoring), `InGame/Things/Resources.cs`.

## Order of removal

1. **HUD/inventory** - after the Oracle status bar and inventory exist.
2. **Intro/menu** - after the Oracle title/file select; remove `--oracle` only
   when the normal entry loads Oracle content.
3. **Maps/tilesets/objects** - after the Oracle object stream and dungeon maps
   cover the LA fallback.
4. **Text/audio/save** - after Oracle text, GBS and save are verified.
5. **LA data files** - last; delete with their loaders.

## Checklist per subsystem

- [ ] Grep for references; none outside the subsystem.
- [ ] Remove classes, data files and `.csproj`/`mgcb` entries.
- [ ] `dotnet build` -> 0 errors.
- [ ] Launch normal and `--oracle`; no missing-resource exception.
- [ ] Update `STATUS.md`, `ARCHITECTURE.md` code map and this inventory.
- [ ] Commit: `chore: remove legacy <subsystem> after <replacement>`.
