# Adjacency mismatch report

Geometry from `Data/Map/SectorLayout.json` vs edges in `Data/Map/Adjacency.json` (snap tol 3.0px).

## Summary

- Labeled sectors: **155** / 155
- Geometry edges: **351**
- Adjacency.json edges: **398**
- Missing in Adjacency.json (geometry has, JSON lacks): **0**
- Extra in Adjacency.json (JSON has, geometry lacks): **47**
- Suppressed geometry false positives: **12**
- Sectors with degree mismatch: **65**

Interpretation:

- **Missing** → likely need to **add** an edge to `Adjacency.json` (if the shared boundary is real).
- **Extra** → likely need to **remove** an edge (or the layout label is wrong / shared edge too short to detect).
- **Suppressed** → geometry false positives confirmed non-adjacent by review (`geometry-false-positives.json`).

## Missing edges (add candidates)

_None._

## Extra edges (remove candidates)

| a | b | zones |
|---|---|---|
| `alliance-lux-r1-01` (Persephone) | `alliance-lux-r1-02` (Pelorum) | Lux / Lux |
| `alliance-white-sun-r2-01` (alliance-white-sun-r2-01) | `alliance-white-sun-r2-02` (Sihnon) | White Sun / White Sun |
| `alliance-white-sun-r2-03` (Liann Jiun) | `alliance-white-sun-r2-04` (alliance-white-sun-r2-04) | White Sun / White Sun |
| `alliance-white-sun-r3-01` (Bellerophon) | `alliance-white-sun-r3-02` (alliance-white-sun-r3-02) | White Sun / White Sun |
| `alliance-white-sun-r3-03` (Gonghe) | `alliance-white-sun-r3-04` (alliance-white-sun-r3-04) | White Sun / White Sun |
| `alliance-white-sun-r3-05` (alliance-white-sun-r3-05) | `alliance-white-sun-r3-06` (alliance-white-sun-r3-06) | White Sun / White Sun |
| `alliance-white-sun-r3-06` (alliance-white-sun-r3-06) | `alliance-white-sun-r3-07` (Osiris) | White Sun / White Sun |
| `alliance-white-sun-r3-07` (Osiris) | `alliance-white-sun-r3-08` (alliance-white-sun-r3-08) | White Sun / White Sun |
| `alliance-white-sun-r4-01` (alliance-white-sun-r4-01) | `alliance-white-sun-r4-02` (alliance-white-sun-r4-02) | White Sun / White Sun |
| `alliance-white-sun-r4-03` (alliance-white-sun-r4-03) | `alliance-white-sun-r4-04` (alliance-white-sun-r4-04) | White Sun / White Sun |
| `alliance-white-sun-r4-08` (Valentine) | `alliance-white-sun-r4-09` (alliance-white-sun-r4-09) | White Sun / White Sun |
| `alliance-white-sun-r4-09` (alliance-white-sun-r4-09) | `alliance-white-sun-r4-10` (alliance-white-sun-r4-10) | White Sun / White Sun |
| `alliance-white-sun-r4-10` (alliance-white-sun-r4-10) | `alliance-white-sun-r4-11` (Albion) | White Sun / White Sun |
| `border-georgia-r2-02` (border-georgia-r2-02) | `border-georgia-r2-03` (Boros) | Georgia / Georgia |
| `border-georgia-r2-03` (Boros) | `border-georgia-r2-04` (Kerry) | Georgia / Georgia |
| `border-georgia-r2-03` (Boros) | `border-georgia-r3-06` (Three Hills) | Georgia / Georgia |
| `border-georgia-r3-01` (border-georgia-r3-01) | `border-georgia-r3-02` (Newhope) | Georgia / Georgia |
| `border-georgia-r3-05` (border-georgia-r3-05) | `border-georgia-r3-06` (Three Hills) | Georgia / Georgia |
| `border-georgia-r3-06` (Three Hills) | `border-georgia-r3-07` (border-georgia-r3-07) | Georgia / Georgia |
| `border-georgia-r3-06` (Three Hills) | `border-space-r2-24` (border-space-r2-24) | Georgia / Border Space |
| `border-georgia-r3-07` (border-georgia-r3-07) | `rim-space-r1-27` (rim-space-r1-27) | Georgia / Rim Space |
| `border-red-sun-r2-01` (border-red-sun-r2-01) | `border-red-sun-r2-02` (Harvest) | Red Sun / Red Sun |
| `border-red-sun-r2-01` (border-red-sun-r2-01) | `border-red-sun-r2-04` (border-red-sun-r2-04) | Red Sun / Red Sun |
| `border-red-sun-r2-02` (Harvest) | `border-red-sun-r2-03` (St. Albans) | Red Sun / Red Sun |
| `border-red-sun-r2-03` (St. Albans) | `border-red-sun-r2-04` (border-red-sun-r2-04) | Red Sun / Red Sun |
| `border-red-sun-r3-01` (border-red-sun-r3-01) | `border-red-sun-r3-02` (Jubilee) | Red Sun / Red Sun |
| `border-red-sun-r3-01` (border-red-sun-r3-01) | `border-red-sun-r3-08` (Space Bazaar) | Red Sun / Red Sun |
| `border-red-sun-r3-05` (border-red-sun-r3-05) | `border-red-sun-r3-06` (border-red-sun-r3-06) | Red Sun / Red Sun |
| `border-red-sun-r3-06` (border-red-sun-r3-06) | `border-red-sun-r3-07` (Greenleaf) | Red Sun / Red Sun |
| `border-red-sun-r3-07` (Greenleaf) | `border-red-sun-r3-08` (Space Bazaar) | Red Sun / Red Sun |
| `border-space-r1-22` (border-space-r1-22) | `border-space-r1-23` (border-space-r1-23) | Border Space / Border Space |
| `border-space-r2-11` (border-space-r2-11) | `border-space-r2-12` (border-space-r2-12) | Border Space / Border Space |
| `border-space-r2-23` (border-space-r2-23) | `border-space-r2-24` (border-space-r2-24) | Border Space / Border Space |
| `rim-blue-sun-r2-02` (Fury) | `rim-blue-sun-r2-03` (Muir) | Blue Sun / Blue Sun |
| `rim-blue-sun-r3-01` (rim-blue-sun-r3-01) | `rim-blue-sun-r3-02` (Dragon's Egg) | Blue Sun / Blue Sun |
| `rim-blue-sun-r3-02` (Dragon's Egg) | `rim-blue-sun-r3-03` (rim-blue-sun-r3-03) | Blue Sun / Blue Sun |
| `rim-cortex-relay-2-r1-11` (Cortex Relay 2) | `rim-penglai-r1-02` (Beylix) | Special / Penglai |
| `rim-kalidasa-r2-01` (Heaven) | `rim-kalidasa-r2-04` (Angel) | Kalidasa / Kalidasa |
| `rim-kalidasa-r3-01` (Aberdeen) | `rim-kalidasa-r3-06` (Whittier) | Kalidasa / Kalidasa |
| `rim-kalidasa-r3-05` (New Kasmir) | `rim-kalidasa-r4-09` (rim-kalidasa-r4-09) | Kalidasa / Kalidasa |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-space-r2-10` (rim-space-r2-10) | Kalidasa / Rim Space |
| `rim-kalidasa-r4-09` (rim-kalidasa-r4-09) | `rim-kalidasa-r4-10` (Djinn's Bane) | Kalidasa / Kalidasa |
| `rim-kalidasa-r4-09` (rim-kalidasa-r4-09) | `rim-penglai-r1-01` (Newhall) | Kalidasa / Penglai |
| `rim-kalidasa-r4-09` (rim-kalidasa-r4-09) | `rim-space-r1-10` (rim-space-r1-10) | Kalidasa / Rim Space |
| `rim-penglai-r1-01` (Newhall) | `rim-penglai-r1-02` (Beylix) | Penglai / Penglai |
| `rim-penglai-r1-01` (Newhall) | `rim-space-r2-10` (rim-space-r2-10) | Penglai / Rim Space |
| `rim-penglai-r1-02` (Beylix) | `rim-space-r1-10` (rim-space-r1-10) | Penglai / Rim Space |

## Suppressed geometry false positives

| a | b | zones |
|---|---|---|
| `border-georgia-r2-03` (Boros) | `border-georgia-r3-07` (border-georgia-r3-07) | Georgia / Georgia |
| `border-georgia-r3-05` (border-georgia-r3-05) | `border-georgia-r3-07` (border-georgia-r3-07) | Georgia / Georgia |
| `border-georgia-r3-06` (Three Hills) | `rim-space-r1-27` (rim-space-r1-27) | Georgia / Rim Space |
| `border-georgia-r3-07` (border-georgia-r3-07) | `border-space-r2-24` (border-space-r2-24) | Georgia / Border Space |
| `border-himinbjorg-r1-01` (Aesir) | `border-space-r2-08` (border-space-r2-08) | Himinbjorg / Border Space |
| `rim-cortex-relay-2-r1-11` (Cortex Relay 2) | `rim-penglai-r1-01` (Newhall) | Special / Penglai |
| `rim-kalidasa-r3-05` (New Kasmir) | `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | Kalidasa / Kalidasa |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-penglai-r1-02` (Beylix) | Kalidasa / Penglai |
| `rim-kalidasa-r4-08` (rim-kalidasa-r4-08) | `rim-space-r1-10` (rim-space-r1-10) | Kalidasa / Rim Space |
| `rim-kalidasa-r4-09` (rim-kalidasa-r4-09) | `rim-space-r2-10` (rim-space-r2-10) | Kalidasa / Rim Space |
| `rim-penglai-r1-01` (Newhall) | `rim-space-r1-10` (rim-space-r1-10) | Penglai / Rim Space |
| `rim-penglai-r1-02` (Beylix) | `rim-space-r2-10` (rim-space-r2-10) | Penglai / Rim Space |

## Per-sector degree mismatches

- **Persephone** `alliance-lux-r1-01` (Lux): geo=5 json=6
  - extra neighbors: `alliance-lux-r1-02`
- **Pelorum** `alliance-lux-r1-02` (Lux): geo=3 json=4
  - extra neighbors: `alliance-lux-r1-01`
- **alliance-white-sun-r2-01** `alliance-white-sun-r2-01` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r2-02`
- **Sihnon** `alliance-white-sun-r2-02` (White Sun): geo=6 json=7
  - extra neighbors: `alliance-white-sun-r2-01`
- **Liann Jiun** `alliance-white-sun-r2-03` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r2-04`
- **alliance-white-sun-r2-04** `alliance-white-sun-r2-04` (White Sun): geo=6 json=7
  - extra neighbors: `alliance-white-sun-r2-03`
- **Bellerophon** `alliance-white-sun-r3-01` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r3-02`
- **alliance-white-sun-r3-02** `alliance-white-sun-r3-02` (White Sun): geo=6 json=7
  - extra neighbors: `alliance-white-sun-r3-01`
- **Gonghe** `alliance-white-sun-r3-03` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r3-04`
- **alliance-white-sun-r3-04** `alliance-white-sun-r3-04` (White Sun): geo=6 json=7
  - extra neighbors: `alliance-white-sun-r3-03`
- **alliance-white-sun-r3-05** `alliance-white-sun-r3-05` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r3-06`
- **alliance-white-sun-r3-06** `alliance-white-sun-r3-06` (White Sun): geo=6 json=8
  - extra neighbors: `alliance-white-sun-r3-05`, `alliance-white-sun-r3-07`
- **Osiris** `alliance-white-sun-r3-07` (White Sun): geo=4 json=6
  - extra neighbors: `alliance-white-sun-r3-06`, `alliance-white-sun-r3-08`
- **alliance-white-sun-r3-08** `alliance-white-sun-r3-08` (White Sun): geo=6 json=7
  - extra neighbors: `alliance-white-sun-r3-07`
- **alliance-white-sun-r4-01** `alliance-white-sun-r4-01` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r4-02`
- **alliance-white-sun-r4-02** `alliance-white-sun-r4-02` (White Sun): geo=7 json=8
  - extra neighbors: `alliance-white-sun-r4-01`
- **alliance-white-sun-r4-03** `alliance-white-sun-r4-03` (White Sun): geo=7 json=8
  - extra neighbors: `alliance-white-sun-r4-04`
- **alliance-white-sun-r4-04** `alliance-white-sun-r4-04` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r4-03`
- **Valentine** `alliance-white-sun-r4-08` (White Sun): geo=5 json=6
  - extra neighbors: `alliance-white-sun-r4-09`
- **alliance-white-sun-r4-09** `alliance-white-sun-r4-09` (White Sun): geo=6 json=8
  - extra neighbors: `alliance-white-sun-r4-08`, `alliance-white-sun-r4-10`
- **alliance-white-sun-r4-10** `alliance-white-sun-r4-10` (White Sun): geo=5 json=7
  - extra neighbors: `alliance-white-sun-r4-09`, `alliance-white-sun-r4-11`
- **Albion** `alliance-white-sun-r4-11` (White Sun): geo=6 json=7
  - extra neighbors: `alliance-white-sun-r4-10`
- **border-georgia-r2-02** `border-georgia-r2-02` (Georgia): geo=5 json=6
  - extra neighbors: `border-georgia-r2-03`
- **Boros** `border-georgia-r2-03` (Georgia): geo=4 json=7
  - extra neighbors: `border-georgia-r2-02`, `border-georgia-r2-04`, `border-georgia-r3-06`
- **Kerry** `border-georgia-r2-04` (Georgia): geo=4 json=5
  - extra neighbors: `border-georgia-r2-03`
- **border-georgia-r3-01** `border-georgia-r3-01` (Georgia): geo=2 json=3
  - extra neighbors: `border-georgia-r3-02`
- **Newhope** `border-georgia-r3-02` (Georgia): geo=5 json=6
  - extra neighbors: `border-georgia-r3-01`
- **border-georgia-r3-05** `border-georgia-r3-05` (Georgia): geo=5 json=6
  - extra neighbors: `border-georgia-r3-06`
- **Three Hills** `border-georgia-r3-06` (Georgia): geo=2 json=6
  - extra neighbors: `border-georgia-r2-03`, `border-georgia-r3-05`, `border-georgia-r3-07`, `border-space-r2-24`
- **border-georgia-r3-07** `border-georgia-r3-07` (Georgia): geo=2 json=4
  - extra neighbors: `border-georgia-r3-06`, `rim-space-r1-27`
- **border-red-sun-r2-01** `border-red-sun-r2-01` (Red Sun): geo=5 json=7
  - extra neighbors: `border-red-sun-r2-02`, `border-red-sun-r2-04`
- **Harvest** `border-red-sun-r2-02` (Red Sun): geo=4 json=6
  - extra neighbors: `border-red-sun-r2-01`, `border-red-sun-r2-03`
- **St. Albans** `border-red-sun-r2-03` (Red Sun): geo=5 json=7
  - extra neighbors: `border-red-sun-r2-02`, `border-red-sun-r2-04`
- **border-red-sun-r2-04** `border-red-sun-r2-04` (Red Sun): geo=4 json=6
  - extra neighbors: `border-red-sun-r2-01`, `border-red-sun-r2-03`
- **border-red-sun-r3-01** `border-red-sun-r3-01` (Red Sun): geo=5 json=7
  - extra neighbors: `border-red-sun-r3-02`, `border-red-sun-r3-08`
- **Jubilee** `border-red-sun-r3-02` (Red Sun): geo=5 json=6
  - extra neighbors: `border-red-sun-r3-01`
- **border-red-sun-r3-05** `border-red-sun-r3-05` (Red Sun): geo=3 json=4
  - extra neighbors: `border-red-sun-r3-06`
- **border-red-sun-r3-06** `border-red-sun-r3-06` (Red Sun): geo=5 json=7
  - extra neighbors: `border-red-sun-r3-05`, `border-red-sun-r3-07`
- **Greenleaf** `border-red-sun-r3-07` (Red Sun): geo=4 json=6
  - extra neighbors: `border-red-sun-r3-06`, `border-red-sun-r3-08`
- **Space Bazaar** `border-red-sun-r3-08` (Red Sun): geo=5 json=7
  - extra neighbors: `border-red-sun-r3-01`, `border-red-sun-r3-07`
- **border-space-r1-22** `border-space-r1-22` (Border Space): geo=5 json=6
  - extra neighbors: `border-space-r1-23`
- **border-space-r1-23** `border-space-r1-23` (Border Space): geo=6 json=7
  - extra neighbors: `border-space-r1-22`
- **border-space-r2-11** `border-space-r2-11` (Border Space): geo=6 json=7
  - extra neighbors: `border-space-r2-12`
- **border-space-r2-12** `border-space-r2-12` (Border Space): geo=2 json=3
  - extra neighbors: `border-space-r2-11`
- **border-space-r2-23** `border-space-r2-23` (Border Space): geo=5 json=6
  - extra neighbors: `border-space-r2-24`
- **border-space-r2-24** `border-space-r2-24` (Border Space): geo=6 json=8
  - extra neighbors: `border-georgia-r3-06`, `border-space-r2-23`
- **Fury** `rim-blue-sun-r2-02` (Blue Sun): geo=6 json=7
  - extra neighbors: `rim-blue-sun-r2-03`
- **Muir** `rim-blue-sun-r2-03` (Blue Sun): geo=2 json=3
  - extra neighbors: `rim-blue-sun-r2-02`
- **rim-blue-sun-r3-01** `rim-blue-sun-r3-01` (Blue Sun): geo=2 json=3
  - extra neighbors: `rim-blue-sun-r3-02`
- **Dragon's Egg** `rim-blue-sun-r3-02` (Blue Sun): geo=5 json=7
  - extra neighbors: `rim-blue-sun-r3-01`, `rim-blue-sun-r3-03`
- **rim-blue-sun-r3-03** `rim-blue-sun-r3-03` (Blue Sun): geo=5 json=6
  - extra neighbors: `rim-blue-sun-r3-02`
- **Cortex Relay 2** `rim-cortex-relay-2-r1-11` (Special): geo=4 json=5
  - extra neighbors: `rim-penglai-r1-02`
- **Heaven** `rim-kalidasa-r2-01` (Kalidasa): geo=3 json=4
  - extra neighbors: `rim-kalidasa-r2-04`
- **Angel** `rim-kalidasa-r2-04` (Kalidasa): geo=5 json=6
  - extra neighbors: `rim-kalidasa-r2-01`
- **Aberdeen** `rim-kalidasa-r3-01` (Kalidasa): geo=3 json=4
  - extra neighbors: `rim-kalidasa-r3-06`
- **New Kasmir** `rim-kalidasa-r3-05` (Kalidasa): geo=6 json=7
  - extra neighbors: `rim-kalidasa-r4-09`
- **Whittier** `rim-kalidasa-r3-06` (Kalidasa): geo=7 json=8
  - extra neighbors: `rim-kalidasa-r3-01`
- **rim-kalidasa-r4-08** `rim-kalidasa-r4-08` (Kalidasa): geo=3 json=4
  - extra neighbors: `rim-space-r2-10`
- **rim-kalidasa-r4-09** `rim-kalidasa-r4-09` (Kalidasa): geo=3 json=7
  - extra neighbors: `rim-kalidasa-r3-05`, `rim-kalidasa-r4-10`, `rim-penglai-r1-01`, `rim-space-r1-10`
- **Djinn's Bane** `rim-kalidasa-r4-10` (Kalidasa): geo=5 json=6
  - extra neighbors: `rim-kalidasa-r4-09`
- **Newhall** `rim-penglai-r1-01` (Penglai): geo=2 json=5
  - extra neighbors: `rim-kalidasa-r4-09`, `rim-penglai-r1-02`, `rim-space-r2-10`
- **Beylix** `rim-penglai-r1-02` (Penglai): geo=2 json=5
  - extra neighbors: `rim-cortex-relay-2-r1-11`, `rim-penglai-r1-01`, `rim-space-r1-10`
- **rim-space-r1-10** `rim-space-r1-10` (Rim Space): geo=4 json=6
  - extra neighbors: `rim-kalidasa-r4-09`, `rim-penglai-r1-02`
- **rim-space-r1-27** `rim-space-r1-27` (Rim Space): geo=2 json=3
  - extra neighbors: `border-georgia-r3-07`
- **rim-space-r2-10** `rim-space-r2-10` (Rim Space): geo=1 json=3
  - extra neighbors: `rim-kalidasa-r4-08`, `rim-penglai-r1-01`

