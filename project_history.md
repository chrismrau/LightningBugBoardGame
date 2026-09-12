# Project history — Lightning Bug / Firefly kernel

Compact of work done in the Grok project and on `chrismrau/LightningBugBoardGame` through 2026-09-12. This is not a chat export. Standing rules live in `AGENTS.md`. Read that first. Read `Documents/` before coding any rule.

Repo created 2026-08-23. Kernel language: C# .NET Standard 2.1. Tests: xUnit. Solution: `Firefly.sln`.

## What this project is

A rules-faithful digital kernel of Gale Force Nine *Firefly: The Game* plus expansions (Blue Sun, Kalidasa, Pirates & Bounty Hunters, Crime & Punishment Alliance Alerts). No Unity UI yet. No network. Inventing a cleaner rule is a bug.

## Layout

| Path | Role |
|---|---|
| `src/Firefly.Core` | Kernel (`Actions`, `Cards`, `Data`, `Map`, `Movement`, `State`) |
| `src/Firefly.Core.Tests` | One test file per subsystem |
| `Data/Cards/` | Card JSON (jobs, crew, gear, nav, misbehave, bounties, alerts, scenarios, …) |
| `Data/Map/` | `Sectors.json`, `Adjacency.json` |
| `Documents/` | Official PDFs + `Supplies.tsv`, `Bounties.tsv`, `AllianceAlert.tsv` |
| `reference/` | Card and board scans |
| `AGENTS.md` | Standing instructions for Cursor / other agents |
| `grok/.grok/project_memory.md` | Stale one-liner on GitHub; do not treat as current |

Working copies sometimes use `data/` instead of `Data/`. Use GitHub casing on case-sensitive disks.

## Timeline of what shipped

### Map and data (late Aug – 5 Sep 2026)

- 155 sectors, 397 adjacency edges.
- Sector schema ~3.3, adjacency schema ~4.144.
- White Sun r4-02 is **not** adjacent to r4-03; Lux sits between them (intentional).
- Santo = `alliance-qin-shi-huang-r1-01`.
- Muir = `rim-blue-sun-r2-03`. Deadwood = `rim-blue-sun-r3-04`.
- Heinlein labeled 2026-09-05: r1-01 Triumph (no supply), r1-02 Silverhold (supply).
- Card JSON aligned to `Documents/Supplies.tsv` (counts and printed fields). Gun Hand / Head Goon remain one card with copies. Keyword `:D` and skill `2D` encoding accepted.

### Core turn and setup (through 4 Sep 2026)

- Official turn: two actions, no repeated action type.
- Actions in: Fly, Nav, Crew, Deal, Work, Buy, Shore Leave, Misbehave.
- Shore Leave is a **Buy** action (not its own `TurnAction`): planet sector, $100 per crew including the Leader, clear all Disgruntled. Cannot Buy and Shore Leave the same turn.
- `GameSetup.Create` wires decks, starting supplies/jobs, a unique Leader per seat, a unique starting ship (id or name). Omitted seats get the next core Firefly III.
- Leaders cannot be killed or dismissed. A kill Disgruntles them. A second Disgruntle fires the rest of the crew and clears the Leader token. Regular crew Jump Ship on a second Disgruntle.
- Seated-leader named crew copies are stripped from supply at setup. Deceptive trio rule runs after buying crew (`RemoveNamedIf`).

### Hold and crew limits (4 Sep 2026)

- Each cargo/stash space: 1 cargo, contraband, passenger, or fugitive, **or** up to 2 fuel/parts mixed.
- Jetwash and Esmeralda: 6 fuel-only stash spaces, 1 fuel each.
- `HoldSpace` / `UsedHolds` enforce Buy and Work. Reject pickups that will not fit.
- Leader counts toward ship `MaxCrew`.
- Bound fugitives ignore crew/hold limits.

### A Better Offer (4 Sep 2026)

- Not an action. On your turn, same sector: pay hiring cost to the bank; Disgruntled crew jumps ship and loses the token.
- Leaders cannot be poached.
- Source: FAQ 4.1 p.1 / GF9 p.15–17. `Roster.TryAccept` for the poached crew.

### Win check (4 Sep 2026)

- `WinCheck` evaluates Story Card win types from `Data/Cards/ScenarioCards.json`.
- Tracks haven, goal tokens, completed Story goals, winner on `GameState`.
- Auto-claims solid/cash goals.
- Travel-pay wins go through `TryFinishTravelPay`.
- `EndTurn` runs EndOfTurn then StartOfTurn checks.
- Haven defaults to the starting sector.

### Drive cores (4 Sep 2026)

- Range comes from the Drive Core card. Mark I = 5 if unstated.
- Starting ship core installed at setup. Bought cores install on Buy; locked cores cannot be replaced.
- Full Burn uses `EffectiveDriveRange`.
- Interceptor Optimal Spec: −1 range per installed upgrade.
- Echelon LR-8 and Mark III need no fuel to initiate Full Burn.

### Bounties / Pirates & Bounty Hunters (5 Sep 2026)

- Sources: `Documents/Bounties.tsv` + PBH rulebook. Data: `Data/Cards/Bounties.json` (20 cards).
- `BountyCatalog`, `BountyDeck`, 3 face-up Most Wanted. Bound list on `PlayerState`.
- Work: Confrontation (board 6 then showdown), Lone Target (showdown vs best skill), Betrayal (no roll; crew except Leader Disgruntled).
- Deliver at dropoff: printed $ × stack + Lawman bonuses. Immoral pay Disgruntles moral crew. Both cards leave the game.
- Jump = board + showdown. Rescue hires free and returns the bounty to the bottom.
- Alliance Cruiser Nav cycles the Wanted List.
- Cortex Alerts stack same-class crew (Enforcer / Scrapper / Bandit).
- `BotchKill` from the card kills that many attacker crew.
- Triumph is listed as a Bandits dropoff on some bounty text but is **not** a planet in current `Sectors.json`. Do not invent it.

### Alliance Alerts / Crime & Punishment (10 Sep 2026)

- Catalog, deck on `GameState`, `ActiveAlertRules`, `GameSetup` + `StartingAlertCard` on scenarios.
- Hooks: Deal (Alliance Audit, Enhanced Inspection), Work (Persons of Interest, Criminal Activity, Alliance Audit), Buy (White Sun / Criminal Sighting), Misbehave (“Alliance Alert!” cycles the deck), Shore Leave, Cruiser boarding, Cruiser Nav (cycle).
- Privilege Suspension uses counted Solid in `WinCheck`.
- Printed names corrected (Privilege Suspension; remove one Wanted).
- Tests: `src/Firefly.Core.Tests/AllianceAlertTests.cs`.
- Alliance Cruiser starts at Londinium (setup + test).

### Agent handoff (12 Sep 2026)

- `AGENTS.md` added on `main` (`e69ce33`) so Cursor and other agents load the same constraints.

## Test files on main

`AllianceAlertTests`, `BountyTests`, `BuyTests`, `CrewRosterTests`, `DealTests`, `DeceptiveCrewTests`, `DriveCoreTests`, `FlyTests`, `HoldAndCrewLimitTests`, `MapTests`, `MisbehaveTests`, `MovementTests`, `NavTests`, `PoachTests`, `SetupAndScenarioTests`, `ShoreLeaveTests`, `SkillAndEncounterTests`, `WinCheckTests`, `WorkTests`.

`dotnet test Firefly.sln` is the gate.

## Open / watch items

- No Unity / UI / multiplayer layer yet. Do not add one unless asked.
- Triumph-as-dropoff vs missing planet in `Sectors.json` is an unresolved data mismatch, not a license to add a sector.
- GitHub `grok/.grok/project_memory.md` is outdated; this file and `AGENTS.md` supersede it.
- Always re-read `Documents/` for the next rule. This history is a map of *what we already built*, not a substitute for the books.

## How to continue in Cursor

1. Clone or pull `chrismrau/LightningBugBoardGame` `main`.
2. Keep `AGENTS.md` in context.
3. Implement the next printed rule with a matching test.
4. Do not re-litigate hold packing, Shore Leave-as-Buy, Leader Disgruntle, or “read the FAQ first.”
