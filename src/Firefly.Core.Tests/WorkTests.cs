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
        public void Successful_pickup_makes_the_job_active()
        {
            var game = NewGame(Harvest);
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out var result, out var error), error);
            Assert.True(result!.BecameActive);
            Assert.NotNull(game.CurrentPlayer.FindActive(Shipping));
        }

        [Fact]
        public void Botched_start_leaves_the_job_in_hand()
        {
            var game = NewGame(Santo);
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", false, out _, out _));
            Assert.Contains(Crime, game.CurrentPlayer.JobHand);
            Assert.Null(game.CurrentPlayer.FindActive(Crime));
        }

        [Fact]
        public void Botched_dropoff_leaves_the_job_active()
        {
            var game = NewGame(Bazaar);
            game.CurrentPlayer.JobHand.Add(Smuggling);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Smuggling, out _, out _));
            Assert.NotNull(game.CurrentPlayer.FindActive(Smuggling));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Aphrodite;
            Assert.True(work.TryWork(game, "p1", Smuggling, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(work.TryProceedMisbehave(game, "p1", false, out _, out _));
            Assert.NotNull(game.CurrentPlayer.FindActive(Smuggling));
            Assert.Equal(2, game.CurrentPlayer.Contraband);
            Assert.Equal(0, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Crime_job_completes_from_hand_after_misbehave()
        {
            var game = NewGame(Santo);
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out var last), last);
            Assert.Equal(3500, done!.Pay);
            Assert.Null(game.CurrentPlayer.FindActive(Crime));
        }

        [Fact]
        public void Shipping_job_pays_on_dropoff()
        {
            var game = NewGame(Harvest);
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out _, out _));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWork(game, "p1", Shipping, out var done, out var error), error);
            Assert.Equal(1500, done!.Pay);
            Assert.Null(game.CurrentPlayer.FindActive(Shipping));
        }
    }
}
