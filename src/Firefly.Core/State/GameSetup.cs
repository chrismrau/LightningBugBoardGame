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
                    shipId: seat.ShipId));
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

            AssignStartingShips(game, seats);
            HireStartingLeaders(game, seats);
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

            return game;
        }

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
