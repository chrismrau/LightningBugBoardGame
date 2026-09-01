using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class WorkTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Harvest = "border-red-sun-r2-02";
        private const string Albion = "alliance-white-sun-r4-11";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Bazaar = "border-red-sun-r3-08";
        private const string Aphrodite = "border-murphy-r1-01";
        private const string Shipping = "job_amnon-duul_feeding-alliance-fat-cats";
        private const string Crime = "job_badger_badgers-11-casino-caper";
        private const string Smuggling = "job_amnon-duul_courting-aphrodite";
        private const string Immoral = "job_badger_send-them-to-the-ruttin-mines";

        private static GameState NewGame(string sectorId = Persephone)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: 0, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            return game;
        }

        [Fact]
        public void Job_terms_parse_misbehave_and_goods()
        {
            var job = JobCatalog.LoadDefault().Get(Smuggling);
            Assert.Equal(2, JobTerms.Pickup(job).Contraband);
            Assert.Equal(1, JobTerms.Dropoff(job).Misbehave);
            Assert.Equal("Aphrodite", JobTerms.PlaceName(job.DropoffLocation));
        }

        [Fact]
        public void Activate_moves_a_hand_job_to_the_active_slot()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Shipping, out var result, out var error), error);
            Assert.Equal(WorkKind.Activate, result!.Kind);
            Assert.Empty(game.CurrentPlayer.JobHand);
            Assert.NotNull(game.CurrentPlayer.FindActive(Shipping));
        }

        [Fact]
        public void Second_active_job_is_rejected_without_a_bonus_slot()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(Shipping);
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Shipping, out _, out _));
            game.EndTurn();
            Assert.False(work.TryActivate(game, "p1", Crime, out _, out var error));
            Assert.Contains("active job", error);
        }

        [Fact]
        public void Shipping_job_loads_cargo_then_pays_on_dropoff()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Shipping, out _, out _));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Harvest;
            Assert.True(work.TryWorkActive(game, "p1", Shipping, out var pickup, out var error), error);
            Assert.Equal(2, game.CurrentPlayer.Cargo);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWorkActive(game, "p1", Shipping, out var done, out var dropError), dropError);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(1500, done.Pay);
            Assert.Equal(0, game.CurrentPlayer.Cargo);
            Assert.True(game.CurrentPlayer.IsSolidWith("contact_amnon-duul"));
        }

        [Fact]
        public void Work_at_the_wrong_planet_is_rejected()
        {
            var game = NewGame(Persephone);
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Shipping, out _, out _));
            game.EndTurn();
            Assert.False(work.TryWorkActive(game, "p1", Shipping, out _, out var error));
            Assert.Contains("Harvest", error);
        }

        [Fact]
        public void Crime_job_completes_after_misbehave_proceeds()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Crime, out _, out _));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Santo;
            Assert.True(work.TryWorkActive(game, "p1", Crime, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.Equal(3, game.PendingMisbehave!.Remaining);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out var last), last);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(3500, done.Pay);
            Assert.True(game.CurrentPlayer.IsSolidWith("contact_badger"));
        }

        [Fact]
        public void Botched_misbehave_spends_the_Work_and_leaves_the_job_active()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Crime, out _, out _));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Santo;
            Assert.True(work.TryWorkActive(game, "p1", Crime, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", false, out _, out _));
            Assert.NotNull(game.CurrentPlayer.FindActive(Crime));
            Assert.Equal(0, game.CurrentPlayer.Cash);
            Assert.Equal(TurnAction.Work, game.LastAction);
        }

        [Fact]
        public void Smuggling_dropoff_requires_the_loaded_contraband()
        {
            var game = NewGame(Bazaar);
            game.CurrentPlayer.JobHand.Add(Smuggling);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Smuggling, out _, out _));
            game.EndTurn();
            Assert.True(work.TryWorkActive(game, "p1", Smuggling, out _, out var pickupError), pickupError);
            Assert.Equal(2, game.CurrentPlayer.Contraband);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Aphrodite;
            game.CurrentPlayer.Contraband = 0;
            Assert.False(work.TryWorkActive(game, "p1", Smuggling, out _, out var missing));
            Assert.Contains("goods", missing);
        }

        [Fact]
        public void Immoral_job_disgruntles_moral_crew_on_activate()
        {
            var game = NewGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(CrewCatalog.LoadDefault().FindByName("Kaylee")!, out _));
            game.CurrentPlayer.JobHand.Add(Immoral);
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Immoral, out var result, out _));
            Assert.Equal(1, result!.MoralDisgruntled);
        }

        [Fact]
        public void Second_Work_on_the_same_turn_is_rejected()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(Shipping);
            game.CurrentPlayer.JobHand.Add(Crime);
            game.CurrentPlayer.ActiveJobLimit = 2;
            var work = new WorkAction();
            Assert.True(work.TryActivate(game, "p1", Shipping, out _, out _));
            Assert.False(work.TryActivate(game, "p1", Crime, out _, out var error));
            Assert.Contains("already used this turn", error);
        }
    }
}
