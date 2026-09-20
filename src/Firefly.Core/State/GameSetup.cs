using System;
using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;

namespace Firefly.Core.State
{
    public sealed class PlayerSeat
    {
        public string Id { get; }
        public string Name { get; }
        public string SectorId { get; }
        public string? ShipId { get; }
        public string? LeaderId { get; }

        public PlayerSeat(string id, string name, string sectorId, string? shipId = null, string? leaderId = null)
        {
            Id = id;
            Name = name;
            SectorId = sectorId;
            ShipId = shipId;
            LeaderId = leaderId;
        }
    }

    /// <summary>
    /// One pick in The Browncoat Way snake draft: purchase a Ship <em>or</em> select a Leader.
    /// SetupCards.json: each turn chooses Leader OR Ship; last player takes both before reverse.
    /// </summary>
    public sealed class BrowncoatDraftPick
    {
        public string PlayerId { get; }
        public string? ShipId { get; }
        public string? LeaderId { get; }

        public BrowncoatDraftPick(string playerId, string? shipId = null, string? leaderId = null)
        {
            PlayerId = playerId;
            ShipId = shipId;
            LeaderId = leaderId;
        }

        public static BrowncoatDraftPick Ship(string playerId, string shipId) =>
            new BrowncoatDraftPick(playerId, shipId: shipId);

        public static BrowncoatDraftPick Leader(string playerId, string leaderId) =>
            new BrowncoatDraftPick(playerId, leaderId: leaderId);
    }

    /// <summary>
    /// Optional post-draft Fuel / Parts purchase (SetupCards.json: Fuel $100, Parts $300).
    /// </summary>
    public sealed class BrowncoatResourceBuy
    {
        public int Fuel { get; set; }
        public int Parts { get; set; }
    }

    public sealed class GameSetupOptions
    {
        public string SetupCardId { get; set; } = "setup_standard";
        public string? ScenarioCardId { get; set; }
        public IRng? Rng { get; set; }
        public bool DealStartingJobs { get; set; } = true;
        /// <summary>
        /// Override Priming the Pump reveal count. Null uses the Setup card
        /// (<c>primeSupplyReveal</c>, default 3; The Blitz Double Dip uses 6).
        /// </summary>
        public int? PrimeSupplyReveal { get; set; }
        /// <summary>
        /// The Blitz Strip Mining: which Supply planet deck to draft from.
        /// Required when the Setup card has <c>stripMineOneSupplyDeck</c>.
        /// </summary>
        public string? StripMinePlanet { get; set; }
        /// <summary>
        /// Seat index of the player who won the Choose Ships &amp; Leaders roll and
        /// therefore claims the Dinosaur first (SetupCards.json Strip Mining text).
        /// Defaults to 0 (first seat).
        /// </summary>
        public int DinosaurStartIndex { get; set; }
        /// <summary>
        /// Optional thin multiplayer claim order: Supply card ids in draft sequence
        /// (playerCount rounds × playerCount picks). Null auto-claims the first
        /// claimable revealed card for each pick.
        /// </summary>
        public IList<string>? StripMineClaims { get; set; }
        /// <summary>
        /// The Browncoat Way: seat index of the highest dice roll — starts the snake draft
        /// (SetupCards.json Choose Ships and Leaders). Defaults to 0.
        /// </summary>
        public int BrowncoatDraftStartIndex { get; set; }
        /// <summary>
        /// Ordered picks for the full snake-draft loop (2 × playerCount picks).
        /// Null auto-picks from each seat's ShipId / LeaderId (ship preferred on the
        /// forward pass when both remain).
        /// </summary>
        public IList<BrowncoatDraftPick>? BrowncoatDraftPicks { get; set; }
        /// <summary>
        /// Optional Fuel/Parts buys after the draft, keyed by player id.
        /// Null = nobody buys (printed "may buy").
        /// </summary>
        public IDictionary<string, BrowncoatResourceBuy>? BrowncoatResourceBuys { get; set; }
        /// <summary>
        /// Optional hook after ships/leaders are seated (and leader crew stripped from
        /// Supply) but before Strip Mining / Priming. Tests use this to stack draft cards.
        /// </summary>
        public Action<GameState>? AfterLeadersHired { get; set; }
        /// <summary>
        /// Blue Sun / Kalidasa: place the Operative's Corvette. Off for core-only setup.
        /// </summary>
        public bool UseOperativesCorvette { get; set; }

        /// <summary>
        /// Pirates &amp; Bounty Hunters: Bounty Deck + Most Wanted List.
        /// Optional — not required for basic play (PBH p.3 expanding the 'Verse).
        /// Also enables <see cref="UsePiratesBountyHunters"/> for piracy Work.
        /// </summary>
        public bool UseBountyDeck { get; set; }

        /// <summary>
        /// Pirates &amp; Bounty Hunters: piracy Jobs (Any Rival boarding + showdown).
        /// Optional expansion flag. When set, also wires the Bounty Deck unless already off.
        /// </summary>
        public bool UsePiratesBountyHunters { get; set; }

        /// <summary>
        /// Blue Sun: three Reaver Cutters on Burnham r2. Core: one Cutter in Border Space.
        /// </summary>
        public bool UseBlueSun { get; set; }

        /// <summary>
        /// Blue Sun physical Alert Tokens. Defaults to on when <see cref="UseBlueSun"/> is set;
        /// set to false for Setup cards that disable Alert Tokens (e.g. Clearer Skies).
        /// </summary>
        public bool? UseAlertTokens { get; set; }

        /// <summary>
        /// Any Port / chooseHavens stories: player id → Haven sector id.
        /// When a seat is omitted and chooseHavens is required, that seat suspends via
        /// <see cref="PendingChoiceKinds.HavenSector"/> after Create (Blue Sun Choosing Havens).
        /// Scripted entries still apply immediately (tests / headless setup).
        /// </summary>
        public IDictionary<string, string>? HavenChoices { get; set; }
    }

    /// <summary>
    /// Builds a playable GameState: catalogs, Nav / Contact / Misbehave decks,
    /// and one Supply market per planet with the top cards face up.
    /// Starting cash, fuel, parts, and optional starting jobs come from the Setup card.
    /// Each seat picks a unique starting ship (by id or printed name); omitted
    /// seats receive the next unused core Firefly III.
    /// </summary>
    public static class GameSetup
    {
        public static readonly string[] StandardStartingContacts =
        {
            "Harken", "Badger", "Amnon Duul", "Patience", "Niska"
        };

        public static readonly string[] CoreSupplyPlanets =
        {
            "Persephone", "Osiris", "Regina", "Silverhold", "Space Bazaar"
        };

        /// <summary>
        /// GF9 Firefly Rulebook p.3: Alliance Cruiser starts at Londinium.
        /// </summary>
        public const string AllianceCruiserStartSectorId = "alliance-white-sun-r1-02";

        /// <summary>
        /// Blue Sun / Kalidasa: Operative's Corvette starts at Cortex Relay 2
        /// when that ship is in the game.
        /// </summary>
        public const string OperativeCorvetteStartSectorId = "rim-cortex-relay-2-r1-11";

        /// <summary>
        /// GF9 core: single Reaver Cutter starts in Border Space.
        /// </summary>
        public const string CoreReaverCutterStartSectorId = "border-space-r2-06";

        /// <summary>
        /// Blue Sun: one Reaver Cutter in each Burnham ring-2 sector.
        /// </summary>
        public static readonly string[] BlueSunReaverCutterStartSectorIds =
        {
            "rim-burnham-r2-01",
            "rim-burnham-r2-02",
            "rim-burnham-r2-03"
        };

        public static GameState Standard(params PlayerSeat[] seats) =>
            Create(seats, new GameSetupOptions { SetupCardId = "setup_standard" });

        public static GameState Create(IReadOnlyList<PlayerSeat> seats, GameSetupOptions? options = null)
        {
            if (seats == null || seats.Count == 0)
                throw new ArgumentException("At least one player seat is required.", nameof(seats));

            options ??= new GameSetupOptions();
            var rng = options.Rng ?? new SystemRng();

            var setups = SetupCatalog.LoadDefault();
            if (!setups.Cards.TryGetValue(options.SetupCardId, out var setup))
                throw new ArgumentException($"Unknown setup card '{options.SetupCardId}'.", nameof(options));

            ScenarioCard? scenario = null;
            if (!string.IsNullOrWhiteSpace(options.ScenarioCardId))
            {
                var scenarios = ScenarioCatalog.LoadDefault();
                scenario = scenarios.Get(options.ScenarioCardId);
            }

            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var cash = setup.StartingCash ?? 3000;
            var fuel = setup.StartingFuel ?? 6;
            var parts = setup.StartingParts ?? 2;

            var players = new List<PlayerState>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var seat in seats)
            {
                if (seat == null || string.IsNullOrWhiteSpace(seat.Id))
                    throw new ArgumentException("Each seat needs an id.");
                if (!seen.Add(seat.Id))
                    throw new ArgumentException($"Duplicate player id '{seat.Id}'.");
                if (!map.TryGet(seat.SectorId, out _))
                    throw new ArgumentException($"Unknown starting sector '{seat.SectorId}'.");

                players.Add(new PlayerState(
                    seat.Id,
                    string.IsNullOrWhiteSpace(seat.Name) ? seat.Id : seat.Name,
                    seat.SectorId,
                    fuel: fuel,
                    parts: parts,
                    cash: cash,
                    // Browncoat purchases ships in the snake draft; seat.ShipId is only a pick preference.
                    shipId: setup.PurchaseShipsFromBank ? null : seat.ShipId));
            }

            var corvette = options.UseOperativesCorvette ? OperativeCorvetteStartSectorId : null;
            var reavers = options.UseBlueSun
                ? BlueSunReaverCutterStartSectorIds
                : new[] { CoreReaverCutterStartSectorId };
            var game = new GameState(
                map,
                players,
                new MapTokens(AllianceCruiserStartSectorId, reavers, corvette))
            {
                Setup = setup,
                Scenario = scenario,
                UseAlertTokens = options.UseBlueSun && options.UseAlertTokens != false,
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Leaders = LeaderCatalog.LoadDefault(),
                Ships = ShipCatalog.LoadDefault(),
                DriveCores = DriveCoreCatalog.LoadDefault(),
                Gear = GearIndex.LoadDefault(),
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault(),
                Supply = SupplyCatalog.LoadDefault()
            };

            game.Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, rng);
            // GF9 p.4 / FAQ 4.1 p.1 / SetupCards.json navReshuffle:
            // Standard: place RESHUFFLE (Alliance Cruiser / Reaver Cutter) in discard for 3+ players.
            game.Decks.ApplyReshuffleSetup(setup, players.Count);
            // Always load Bounties.json as authority so Jobs.json duplicates stay out of Contact decks.
            game.Bounties = BountyCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, rng, game.Bounties);
            // Supply: shuffle only; Strip Mining drafts from the draw pile before Priming the Pump.
            game.SupplyDecks = SupplyDecks.FromCatalog(game.Supply, rng);
            var misbehave = MisbehaveCatalog.LoadDefault();
            game.MisbehaveCatalog = misbehave;
            game.Misbehave = MisbehaveDeck.FromCatalog(misbehave, rng);
            // PBH p.8: Bounty Cards form a separate deck; reveal top 3 Most Wanted
            // (optional expansion via UseBountyDeck / UsePiratesBountyHunters).
            var pbh = options.UsePiratesBountyHunters || options.UseBountyDeck;
            game.UsePiratesBountyHunters = pbh;
            if (pbh)
                game.BountyDeck = BountyDeck.FromCatalog(game.Bounties, rng);
            game.AllianceAlerts = AllianceAlertCatalog.LoadDefault();
            game.AllianceAlertDeck = AllianceAlertDeck.FromCatalog(game.AllianceAlerts, rng);
            if (setup.StartingAlertCard || (scenario != null && scenario.StartingAlertCard))
                game.AllianceAlertDeck.DrawAndActivate();

            if (setup.PurchaseShipsFromBank)
            {
                // SetupCards.json The Browncoat Way: snake-draft purchase ships / choose leaders,
                // then optional Fuel ($100) and Parts ($300). No free starting Fuel/Parts.
                ApplyBrowncoatSnakeDraft(game, seats, options);
                ApplyBrowncoatResourceBuys(game, setup, options);
            }
            else
            {
                AssignStartingShips(game, seats);
                HireStartingLeaders(game, seats);
            }
            options.AfterLeadersHired?.Invoke(game);

            if (setup.StripMineOneSupplyDeck)
                ApplyStripMining(game, options);

            if (options.DealStartingJobs)
                DealStartingJobs(game);

            // GF9 / Director's Cut Priming the Pump: reveal top N into discard, then deal FaceUp.
            // SetupCards.json Blitz: "Reveal the top 6 cards of each Supply deck. Place the
            // revealed cards in their discard piles."
            var primeCount = options.PrimeSupplyReveal ?? setup.PrimeSupplyReveal;
            PrimeSupplyDecks(game.SupplyDecks, primeCount);

            if (setup.GameLengthTokenCount is int tokenCount)
            {
                // SetupCards.json Time's Not: pile of Disgruntled Tokens as Game Length Tokens,
                // held by the player taking the first turn.
                game.GameLengthTokensRemaining = tokenCount;
                game.FirstPlayerIndex = game.CurrentPlayerIndex;
                game.BeginOpeningTurn();
            }

            // Scenario setup (Any Port chooseHavens / warrants / Alliance Alert Tokens).
            ApplyScenarioSetup(game, seats, options);

            return game;
        }

        /// <summary>
        /// ScenarioCards.json Any Port in a Storm (and other chooseHavens stories):
        /// Choose Havens, starting warrants, Blue Sun Alliance Alert Tokens on non-Haven
        /// Alliance planets. Tokens are physical Alert Tokens — not the C&amp;P Alert deck.
        /// </summary>
        private static void ApplyScenarioSetup(
            GameState game,
            IReadOnlyList<PlayerSeat> seats,
            GameSetupOptions options)
        {
            var scenario = game.Scenario;
            if (scenario == null)
                return;

            if (scenario.ChooseHavens is { Required: true })
                ApplyChooseHavens(game, seats, options);

            if (scenario.StartingWarrants > 0)
            {
                foreach (var player in game.Players)
                    player.Warrants = scenario.StartingWarrants;
            }

            // Alliance Alert Tokens need every Haven placed first (non-Haven planets only).
            if (scenario.AllianceAlertTokensOnNonHavenAlliancePlanets
                && game.PendingChoice == null)
                PlaceAllianceAlertTokensOnNonHavenAlliancePlanets(game);
        }

        private static void ApplyChooseHavens(
            GameState game,
            IReadOnlyList<PlayerSeat> seats,
            GameSetupOptions options)
        {
            // PlayerState defaults Haven to the seat start sector; clear before validating uniqueness.
            foreach (var player in game.Players)
                player.HavenSectorId = "";

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var seat in seats)
            {
                var player = game.GetPlayer(seat.Id);
                if (options.HavenChoices == null
                    || !options.HavenChoices.TryGetValue(seat.Id, out var chosen)
                    || string.IsNullOrWhiteSpace(chosen))
                {
                    // Defer to PendingChoice (one seat at a time) after scripted seats apply.
                    continue;
                }

                var havenId = chosen;
                if (!HavenRules.IsEligibleHavenSector(game, havenId, out var error))
                    throw new ArgumentException(
                        $"Haven for '{seat.Id}': {error}",
                        nameof(options));
                if (!taken.Add(havenId))
                    throw new ArgumentException(
                        $"Haven sector '{havenId}' is already claimed.",
                        nameof(options));

                player.HavenSectorId = havenId;
                // Blue Sun: unless otherwise noted, ships start at their Haven.
                player.SectorId = havenId;
            }

            // Suspend for the first seat still missing a Haven; Alliance Alert Tokens wait
            // until every Haven is chosen (TryResumeHavenSector / ApplyScenarioSetup).
            if (!HavenRules.TrySuspendNextHavenPick(game, out var pendingError)
                && game.PendingChoice == null)
            {
                throw new ArgumentException(
                    pendingError ?? "Haven selection failed.",
                    nameof(options));
            }
        }

        private static void PlaceAllianceAlertTokensOnNonHavenAlliancePlanets(GameState game) =>
            HavenRules.PlaceAllianceAlertTokensOnNonHavenAlliancePlanets(game);

        private static void PrimeSupplyDecks(SupplyDecks? decks, int revealCount)
        {
            if (decks == null)
                return;
            foreach (var market in decks.Markets)
            {
                market.PrimeToDiscard(revealCount);
                market.Refill();
            }
        }

        /// <summary>
        /// The Blitz Strip Mining (SetupCards.json / printed Set Up card):
        /// Choose 1 Supply Deck. Dinosaur holder starts; each round reveal N cards
        /// (N = players), claim free leftward; pass Dinosaur; repeat until each player
        /// has had the Dinosaur first. Director's Cut p.47: free Supply Cards equal
        /// to the number of players.
        /// </summary>
        private static void ApplyStripMining(GameState game, GameSetupOptions options)
        {
            if (game.SupplyDecks == null)
                throw new InvalidOperationException("Supply decks are required for Strip Mining.");
            if (string.IsNullOrWhiteSpace(options.StripMinePlanet))
                throw new ArgumentException(
                    "StripMinePlanet is required when the Setup card strip-mines one Supply deck.",
                    nameof(options));
            if (!game.SupplyDecks.TryGet(options.StripMinePlanet, out var market))
                throw new ArgumentException(
                    $"Unknown Strip Mine Supply planet '{options.StripMinePlanet}'.",
                    nameof(options));

            var n = game.Players.Count;
            var dino = options.DinosaurStartIndex;
            if (dino < 0 || dino >= n)
                throw new ArgumentException(
                    $"DinosaurStartIndex {dino} is out of range for {n} players.",
                    nameof(options));

            var claimCursor = 0;
            var claims = options.StripMineClaims;

            // N rounds — each player gets the Dinosaur once (picks first that round).
            for (var round = 0; round < n; round++)
            {
                var revealed = new List<SupplyCard>(n);
                for (var i = 0; i < n; i++)
                {
                    if (!market.TryDraw(out var card))
                        throw new InvalidOperationException(
                            $"Strip Mining: Supply deck '{market.Planet}' ran out of cards.");
                    revealed.Add(card);
                }

                for (var pick = 0; pick < n; pick++)
                {
                    var playerIndex = (dino + pick) % n;
                    var player = game.Players[playerIndex];
                    SupplyCard? chosen = null;
                    if (claims != null)
                    {
                        if (claimCursor >= claims.Count)
                            throw new ArgumentException(
                                "StripMineClaims does not cover the full draft.",
                                nameof(options));
                        var wantId = claims[claimCursor++];
                        for (var i = 0; i < revealed.Count; i++)
                        {
                            if (string.Equals(revealed[i].Id, wantId, StringComparison.Ordinal))
                            {
                                chosen = revealed[i];
                                revealed.RemoveAt(i);
                                break;
                            }
                        }
                        if (chosen == null)
                            throw new ArgumentException(
                                $"StripMineClaims card '{wantId}' is not among the revealed cards.",
                                nameof(options));
                    }
                    else
                    {
                        // Auto-pick first claimable card (thin multiplayer / tests).
                        for (var i = 0; i < revealed.Count; i++)
                        {
                            if (CanClaimFree(game, player, revealed[i]))
                            {
                                chosen = revealed[i];
                                revealed.RemoveAt(i);
                                break;
                            }
                        }
                        if (chosen == null)
                            throw new InvalidOperationException(
                                $"Strip Mining: {player.Name} cannot claim any revealed card.");
                    }

                    if (!BuyAction.TryGiveSupply(game, player, chosen, market.Planet, out var error))
                        throw new InvalidOperationException(
                            $"Strip Mining: failed to give '{chosen.Id}' to {player.Name}: {error}");
                }

                // Pass the Dinosaur to the left.
                dino = (dino + 1) % n;
            }
        }

        private static bool CanClaimFree(GameState game, PlayerState player, SupplyCard card)
        {
            switch (card.Kind)
            {
                case SupplyKind.Crew:
                    return game.Crew != null
                        && game.Crew.TryGet(card.Id, out _)
                        && player.Roster.Count < player.Roster.MaxCrew;
                case SupplyKind.DriveCore:
                    if (game.DriveCores == null)
                        return false;
                    if (!game.DriveCores.TryResolve(card.Id, out _)
                        && game.DriveCores.FindByName(card.Name) == null)
                        return false;
                    if (!string.IsNullOrWhiteSpace(player.DriveCoreId)
                        && game.DriveCores.TryResolve(player.DriveCoreId, out var current)
                        && current.Locked)
                        return false;
                    return true;
                case SupplyKind.Gear:
                case SupplyKind.ShipUpgrade:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The Browncoat Way snake draft (SetupCards.json / printed Set Up card):
        /// Highest roller picks Leader OR purchases a Ship (list price). Leftward until
        /// the last player, who selects Leader and purchases a Ship. Then reverse rightward
        /// for remaining choices. Director's Cut p.47: list prices include starting upgrades.
        /// </summary>
        private static void ApplyBrowncoatSnakeDraft(
            GameState game,
            IReadOnlyList<PlayerSeat> seats,
            GameSetupOptions options)
        {
            if (game.Ships == null)
                throw new InvalidOperationException("Ship catalog is required for Browncoat ship purchase.");

            var n = seats.Count;
            var start = options.BrowncoatDraftStartIndex;
            if (start < 0 || start >= n)
                throw new ArgumentException(
                    $"BrowncoatDraftStartIndex {start} is out of range for {n} players.",
                    nameof(options));

            var turnPlayerIndexes = BuildBrowncoatTurnPlayerIndexes(n, start);
            var shipsTaken = new HashSet<string>(StringComparer.Ordinal);
            var leadersTaken = new HashSet<string>(StringComparer.Ordinal);
            var corePool = new Queue<ShipCard>();
            foreach (var ship in game.Ships.CoreStartingShips())
                corePool.Enqueue(ship);

            var picks = options.BrowncoatDraftPicks;
            var pickCursor = 0;
            if (picks == null)
            {
                picks = BuildAutoBrowncoatPicks(game, seats, turnPlayerIndexes, shipsTaken, corePool);
                shipsTaken.Clear();
                leadersTaken.Clear();
                corePool.Clear();
                foreach (var ship in game.Ships.CoreStartingShips())
                    corePool.Enqueue(ship);
                pickCursor = 0;
            }

            for (var turnIndex = 0; turnIndex < turnPlayerIndexes.Count; turnIndex++)
            {
                var playerIndex = turnPlayerIndexes[turnIndex];
                var seat = seats[playerIndex];
                var player = game.GetPlayer(seat.Id);
                var slots = PicksForBrowncoatTurn(turnIndex, n);
                for (var slot = 0; slot < slots; slot++)
                {
                    if (BrowncoatPlayerComplete(player, seat))
                        break;
                    if (pickCursor >= picks.Count)
                        throw new ArgumentException(
                            "BrowncoatDraftPicks ran out before the snake draft finished.",
                            nameof(options));
                    var pick = picks[pickCursor++];
                    if (!string.Equals(pick.PlayerId, seat.Id, StringComparison.Ordinal))
                        throw new ArgumentException(
                            $"BrowncoatDraftPicks[{pickCursor - 1}] player '{pick.PlayerId}' does not match snake-order seat '{seat.Id}'.",
                            nameof(options));
                    ApplyBrowncoatPick(game, player, pick, shipsTaken, leadersTaken);
                }
            }

            if (pickCursor != picks.Count)
                throw new ArgumentException(
                    $"BrowncoatDraftPicks has {picks.Count - pickCursor} unused pick(s) after the snake draft.",
                    nameof(options));

            foreach (var seat in seats)
            {
                var player = game.GetPlayer(seat.Id);
                if (string.IsNullOrWhiteSpace(player.ShipId))
                    throw new InvalidOperationException(
                        $"Browncoat draft incomplete: {player.Name} has no ship.");
            }
        }

        /// <summary>
        /// Turn owners in snake order: forward leftward (N−1 one-pick turns), last player
        /// (two picks), then reverse rightward (N−1 one-pick turns). For N=1 the sole
        /// player is the "last" player and takes both choices in one turn.
        /// </summary>
        private static IReadOnlyList<int> BuildBrowncoatTurnPlayerIndexes(int playerCount, int startIndex)
        {
            var turns = new List<int>();
            for (var i = 0; i < playerCount - 1; i++)
                turns.Add((startIndex + i) % playerCount);
            turns.Add((startIndex + playerCount - 1) % playerCount);
            for (var i = playerCount - 2; i >= 0; i--)
                turns.Add((startIndex + i) % playerCount);
            return turns;
        }

        private static int PicksForBrowncoatTurn(int turnIndex, int playerCount) =>
            turnIndex == playerCount - 1 ? 2 : 1;

        private static bool BrowncoatPlayerComplete(PlayerState player, PlayerSeat seat)
        {
            if (string.IsNullOrWhiteSpace(player.ShipId))
                return false;
            if (!string.IsNullOrWhiteSpace(seat.LeaderId) && string.IsNullOrWhiteSpace(player.LeaderId))
                return false;
            return true;
        }

        private static IList<BrowncoatDraftPick> BuildAutoBrowncoatPicks(
            GameState game,
            IReadOnlyList<PlayerSeat> seats,
            IReadOnlyList<int> turnPlayerIndexes,
            HashSet<string> shipsTaken,
            Queue<ShipCard> corePool)
        {
            var n = seats.Count;
            var hasShip = new bool[n];
            var hasLeader = new bool[n];
            var picks = new List<BrowncoatDraftPick>();

            for (var turnIndex = 0; turnIndex < turnPlayerIndexes.Count; turnIndex++)
            {
                var playerIndex = turnPlayerIndexes[turnIndex];
                var seat = seats[playerIndex];
                var slots = PicksForBrowncoatTurn(turnIndex, n);
                for (var slot = 0; slot < slots; slot++)
                {
                    var needsShip = !hasShip[playerIndex];
                    var needsLeader = !hasLeader[playerIndex] && !string.IsNullOrWhiteSpace(seat.LeaderId);
                    if (!needsShip && !needsLeader)
                        break;

                    // Prefer ship when both remain (list price is the scarce choice).
                    if (needsShip)
                    {
                        var shipId = ResolveAutoShipId(seat, shipsTaken, corePool, game);
                        if (!game.Ships!.TryResolve(shipId, out var ship))
                            throw new ArgumentException($"Unknown ship '{shipId}'.");
                        shipsTaken.Add(ship.Id);
                        picks.Add(BrowncoatDraftPick.Ship(seat.Id, ship.Id));
                        hasShip[playerIndex] = true;
                        continue;
                    }

                    picks.Add(BrowncoatDraftPick.Leader(seat.Id, seat.LeaderId!));
                    hasLeader[playerIndex] = true;
                }
            }

            return picks;
        }

        private static string ResolveAutoShipId(
            PlayerSeat seat,
            HashSet<string> shipsTaken,
            Queue<ShipCard> corePool,
            GameState game)
        {
            if (!string.IsNullOrWhiteSpace(seat.ShipId))
            {
                if (!game.Ships!.TryResolve(seat.ShipId, out var named))
                    throw new ArgumentException($"Unknown ship '{seat.ShipId}'.");
                if (shipsTaken.Contains(named.Id))
                    throw new ArgumentException($"Ship '{named.Name}' is already seated.");
                return named.Id;
            }

            var ship = NextUnusedCoreShip(corePool, shipsTaken)
                ?? throw new InvalidOperationException("No core Firefly ships remain to purchase.");
            return ship.Id;
        }

        private static void ApplyBrowncoatPick(
            GameState game,
            PlayerState player,
            BrowncoatDraftPick pick,
            HashSet<string> shipsTaken,
            HashSet<string> leadersTaken)
        {
            var hasShip = !string.IsNullOrWhiteSpace(pick.ShipId);
            var hasLeader = !string.IsNullOrWhiteSpace(pick.LeaderId);
            if (hasShip == hasLeader)
                throw new ArgumentException(
                    "Each Browncoat draft pick must set exactly one of ShipId or LeaderId.");

            if (hasShip)
            {
                if (!string.IsNullOrWhiteSpace(player.ShipId))
                    throw new InvalidOperationException($"{player.Name} already has a ship.");
                if (!game.Ships!.TryResolve(pick.ShipId!, out var ship))
                    throw new ArgumentException($"Unknown ship '{pick.ShipId}'.");
                if (!shipsTaken.Add(ship.Id))
                    throw new ArgumentException($"Ship '{ship.Name}' is already seated.");

                // SetupCards.json: "paying the Bank the list price on the Ship's card."
                // Director's Cut p.47: list prices include starting Ship Upgrades — do not pay again.
                if (player.Cash < ship.Cost)
                    throw new InvalidOperationException(
                        $"{player.Name} cannot afford '{ship.Name}' (${ship.Cost}).");
                player.Cash -= ship.Cost;

                player.ApplyShip(ship);
                if (game.DriveCores != null && game.DriveCores.TryResolve(ship.MainDrive, out var core))
                    player.ApplyDriveCore(core);
                GrantStartingShipUpgrades(player, ship);
                return;
            }

            if (game.Leaders == null)
                throw new InvalidOperationException("Leader catalog is required to select a Leader.");
            if (!string.IsNullOrWhiteSpace(player.LeaderId))
                throw new InvalidOperationException($"{player.Name} already has a Leader.");
            if (!game.Leaders.TryResolve(pick.LeaderId!, out var leader))
                throw new ArgumentException($"Unknown leader '{pick.LeaderId}'.");
            if (!leadersTaken.Add(leader.Id))
                throw new ArgumentException($"Leader '{leader.Name}' is already seated.");
            if (!player.Roster.TryHire(leader, out var error))
                throw new InvalidOperationException(error);
            player.LeaderId = leader.Id;
            game.SupplyDecks?.RemoveCrewNamed(leader.Name);
        }

        /// <summary>
        /// Director's Cut p.47–48: Series IV ships are pre-equipped with starting Ship Upgrades.
        /// On The Browncoat Way those upgrades are included in the ship list price.
        /// </summary>
        private static void GrantStartingShipUpgrades(PlayerState player, ShipCard ship)
        {
            foreach (var upgradeId in ship.StartingUpgrades)
            {
                if (string.IsNullOrWhiteSpace(upgradeId))
                    continue;
                if (!player.ShipUpgrades.Contains(upgradeId))
                    player.ShipUpgrades.Add(upgradeId);
            }
        }

        private static void ApplyBrowncoatResourceBuys(
            GameState game,
            SetupCard setup,
            GameSetupOptions options)
        {
            var buys = options.BrowncoatResourceBuys;
            if (buys == null || buys.Count == 0)
                return;

            foreach (var player in game.Players)
            {
                if (!buys.TryGetValue(player.Id, out var buy) || buy == null)
                    continue;
                if (buy.Fuel < 0 || buy.Parts < 0)
                    throw new ArgumentException($"Browncoat resource buy for '{player.Id}' cannot be negative.");
                if (buy.Fuel == 0 && buy.Parts == 0)
                    continue;

                var cost = buy.Fuel * setup.BuyFuelCost + buy.Parts * setup.BuyPartsCost;
                if (player.Cash < cost)
                    throw new InvalidOperationException(
                        $"{player.Name} cannot afford Browncoat Fuel/Parts (${cost}).");
                if (!HoldSpace.TryExplain(player, out var error, addFuel: buy.Fuel, addParts: buy.Parts))
                    throw new InvalidOperationException(
                        $"Browncoat Fuel/Parts for {player.Name}: {error}");

                player.Cash -= cost;
                player.Fuel += buy.Fuel;
                player.Parts += buy.Parts;
            }
        }

        private static void AssignStartingShips(GameState game, IReadOnlyList<PlayerSeat> seats)
        {
            if (game.Ships == null)
                return;

            var taken = new HashSet<string>(StringComparer.Ordinal);
            var pool = new Queue<ShipCard>();
            foreach (var ship in game.Ships.CoreStartingShips())
                pool.Enqueue(ship);

            foreach (var seat in seats)
            {
                var player = game.GetPlayer(seat.Id);
                ShipCard ship;
                if (!string.IsNullOrWhiteSpace(seat.ShipId))
                {
                    if (!game.Ships.TryResolve(seat.ShipId, out ship))
                        throw new ArgumentException($"Unknown ship '{seat.ShipId}'.");
                }
                else
                {
                    ship = NextUnusedCoreShip(pool, taken)
                        ?? throw new InvalidOperationException("No core Firefly ships remain to assign.");
                }

                if (!taken.Add(ship.Id))
                    throw new ArgumentException($"Ship '{ship.Name}' is already seated.");

                player.ApplyShip(ship);
                if (game.DriveCores != null && game.DriveCores.TryResolve(ship.MainDrive, out var core))
                    player.ApplyDriveCore(core);
                // Standard / non-Browncoat: starting upgrades still equip; payment for those
                // upgrades outside Browncoat is a separate Coachworks setup concern.
                GrantStartingShipUpgrades(player, ship);
            }
        }

        private static ShipCard? NextUnusedCoreShip(Queue<ShipCard> pool, HashSet<string> taken)
        {
            while (pool.Count > 0)
            {
                var ship = pool.Dequeue();
                if (!taken.Contains(ship.Id))
                    return ship;
            }
            return null;
        }

        private static void HireStartingLeaders(GameState game, IReadOnlyList<PlayerSeat> seats)
        {
            if (game.Leaders == null)
                return;

            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var seat in seats)
            {
                if (string.IsNullOrWhiteSpace(seat.LeaderId))
                    continue;
                if (!game.Leaders.TryResolve(seat.LeaderId, out var leader))
                    throw new ArgumentException($"Unknown leader '{seat.LeaderId}'.");
                if (!taken.Add(leader.Id))
                    throw new ArgumentException($"Leader '{leader.Name}' is already seated.");

                var player = game.GetPlayer(seat.Id);
                if (!player.Roster.TryHire(leader, out var error))
                    throw new InvalidOperationException(error);
                player.LeaderId = leader.Id;
                game.SupplyDecks?.RemoveCrewNamed(leader.Name);
            }
        }

        private static void DealStartingJobs(GameState game)
        {
            if (game.ContactDecks == null || game.Jobs == null)
                return;

            var contacts = StandardStartingContacts;
            foreach (var player in game.Players)
            {
                foreach (var name in contacts)
                {
                    if (!game.ContactDecks.TryGet(name, out var deck))
                        continue;
                    var drawn = deck.DrawConsider(1);
                    if (drawn.Count == 0)
                        continue;
                    player.JobHand.Add(drawn[0].Id);
                }

                while (player.JobHand.Count > player.JobHandLimit)
                {
                    var last = player.JobHand[player.JobHand.Count - 1];
                    player.JobHand.RemoveAt(player.JobHand.Count - 1);
                    if (game.Jobs.TryGet(last, out var job) &&
                        game.ContactDecks.TryGet(job.ContactName, out var deck))
                    {
                        deck.MoveToDiscard(job);
                    }
                }
            }
        }
    }
}
