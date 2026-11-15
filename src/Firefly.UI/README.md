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

## What you get (P1–P9)

- Pan/zoom Verse map (scroll zoom, drag empty space / middle-mouse / Space+drag, `0` to fit)
- Sector hit-test + inspect panel
- Ship token at sector centroid (1-player smoke setup); moves when sector changes
- Alliance Cruiser / Cutter / Corvette markers when present
- Scenario chip: King of All Londinium · First Time in the Captain's Chair · Awful Lonely in the Big Black
- Optional expansion toggles (Blue Sun / PBH / Corvette) — restart the hotseat game
- **Expansions chrome (P9)** — Most Wanted face-up (Crew portrait reuse) · Alliance Alert text card · Corvette sector chip · Blue Sun Alert Token chits on map (sprite logos)
- Compact status strip (cash / fuel / parts / warrants / actions / pending) — ship name opens the sheet
- **Own Ship Sheet** (left drawer) — hold/stash grids (kernel `HoldSpace` packing), drive + upgrades, crew + gear, active jobs + job hand, cash / warrants / Solid / Haven; ship card + Option B portraits when served from `reference/Cards/`
- **Bottom action bar** — Fly / Deal / Work / Buy / Shore Leave / Crew / A Better Offer, legality via kernel `CanTakeAction` (blocked while pending)
- **End Turn** on the action bar with 0, 1, or 2 actions used (GF9 BBG p.6 “may take 2 Actions”)
- **PendingChoice host** — sticky priority banner + center modal; kind → widget registry; Nav reveal/resolve; submit via `TrySubmitChoice` / Nav resume
- **Fly lean loop** — Mosey (adjacent highlight → click) · Full Burn (path preview within drive range / fuel via kernel) · Nav reveal → option resolve
- **Discard browser** (right drawer) — Supply (per-planet) + Contact Job discards + Nav; inspect-only; Option B plates / Contact full scans; deep-link keys for Buy/Deal
- **Deal** — planet/remote Contact picker; consider staging → keep up to 2; take from public discard; sell goods / Badger warrant wipe when legal; `DealSellParts` via PendingHost
- **Buy** — face-up Supply at current planet + Fuel/Parts; packing/prices via kernel `BuyAction`; deep-links Supply discard browser
- **Shore Leave** — consumes Buy action type; $100/crew incl. Leader; clears Disgruntled (kernel `ShoreLeaveAction`)
- **A Better Offer** — non-action poach when same-sector Disgruntled rival crew is legal (`PoachAction`)
- **Work** — legal Job / Make-Work / Datascope / Goal / **bounty** picker (`WorkAction` / `GoalWorkAction` / `BountyAction`); Job activates on first Work (FAQ 4.1 p.5); PBH Confront / Lone / Betray / Deliver / Jump when Most Wanted is on
- **Misbehave** — PendingHost reveal/resolve (text chrome); Ace + option buttons; resume via `MisbehaveResolver` / `MisbehaveChoiceResume`
- **Goal Work** — continue boarding/skill after Misbehave via PendingHost
- **Encounters / Alert Tokens** — PendingHost resolve for Cruiser boarding + Corvette Contact; Blue Sun Alert Tokens via `AlertTokenResolver` (choices resume in session)
- **Rival public sheets** (right peeks) — Active Jobs + board resources; Job hand hidden (prefs visibility lock). Own left sheet still shows Job hand. Set **Seats → 2P** on the scenario chip for smoke; End Turn swaps whose sheet is private.
- Right-edge polish: opening a rival peek closes Discards (and vice versa)
- Bound Bounties on ship sheet (own + rival)

## Smoke — expansions

1. `dotnet run --project src/Firefly.UI`
2. **PBH / Most Wanted:** toggle **PBH / Bounties** on the scenario chip → strip shows 3 face-up Wanted; Work lists Confront/Lone/Betray/Deliver/Jump when legal.
3. **Alliance Alert:** pick scenario **Wanted Men (starting Alliance Alert)** → Active Alert text card in the expansions strip. Misbehave “Alliance Alert!” / Cruiser Nav cycle via kernel.
4. **Corvette:** toggle **Corvette** → marker at Cortex Relay 2; Outlaw sharing sector → PendingHost Corvette Contact.
5. **Alert Tokens:** toggle **Blue Sun** → permanent Reaver Space chits (sprite logos); pending Alert Token sectors resolve in PendingHost.

## Not yet

- Every exotic Misbehave / Goal branch widget (happy path + PendingChoice coverage; gaps in `internal/ui-p7-status.md`)
- Reaver Contact evade-sector picker; bounty skill/boarding pick chrome (auto best-skill); Jump kill victim polish — see `internal/ui-p9-status.md`
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
