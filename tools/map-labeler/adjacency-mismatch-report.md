# Adjacency mismatch report

Geometry from `Data/Map/SectorLayout.json` vs edges in `Data/Map/Adjacency.json` (snap tol 3.0px).

## Summary

- Labeled sectors: **155** / 155
- Geometry edges: **391**
- Adjacency.json edges: **391**
- Missing in Adjacency.json (geometry has, JSON lacks): **0**
- Extra in Adjacency.json (JSON has, geometry lacks): **0**
- Suppressed geometry false positives: **9**
- Sectors with degree mismatch: **0**

Interpretation:

- **Missing** → likely need to **add** an edge to `Adjacency.json` (if the shared boundary is real).
- **Extra** → likely need to **remove** an edge (or the layout label is wrong / shared edge too short to detect).
- **Suppressed** → geometry false positives confirmed non-adjacent by review (`geometry-false-positives.json`).

## Missing edges (add candidates)

_None._

## Extra edges (remove candidates)

_None._

## Suppressed geometry false positives

| a | b | zones |
|---|---|---|
| `border-georgia-r2-03` (Boros) | `border-georgia-r3-06` (border-georgia-r3-06) | Georgia / Georgia |
| `border-georgia-r3-05` (border-georgia-r3-05) | `border-georgia-r3-06` (border-georgia-r3-06) | Georgia / Georgia |
| `border-georgia-r3-06` (border-georgia-r3-06) | `border-space-r2-24` (border-space-r2-24) | Georgia / Border Space |
| `border-himinbjorg-r1-01` (Aesir) | `border-space-r2-08` (border-space-r2-08) | Himinbjorg / Border Space |
| `rim-cortex-relay-2-r1-11` (Cortex Relay 2) | `rim-penglai-r1-01` (Newhall) | Special / Penglai |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-penglai-r1-02` (Beylix) | Kalidasa / Penglai |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-space-r2-10` (rim-space-r2-10) | Kalidasa / Rim Space |
| `rim-penglai-r1-01` (Newhall) | `rim-space-r1-10` (rim-space-r1-10) | Penglai / Rim Space |
| `rim-penglai-r1-02` (Beylix) | `rim-space-r2-10` (rim-space-r2-10) | Penglai / Rim Space |

## Per-sector degree mismatches

_None._

