# Firefly.UI — Hotseat Blazor play surface

Map-centric hotseat shell for Lightning Bug. Server owns `Firefly.Core` / `GameState`. SignalR lobby rooms can wrap the same `IGameSession` later.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Board art at `reference/board/GameBoardMarkup.png` (already in repo)
- Map data at `Data/Map/SectorLayout.json`

## Run locally

From the repo root:

```bash
dotnet run --project src/Firefly.UI
```

Then open the URL printed by Kestrel (default `http://localhost:5027`).

Or:

```bash
cd src/Firefly.UI
dotnet run
```

HTTPS profile: `dotnet run --launch-profile https`

## What you get (P1–P4)

- Pan/zoom Verse map (scroll zoom, drag empty space / middle-mouse / Space+drag, `0` to fit)
- Sector hit-test + inspect panel
- Ship token at sector centroid (1-player smoke setup); moves when sector changes
- Alliance Cruiser / Cutter / Corvette markers when present
- Scenario chip: King of All Londinium · First Time in the Captain's Chair · Awful Lonely in the Big Black
- Optional expansion toggles (Blue Sun / PBH / Corvette) — restart the hotseat game
- Compact status strip (cash / fuel / parts / warrants / actions / pending) — ship name opens the sheet
- **Own Ship Sheet** (left drawer) — hold/stash grids (kernel `HoldSpace` packing), drive + upgrades, crew + gear, active jobs + job hand, cash / warrants / Solid / Haven; ship card + Option B portraits when served from `reference/Cards/`
- **Bottom action bar** — Fly / Deal / Work / Buy / Shore Leave / Crew / A Better Offer, legality via kernel `CanTakeAction` (blocked while pending)
- **End Turn** on the action bar with 0, 1, or 2 actions used (GF9 BBG p.6 “may take 2 Actions”)
- **PendingChoice host** — sticky priority banner + center modal; kind → widget registry; Nav reveal/resolve; submit via `TrySubmitChoice` / Nav resume
- **Fly lean loop** — Mosey (adjacent highlight → click) · Full Burn (path preview within drive range / fuel via kernel) · Nav reveal → option resolve

## Not yet

- Discard browsers (P5)
- Deal / Buy / Shore Leave surfaces (P6)
- Every exotic Nav edge-case widget (happy path + PendingChoice fallback)
- Rival public sheets (P8)
- Multi-seat SignalR lobbies (session API is shaped for it)

## Art drops (ask Christopher)

Preferred commit paths for scans/tokens:

```
reference/cards/crew/
reference/cards/gear/
reference/cards/jobs/
reference/cards/nav/
reference/cards/misbehave/
reference/cards/contacts/
reference/cards/supply/
reference/cards/ships/
reference/cards/leaders/
reference/cards/scenarios/
reference/tokens/
```

Do not invent card faces in the UI.
