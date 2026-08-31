using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class DealTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";

        private static GameState NewGame(string sectorId = Persephone, int? seed = 1)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: 2000, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(seed));
            return game;
        }

        [Fact]
        public void Job_catalog_loads_core_and_expansion_jobs()
        {
            var jobs = JobCatalog.LoadDefault();
            Assert.True(jobs.Cards.Count > 100);
            Assert.NotEmpty(jobs.ForContact("Badger"));
            Assert.NotEmpty(jobs.ForContact("Fanty & Mingo"));
        }

        [Fact]
        public void Deal_with_Badger_on_Persephone_keeps_one_job()
        {
            var game = NewGame();
            var only = new JobCard(
                "job_badger_test", "Test Job", "Badger", "Crime",
                legal: true, immoral: false,
                "Persephone", null, "Ezra", null,
                1000, "1000", null, null, null);
            game.Jobs = new JobCatalog(new[] { only });
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));

            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", "Badger", "job_badger_test", 0, 0, false, out var result, out var error));
            Assert.Null(error);
            Assert.Equal("job_badger_test", result!.Kept!.Id);
            Assert.Contains("job_badger_test", game.CurrentPlayer.JobHand);
            Assert.Equal(TurnAction.Deal, game.LastAction);
            Assert.Equal(1, game.ActionsUsedThisTurn);
        }

        [Fact]
        public void Deal_is_rejected_when_not_in_the_contact_sector()
        {
            var game = NewGame(Pelorum);
            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", "Badger", null, 0, 0, false, out _, out var error));
            Assert.Contains("Must be in Badger's sector", error);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void Deal_then_Mosey_is_a_legal_two_action_turn()
        {
            var game = NewGame();
            var deal = new DealAction();
            var fly = new FlyAction(new MovementEngine(game.Map));
            Assert.True(deal.TryDeal(game, "p1", "Badger", null, 0, 0, false, out _, out _));
            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out var error));
            Assert.Null(error);
            Assert.True(game.TurnComplete);
            Assert.False(fly.TryMosey(game, "p1", Persephone, out _, out var second));
            Assert.Contains("both actions", second);
        }

        [Fact]
        public void Second_Deal_on_the_same_turn_is_rejected()
        {
            var game = NewGame();
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", "Badger", null, 0, 0, false, out _, out _));
            Assert.False(deal.TryDeal(game, "p1", "Badger", null, 0, 0, false, out _, out var error));
            Assert.Contains("already used this turn", error);
        }

        [Fact]
        public void Selling_contraband_to_Badger_pays_seven_hundred_each()
        {
            var game = NewGame();
            game.CurrentPlayer.Contraband = 2;
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", "Badger", null, sellContraband: 2, sellCargo: 0, false, out var result, out _));
            Assert.Equal(1400, result!.CashFromSales);
            Assert.Equal(0, game.CurrentPlayer.Contraband);
            Assert.Equal(3400, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Harken_requires_the_Alliance_Cruiser()
        {
            var game = NewGame(Persephone);
            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", "Harken", null, 0, 0, false, out _, out var error));
            Assert.Contains("Alliance Cruiser", error);

            game.Tokens = new MapTokens(allianceCruiserSectorId: Persephone);
            Assert.True(deal.TryDeal(game, "p1", "Harken", null, 0, 0, false, out _, out var ok));
            Assert.Null(ok);
        }

        [Fact]
        public void Higgins_refuses_a_crew_that_includes_Jayne()
        {
            var game = NewGame("border-red-sun-r2-02");
            var crew = CrewCatalog.LoadDefault();
            var jayne = crew.FindByName("Jayne");
            Assert.NotNull(jayne);
            Assert.True(game.CurrentPlayer.Roster.TryHire(jayne!, out _));

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", "Magistrate Higgins", null, 0, 0, false, out _, out var error));
            Assert.Contains("Jayne", error);
        }

        [Fact]
        public void Solid_Mr_Universe_can_be_dealt_from_any_sector()
        {
            var game = NewGame(Persephone);
            var universe = game.Contacts!.Cards["contact_mr-universe"];
            game.CurrentPlayer.BecomeSolid(universe.Id);
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", "Mr. Universe", null, 0, 0, false, out _, out var error));
            Assert.Null(error);
        }
    }
}
