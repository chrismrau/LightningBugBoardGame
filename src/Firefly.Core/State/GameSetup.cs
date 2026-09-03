using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;

namespace Firefly.Core.State
{
    public sealed class PlayerSeat
    {
        public string Id { get; }
        public string Name { get; }
        public string SectorId { get; }
        public string? ShipId { get; }

        public PlayerSeat(string id, string name, string sectorId, string? shipId = null)
        {
            Id = id;
            Name = name;
            SectorId = sectorId;
            ShipId = shipId;
        }
    }

    public sealed class GameSetupOptions
    {
        public string SetupCardId { get; set; } = "setup_standard";
        public string? ScenarioCardId { get; set; }
        public IRng? Rng { get; set; }
        public bool DealStartingJobs { get; set; } = true;
        public int PrimeSupplyReveal { get; set; } = 3;
    }

    /// <summary>
    /// Builds a playable GameState: catalogs, Nav / Contact / Misbehave decks,
    /// and one Supply market per planet with the top cards face up.
    /// Starting cash, fuel, parts, and optional starting jobs come from the Setup card.
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

            var game = new GameState(map, players)
            {
                Setup = setup,
                Scenario = scenario,
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Gear = GearIndex.LoadDefault(),
                Supply = SupplyCatalog.LoadDefault()
            };

            game.Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, rng);
            game.ContactDecks = new ContactDecks(game.Jobs, rng);
            game.SupplyDecks = BuildSupplyDecks(game.Supply, rng, options.PrimeSupplyReveal);
            var misbehave = MisbehaveCatalog.LoadDefault();
            game.MisbehaveCatalog = misbehave;
            game.Misbehave = MisbehaveDeck.FromCatalog(misbehave, rng);

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
