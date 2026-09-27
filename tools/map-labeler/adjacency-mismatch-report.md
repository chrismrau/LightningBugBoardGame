# Adjacency mismatch report

Geometry from `Data/Map/SectorLayout.json` vs edges in `Data/Map/Adjacency.json` (snap tol 3.0px).

## Summary

- Labeled sectors: **155** / 155
- Geometry edges: **397**
- Adjacency.json edges: **397**
- Missing in Adjacency.json (geometry has, JSON lacks): **0**
- Extra in Adjacency.json (JSON has, geometry lacks): **0**
- Suppressed geometry false positives: **5**
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
| `border-himinbjorg-r1-01` (Aesir) | `border-space-r2-08` (border-space-r2-08) | Himinbjorg / Border Space |
| `border-himinbjorg-r1-02` (Brisingamen) | `border-space-r2-07` (border-space-r2-07) | Himinbjorg / Border Space |
| `border-space-r2-09` (border-space-r2-09) | `rim-kalidasa-r4-12` (rim-kalidasa-r4-12) | Border Space / Kalidasa |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-penglai-r1-01` (Newhall) | Kalidasa / Penglai |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-space-r2-10` (rim-space-r2-10) | Kalidasa / Rim Space |

## Per-sector degree mismatches

_None._

