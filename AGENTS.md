# Firefly / Lightning Bug — agent instructions

Canonical repo: `chrismrau/LightningBugBoardGame` (branch `main`).
This is a rules-faithful digital kernel of Gale Force Nine *Firefly: The Game* plus expansions. Inventing a cleaner rule is a bug.

## Non-negotiable: read official text first

Before implementing or changing any game rule, open the printed source in `Documents/`. Do not implement from memory, from training data, or from a prior chat.

Primary sources, in the order to consult when they conflict:

1. `Documents/Firefly_FAQ_v4.1.pdf`
2. `Documents/Firefly_Rules_Director's_Cut_03-05-26.pdf`
3. `Documents/GF9_Firefly_Rulebook_BBG.pdf`
4. Expansion books in `Documents/` (Blue Sun, Kalidasa, Pirates & Bounty Hunters)
5. `Documents/Firefly_FAQ_v2.pdf`, `Documents/Firefly_v5.pdf`, `Documents/cheatsheet.pdf`, reference sheets

Also use:

- `Documents/Supplies.tsv` — authoritative supply counts and printed fields
- `Documents/Bounties.tsv` — bounty card reference
- `Documents/AllianceAlert.tsv` — Alliance Alert reference

Quote the relevant sentence in the commit or PR description when a rule is non-obvious.

## Stack and layout

- Kernel: C# / .NET Standard 2.1 in `src/Firefly.Core`
- Tests: xUnit in `src/Firefly.Core.Tests`
- Solution: `Firefly.sln`
- Card JSON: `Data/Cards/`
- Map JSON: `Data/Map/Sectors.json`, `Data/Map/Adjacency.json`
- Official PDFs / TSVs: `Documents/`
- Reference art: `reference/`
- Compact of work already shipped: `project_history.md`

On case-sensitive filesystems use the GitHub casing (`Data/`, `Documents/`, `src/`). Some working copies use `data/`. Do not “fix” path casing as a drive-by change.

Build / test:

```bash
dotnet test Firefly.sln
```

Do not commit `bin/`, `obj/`, or IDE junk. Follow `.gitignore`.

## How to change code

- Match existing style in the file you touch. No new frameworks, DI containers, or source generators unless asked.
- Prefer small, test-backed changes. Add or extend a fact in the matching `*Tests.cs` file for every new rule branch.
- Do not implement a rule “approximately.” If the book is ambiguous, stop and say so; do not guess.
- Do not rename public types, card ids, or sector ids to look nicer.
- Gun Hand and Head Goon stay one card definition with copies. Keyword `:D` and skill `2D` encoding is accepted.
- Leaders cannot be killed or dismissed. A kill Disgruntles the Leader. A second Disgruntle fires the rest of the crew and clears the Leader token. Regular crew Jump Ship on a second Disgruntle.
- Do not invent planets, contacts, or dropoff sites that are not in the data files.

## Turn and actions

Official turn: two actions, no repeated action type.

Implemented action types: Fly, Nav, Crew, Deal, Work, Buy, Shore Leave, Misbehave.

- Shore Leave counts as a **Buy** action: planet sector, $100 per crew including the Leader, clear all Disgruntled. Cannot Buy and Shore Leave in the same turn.
- A Better Offer is **not** an action. On your turn, same sector: pay hiring cost to the bank; the Disgruntled crew jumps ship and loses the token. Leaders cannot be poached. FAQ 4.1 p.1 / GF9 p.15–17.

`GameSetup.Create` wires decks, starting supplies/jobs, a unique starting Leader, and a unique starting ship (id or name). Omitted seats get the next core Firefly III.

## Hold, crew, and ships

- Each cargo/stash space holds 1 cargo, contraband, passenger, or fugitive, **or** up to 2 fuel/parts mixed.
- Jetwash and Esmeralda have 6 fuel-only stash spaces (1 fuel each).
- `HoldSpace` enforces Buy and Work packing.
- Leader counts toward ship `MaxCrew`.
- Bound fugitives ignore crew/hold limits.

## Movement and drives

- Drive range comes from the Drive Core card. Mark I = 5 if unstated.
- Full Burn uses `EffectiveDriveRange`.
- Locked cores cannot be replaced.
- Interceptor Optimal Spec: −1 range per installed upgrade.
- Echelon LR-8 and Mark III need no fuel to initiate Full Burn.

## Map facts that have already bitten us

- 155 sectors, 397 adjacency edges.
- Santo: `alliance-qin-shi-huang-r1-01`
- Heinlein r1-01 Triumph (no supply); r1-02 Silverhold (supply)
- Muir: `rim-blue-sun-r2-03`
- Deadwood: `rim-blue-sun-r3-04`
- White Sun r4-02 is **not** adjacent to r4-03; Lux sits between them (intentional).
- Triumph is listed as a Bandits dropoff on some bounty text but is not a planet in the current `Sectors.json`. Do not invent it.

## Win check and turn cycle

- `WinCheck` evaluates Story Card win types from `Data/Cards/ScenarioCards.json`.
- Auto-claims solid/cash goals.
- Travel-pay wins go through `TryFinishTravelPay`.
- `EndTurn` runs EndOfTurn then StartOfTurn checks.
- Haven defaults to the starting sector.
- Alliance Cruiser starts at Londinium.

## Bounties (Pirates & Bounty Hunters)

Sources: `Documents/Bounties.tsv` + PBH rulebook. Data: `Data/Cards/Bounties.json` (20 cards).

- Setup wires `BountyDeck` with 3 face-up Most Wanted.
- Work — Confrontation: board 6, then showdown.
- Work — Lone Target: showdown vs best skill.
- Work — Betrayal: no roll; crew except Leader become Disgruntled.
- Deliver at dropoff: printed $ × stack + Lawman bonuses. Immoral pay Disgruntles moral crew. Both cards leave the game.
- Jump = board + showdown. Rescue hires free and returns the bounty to the bottom.
- Alliance Cruiser Nav cycles the Wanted List.
- Cortex Alerts stack same-class crew (Enforcer / Scrapper / Bandit).
- `BotchKill` from the card kills that many attacker crew.

## Alliance Alerts

Kernel is on `main`: deck on `GameState`, `ActiveAlertRules`, hooks in Deal / Work / Misbehave / Buy / Shore Leave / Cruiser boarding / Cruiser Nav. Tests live in `src/Firefly.Core.Tests/AllianceAlertTests.cs`.

- Privilege Suspension uses counted Solid in `WinCheck`.
- Misbehave “Alliance Alert!” cycles the alert deck.
- Do not rename printed alert titles.

## Data authority

Printed TSV/PDF wins over JSON when they disagree — unless the JSON difference is a documented encoding (`:D`, `2D`, copy counts). `Documents/Supplies.tsv` is the supply source of truth. `Crew.json`, `Gear.json`, `ShipUpgrades.json`, `Leaders.json`, and `DriveCores.json` should match it.

## What not to do

- Do not add READMEs, CI, Unity, networking, or a UI unless asked.
- Do not “simplify” two-action turns, Solid/Wanted, Disgruntle, or packing rules.
- Do not treat chat history or this file as a substitute for `Documents/` when coding a new rule.
- Do not push secrets. There should be none in this repo.

## When you are unsure

Name the rule, name the file in `Documents/` you would open, and wait. Wrong-but-compiling Firefly is worse than an unanswered question.
