using System;
using System.Collections.Generic;
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
        public int PrimeSupplyReveal { get; set; } = 3;
        /// <summary>
        /// Blue Sun / Kalidasa: place the Operative's Corvette. Off for core-only setup.
        /// </summary>
        public bool UseOperativesCorvette { get; set; }

        /// <summary>
        /// Pirates &amp; Bounty Hunters: Bounty Deck + Most Wanted List.
        /// Optional — not required for basic play (PBH p.3 expanding the 'Verse).
        /// </summary>
        public bool UseBountyDeck { get; set; }

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
            game.SupplyDecks = BuildSupplyDecks(game.Supply, rng, options.PrimeSupplyReveal);
            var misbehave = MisbehaveCatalog.LoadDefault();
            game.MisbehaveCatalog = misbehave;
            game.Misbehave = MisbehaveDeck.FromCatalog(misbehave, rng);
            // PBH p.3 / p.8: Bounty Cards form a new, separate deck — optional expansion.
            if (options.UseBountyDeck)
                game.BountyDeck = BountyDeck.FromCatalog(game.Bounties, rng);
            game.AllianceAlerts = AllianceAlertCatalog.LoadDefault();
            game.AllianceAlertDeck = AllianceAlertDeck.FromCatalog(game.AllianceAlerts, rng);
            if (setup.StartingAlertCard || (scenario != null && scenario.StartingAlertCard))
                game.AllianceAlertDeck.DrawAndActivate();

            AssignStartingShips(game, seats);
            HireStartingLeaders(game, seats);

            if (options.DealStartingJobs)
                DealStartingJobs(game);

            return game;
        }

        private static SupplyDecks BuildSupplyDecks(SupplyCatalog catalog, IRng rng, int faceUp)
        {
            var decks = SupplyDecks.FromCatalog(catalog, rng);
            if (faceUp == SupplyMarket.FaceUpCount)
                return decks;

            foreach (var market in decks.Markets)
            {
                while (market.FaceUp.Count > faceUp)
                {
                    var extra = market.FaceUp[market.FaceUp.Count - 1];
                    market.FaceUp.RemoveAt(market.FaceUp.Count - 1);
                    market.Deck.Insert(0, extra);
                }
                while (market.FaceUp.Count < faceUp && market.Deck.Count > 0)
                {
                    var next = market.Deck[0];
                    market.Deck.RemoveAt(0);
                    market.FaceUp.Add(next);
                }
            }
            return decks;
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
