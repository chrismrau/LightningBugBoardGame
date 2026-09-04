using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class SetupAndScenarioTests
    {
        [Fact]
        public void Setup_catalog_has_eight_cards()
        {
            var catalog = SetupCatalog.LoadDefault();
            Assert.Equal(8, catalog.Cards.Count);
            var standard = catalog.Get("setup_standard");
            Assert.Equal(3000, standard.StartingCash);
            Assert.Equal(6, standard.StartingFuel);
            Assert.Equal(2, standard.StartingParts);
        }

        [Fact]
        public void Browncoat_way_starts_with_twelve_thousand_and_no_free_fuel()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_the-browncoat-way");
            Assert.Equal(12000, card.StartingCash);
            Assert.Equal(0, card.StartingFuel);
        }

        [Fact]
        public void Scenario_catalog_has_nineteen_cards()
        {
            var catalog = ScenarioCatalog.LoadDefault();
            Assert.Equal(19, catalog.Cards.Count);
            Assert.Equal("First Time in the Captain's Chair", catalog.Get("scenario_first-time-in-the-captains-chair").Name);
            Assert.Equal("firstToCompleteGoal", catalog.Get("scenario_first-time-in-the-captains-chair").WinType);
        }

        [Fact]
        public void Blitz_steps_are_numbered_one_through_eight()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(GameData.SetupCardsPath));
            var blitz = doc.RootElement.GetProperty("setupCards").EnumerateArray()
                .First(c => c.GetProperty("id").GetString() == "setup_the-blitz");
            var orders = blitz.GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("order").GetInt32()).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, orders);
        }

        [Fact]
        public void Scavengers_verse_is_deferred()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(GameData.ScenarioCardsPath));
            var card = doc.RootElement.GetProperty("scenarioCards").EnumerateArray()
                .First(c => c.GetProperty("id").GetString() == "scenario_the-scavengers-verse");
            Assert.True(card.GetProperty("deferred").GetBoolean());
        }

        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";

        [Fact]
        public void Standard_setup_wires_misbehave_and_supply_decks()
        {
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            Assert.Equal("setup_standard", game.Setup!.Id);
            Assert.NotNull(game.Misbehave);
            Assert.NotNull(game.MisbehaveCatalog);
            Assert.True(game.Misbehave!.DrawCount >= 70);
            Assert.Equal(0, game.Misbehave.DiscardCount);

            Assert.NotNull(game.Supply);
            Assert.NotNull(game.SupplyDecks);
            foreach (var planet in GameSetup.CoreSupplyPlanets)
            {
                Assert.True(game.SupplyDecks!.TryGet(planet, out var market), planet);
                Assert.Equal(3, market.FaceUp.Count);
                Assert.True(market.Deck.Count > 0, planet);
            }

            Assert.NotNull(game.Decks);
            Assert.NotNull(game.Jobs);
            Assert.NotNull(game.ContactDecks);
            Assert.NotNull(game.Crew);
            Assert.NotNull(game.Leaders);
            Assert.NotNull(game.Gear);
        }

        [Fact]
        public void Starting_leader_is_hired_for_free()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(8) });

            var player = game.CurrentPlayer;
            Assert.Equal("leader_malcolm", player.LeaderId);
            Assert.Equal(1, player.Roster.Count);
            Assert.True(player.Roster.Members[0].IsLeader);
            Assert.Equal("Malcolm", player.Roster.Members[0].Name);
            Assert.Equal(2, player.Fight);
            Assert.Equal(1, player.Talk);
            Assert.True(player.Roster.HasProfession("Pilot"));
            Assert.Equal(3000, player.Cash);
        }

        [Fact]
        public void Leader_can_be_chosen_by_printed_name()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Nandi", Persephone, leaderId: "Nandi") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(9) });

            Assert.Equal("leader_nandi", game.CurrentPlayer.LeaderId);
            Assert.True(game.CurrentPlayer.Roster.HasProfession("Companion"));
        }

        [Fact]
        public void Two_players_cannot_share_a_leader()
        {
            Assert.Throws<ArgumentException>(() =>
                GameSetup.Standard(
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm"),
                    new PlayerSeat("p2", "Also Mal", Santo, leaderId: "Malcolm")));
        }

        [Fact]
        public void Standard_setup_gives_printed_starting_supplies_and_a_job_hand()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { Rng = new SystemRng(4) });

            var player = game.CurrentPlayer;
            Assert.Equal(3000, player.Cash);
            Assert.Equal(6, player.Fuel);
            Assert.Equal(2, player.Parts);
            Assert.True(player.JobHand.Count > 0);
            Assert.True(player.JobHand.Count <= player.JobHandLimit);
        }

        [Fact]
        public void Browncoat_setup_starts_rich_and_dry()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-browncoat-way",
                    DealStartingJobs = false,
                    Rng = new SystemRng(5)
                });

            Assert.Equal(12000, game.CurrentPlayer.Cash);
            Assert.Equal(0, game.CurrentPlayer.Fuel);
            Assert.Equal(0, game.CurrentPlayer.Parts);
            Assert.Empty(game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Setup_game_can_buy_at_persephone()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(6) });

            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            var cardId = market.FaceUp[0].Id;
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1, SupplyCardIds = { cardId } }, out var result, out var error), error);
            Assert.Equal(1, result!.FuelBought);
            Assert.Single(result.CardsBought);
            Assert.Equal(3, market.FaceUp.Count);
        }

        [Fact]
        public void Setup_game_can_draw_a_misbehave_card()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(7) });

            game.CurrentPlayer.JobHand.Add("job_badger_badgers-11-casino-caper");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", "job_badger_badgers-11-casino-caper", out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);

            var resolver = new MisbehaveResolver();
            var card = resolver.DrawNext(game);
            Assert.False(string.IsNullOrWhiteSpace(card.Id));
            Assert.NotNull(game.PendingMisbehave!.FaceUp);
        }
    }
}
