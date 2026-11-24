# Third-party code and data

This is a personal, non-commercial project. The table records what each
reference repository is and how it may be used. It is not legal advice.

| Repository | Licence | Use in Oracles-HD |
|---|---|---|
| `Links-Awakening-DX-HD` v2.0.6 | No `LICENSE` file in this tree | Engine base (local fork); owner-authorized for this project. |
| `oracles-disasm` | No `LICENSE` file in this tree | Pinned data source; owner-authorized offline use. |
| `ooa-godot` | No `LICENSE` file in this tree | Behavioural accelerator; owner-authorized for this project. |
| `oracles-decomp` | **GPLv3** | Authoritative C spec; copying code into the engine would make the combined work GPLv3. |
| `top` | **MIT** | Data readers/algorithms and format documentation; keep the copyright notice when copying. |
| `epoch-equinox` | **MIT** | Documentation/format reference and small utilities; keep the notice. |

The owner authorization (ADR-018) covers this personal project only. The
licence column still governs any distribution.

## Game data

- No ROM, extracted graphics, audio, text or save file is committed.
- `Tools/OracleAssetBuilder` reads `external/oracles-disasm` locally and writes
  assets under `Data/`, which is gitignored.
- Manifests contain paths and SHA-256 hashes, not game data.
- Do not commit a ROM, a `.gbs`, an extracted tileset or a generated map.

## Owner authorization

The project owner authorized reuse of all reference repositories for this
personal, non-commercial project (2026-11-25, ADR-018). The caveats below apply
if the project is ever distributed.

## Code reuse policy

- Prefer behavioural reimplementation from the disassembly/spec over copying.
- MIT code (`top`, `epoch-equinox`) may be adapted with attribution.
- GPLv3 code (`oracles-decomp`) is a specification; do not paste it into the
  engine unless the project is relicensed.
- No-licence repositories are authorized for this personal project by the owner
  (ADR-018); distribution needs the authors' permission.
- Record any adapted third-party code in this file and in the commit message.

## Credits

- Engine: Links-Awakening-DX-HD v2.0.6.
- Data/reference: `oracles-disasm`, `oracles-decomp`, `top`, `ooa-godot`,
  `epoch-equinox`.
- *The Legend of Zelda* and the Oracle games belong to Nintendo/Capcom.
