# Adjacency mismatch report

Geometry from `Data/Map/SectorLayout.json` vs edges in `Data/Map/Adjacency.json` (snap tol 3.0px).

## Summary

- Labeled sectors: **155** / 155
- Geometry edges: **402**
- Adjacency.json edges: **402**
- Missing in Adjacency.json (geometry has, JSON lacks): **0**
- Extra in Adjacency.json (JSON has, geometry lacks): **0**
- Suppressed geometry false positives: **0**
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

_None._

## Per-sector degree mismatches

_None._

