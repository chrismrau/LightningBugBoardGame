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

        private static JobCard Job(string id, string contact = "Badger") =>
            new JobCard(id, id, contact, "Crime", true, false, "Persephone", null, "Ezra", null, 1000, "1000", null, null, null);

        [Fact]
        public void Job_catalog_loads_core_and_expansion_jobs()
        {
            var jobs = JobCatalog.LoadDefault();
            Assert.True(jobs.Cards.Count > 100);
            Assert.NotEmpty(jobs.ForContact("Badger"));
            Assert.NotEmpty(jobs.ForContact("Fanty & Mingo"));
        }

        [Fact]
        public void Consider_limit_is_three_by_default_and_keep_at_most_two()
        {
            var game = NewGame();
            game.Jobs = new JobCatalog(new[] { Job("j1"), Job("j2"), Job("j3"), Job("j4") });
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            var deal = new DealAction();
            var badger = game.Contacts!.Cards["contact_badger"];

            Assert.Equal(3, DealAction.ConsiderLimit(game.CurrentPlayer, badger, remote: false));
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger", ConsiderCount = 4 }, out _, out var error));
            Assert.Contains("at most 3", error);

            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                ConsiderCount = 3,
                KeepFromConsidered = { "j1", "j2", "j3" }
            }, out _, out var tooMany));
            Assert.Contains("at most 2", tooMany);
        }

        [Fact]
        public void Deal_may_keep_zero_considered_jobs()
        {
            var game = NewGame();
            game.Jobs = new JobCatalog(new[] { Job("j1"), Job("j2"), Job("j3") });
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(7));
            var deal = new DealAction();

            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                ConsiderCount = 3
            }, out var none, out _));
            Assert.True(none!.Considered);
            Assert.Empty(none.KeptFromConsider);
            Assert.Empty(game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Deal_keeps_two_from_a_consider_three()
        {
            var game = NewGame();
            game.Jobs = new JobCatalog(new[] { Job("j1"), Job("j2"), Job("j3") });
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));

            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                ConsiderCount = 3,
                KeepFromConsidered = { "j1", "j2" }
            }, out var result, out var error), error);
            Assert.Equal(2, result!.KeptFromConsider.Count);
            Assert.Equal(2, game.CurrentPlayer.JobHand.Count);
        }

        [Fact]
        public void Taking_from_discard_is_not_considering()
        {
            var game = NewGame();
            var discarded = Job("job_disc");
            game.Jobs = new JobCatalog(new[] { discarded, Job("j2") });
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            Assert.True(game.ContactDecks.TryGet("Badger", out var deck));
            deck.MoveToDiscard(discarded);

            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                ConsiderCount = 0,
                TakeFromDiscard = { "job_disc" }
            }, out var result, out var error), error);
            Assert.False(result!.Considered);
            Assert.Equal("job_disc", result.TakenFromDiscard[0].Id);
            Assert.Contains("job_disc", game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Deal_is_rejected_when_not_in_the_contact_sector()
        {
            var game = NewGame(Pelorum);
            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out var error));
            Assert.Contains("Must be in Badger's sector", error);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void Deal_then_Mosey_is_a_legal_two_action_turn()
        {
            var game = NewGame();
            var deal = new DealAction();
            var fly = new FlyAction(new MovementEngine(game.Map));
            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out _));
            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out var error));
            Assert.Null(error);
            Assert.True(game.TurnComplete);
        }

        [Fact]
        public void Second_Deal_on_the_same_turn_is_rejected()
        {
            var game = NewGame();
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out _));
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger" }, out _, out var error));
            Assert.Contains("already used this turn", error);
        }

        [Fact]
        public void Selling_contraband_to_Badger_pays_seven_hundred_each()
        {
            var game = NewGame();
            game.CurrentPlayer.Contraband = 2;
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                SellContraband = 2
            }, out var result, out _));
            Assert.Equal(1400, result!.CashFromSales);
            Assert.Equal(0, game.CurrentPlayer.Contraband);
            Assert.Equal(3400, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Harken_requires_the_Alliance_Cruiser()
        {
            var game = NewGame(Persephone);
            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Harken" }, out _, out var error));
            Assert.Contains("Alliance Cruiser", error);

            game.Tokens = new MapTokens(allianceCruiserSectorId: Persephone);
            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Harken" }, out _, out var ok));
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
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Magistrate Higgins" }, out _, out var error));
            Assert.Contains("Jayne", error);
        }

        [Fact]
        public void Solid_Mr_Universe_can_be_dealt_from_any_sector()
        {
            var game = NewGame(Persephone);
            var universe = game.Contacts!.Cards["contact_mr-universe"];
            game.CurrentPlayer.BecomeSolid(universe.Id);
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Mr. Universe" }, out _, out var error));
            Assert.Null(error);
        }

        [Fact]
        public void Fine_Hat_raises_consider_limit_to_four()
        {
            var game = NewGame();
            game.CurrentPlayer.Deal.ConsiderUpTo = DealActionDefaults.FineHatConsiderUpTo;
            var badger = game.Contacts!.Cards["contact_badger"];
            Assert.Equal(4, DealAction.ConsiderLimit(game.CurrentPlayer, badger, remote: false));
        }

        [Fact]
        public void Cortex_Uplink_considers_only_the_top_card_from_another_sector()
        {
            var game = NewGame(Pelorum);
            game.CurrentPlayer.Deal.ConsiderTopCardFromAnyContact = true;
            var badger = game.Contacts!.Cards["contact_badger"];
            Assert.Equal(1, DealAction.ConsiderLimit(game.CurrentPlayer, badger, remote: true));

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                ConsiderCount = 3
            }, out _, out var error));
            Assert.Contains("at most 1", error);

            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Badger",
                ConsiderCount = 1
            }, out var result, out var ok), ok);
            Assert.True(result!.Considered);
            Assert.Equal(1, result.Drawn.Count);
        }

        [Fact]
        public void Solid_Patience_considers_four()
        {
            var game = NewGame("border-georgia-r3-03");
            var patience = game.Contacts!.Cards["contact_patience"];
            game.CurrentPlayer.BecomeSolid(patience.Id);
            Assert.Equal(4, DealAction.ConsiderLimit(game.CurrentPlayer, patience, remote: false));
        }
    }
}
