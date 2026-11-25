# Project principles

## Target

Reproduce *Oracle of Ages* and *Oracle of Seasons* on the DX-HD engine:
**engine parity with Links-Awakening-DX-HD v2.0.6** and **content parity with
the Oracle games**. No extra engine features, menus, filters or shaders.

Use evidence in this order:

1. Executed behaviour in the clean US ROM (or a recorded trace).
2. `oracles-disasm` code/data, with `oracles-decomp` and `top` as corroboration.
3. Generated typed records that retain source identity.
4. Runtime behaviour and the checks in `VERIFICATION.md`.
5. Assumptions, memory, screenshots or visual approximation.

`ooa-godot` is the default accelerator for content logic (enemies, bosses,
scripts, inventory, UI). When it conflicts with 1-2, the disassembly wins and the
conflict is recorded in `DECISIONS.md`.

## Reference reuse workflow

Before writing a low-level parser, read the existing high-level implementation in
this order:

1. **`ooa-godot`** - its `tools/import_oracles` scripts and runtime C# already
   describe warps, object streams, interaction presentation, OAM composition and
   entity behaviour. Translate or adapt that C# to DX-HD instead of re-deriving
   the format from `.s`.
2. **`top` / `oracles-decomp`** - use the C readers and the C specification to
   check a detail, a branch, Seasons data or an edge case that `ooa-godot` does
   not cover.
3. **`oracles-disasm`** - the raw data source and final arbiter. Read it directly
   only when the higher-level implementations disagree or do not cover the case,
   and record the finding.

The output still targets DX-HD mechanisms; reuse means adapting the algorithm,
not importing another engine's architecture. This rule is recorded as ADR-028.

## Fidelity rules

- Trace handlers, callers, tables and placement before coding.
- Preserve source order, RNG consumption and 60-update counter boundaries.
- Preserve byte/integer/fixed-point arithmetic where it affects behaviour.
- Keep gameplay coordinates in room/world space; camera and HUD are
  presentation.
- Import general tables; a room-specific branch is correct only when the source
  has one.
- Reject unsupported data with a source path and hex ID. Never turn an unknown
  opcode, object or treasure into a silent no-op.
- Do not add save migrations, automatic repairs or unstuck rules; fix the cause.
- Keep hex source IDs in diagnostics and comments.

## Architecture consequences

- The importer is the only component that reads `oracles-disasm`; the runtime
  reads generated assets only.
- One authoritative owner per state (save image, room identity, RNG, inventory,
  modal/transition state). Do not mirror state.
- Shared components are used only for genuinely shared mechanics. Species
  state machines, scripts and counter semantics stay distinct when the source
  distinguishes them.
- Legacy LA code/data is removed when its Oracle replacement exists and is
  verified (`CLEANUP.md`); no commented-out legacy.

## Definition of done

For a behavioural change:

1. Record the original inputs, state, ordering, timing, arithmetic and side
   effects.
2. Extend the importer if the runtime lacks required source information.
3. Implement the smallest general behaviour that matches the evidence.
4. Add a focused check for the reported case and meaningful branches (re-entry,
   persistence, failure) when a harness exists.
5. Regenerate the affected assets; build with zero errors; run the checks.
6. Update `STATUS.md` and the affected durable document in the same commit.

A feature is complete when its data, runtime effects, timing, transitions,
persistence and re-entry agree with the source for the supported paths.
