using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class JobSitesBountiesTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string CorvetteStart = "rim-cortex-relay-2-r1-11";
        private const string WantedChild = "job_bounty_wanted-the-child";
        private const string CortexBanditsJob = "job_bounty_cortex-alert-bandits";

        private static GameState NewGame(string sectorId = Persephone, bool corvette = false)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: 0, fuel: 3);
            var tokens = corvette
                ? new MapTokens(GameSetup.AllianceCruiserStartSectorId, new[] { GameSetup.CoreReaverCutterStartSectorId }, CorvetteStart)
                : new MapTokens(GameSetup.AllianceCruiserStartSectorId, new[] { GameSetup.CoreReaverCutterStartSectorId }, null);
            var game = new GameState(map, new[] { player }, tokens)
            {
                Jobs = JobCatalog.LoadDefault(),
                Bounties = BountyCatalog.LoadDefault()
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1), game.Bounties);
            return game;
        }

        [Fact]
        public void Operative_corvette_dropoff_succeeds_when_token_is_co_located()
        {
            // Expansion selected → token on board; Work drop-off at token sector.
            var game = NewGame(Persephone, corvette: true);
            game.CurrentPlayer.ActiveJobs.Add(new ActiveJob(WantedChild) { PickedUp = true });
            game.CurrentPlayer.SectorId = CorvetteStart;

            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", WantedChild, out var result, out var error), error);
            Assert.Equal(WorkKind.Complete, result!.Kind);
            Assert.Equal(4000, result.Pay);
            Assert.Null(game.CurrentPlayer.FindActive(WantedChild));
        }

        [Fact]
        public void Operative_corvette_dropoff_fails_when_expansion_not_selected()
        {
            var game = NewGame(Persephone, corvette: false);
            game.CurrentPlayer.ActiveJobs.Add(new ActiveJob(WantedChild) { PickedUp = true });
            game.CurrentPlayer.SectorId = CorvetteStart;

            var work = new WorkAction();
            Assert.False(work.TryWork(game, "p1", WantedChild, out _, out var error));
            Assert.Contains("Operative's Corvette", error);
            Assert.NotNull(game.CurrentPlayer.FindActive(WantedChild));
        }

        [Fact]
        public void Operative_corvette_dropoff_fails_in_wrong_sector()
        {
            var game = NewGame(Persephone, corvette: true);
            game.CurrentPlayer.ActiveJobs.Add(new ActiveJob(WantedChild) { PickedUp = true });
            // Still at Persephone while Corvette is at Relay 2.
            Assert.False(new WorkAction().TryWork(game, "p1", WantedChild, out _, out var error));
            Assert.Contains("Operative's Corvette", error);
        }

        [Fact]
        public void Setup_places_corvette_only_when_flag_set()
        {
            var off = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });
            Assert.Null(off.Tokens.OperativeCorvetteSectorId);

            var on = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UseOperativesCorvette = true,
                    Rng = new SystemRng(1)
                });
            Assert.Equal(CorvetteStart, on.Tokens.OperativeCorvetteSectorId);
        }

        [Fact]
        public void PBH_bounty_jobs_are_excluded_from_contact_decks()
        {
            var jobs = JobCatalog.LoadDefault();
            var bounties = BountyCatalog.LoadDefault();
            Assert.True(BountyJobAuthority.IsCoveredByBountyDeck(jobs.Get(CortexBanditsJob), bounties));
            Assert.True(BountyJobAuthority.IsCoveredByBountyDeck(jobs.Get("job_bounty_wanted-jayne"), bounties));
            Assert.False(BountyJobAuthority.IsCoveredByBountyDeck(jobs.Get(WantedChild), bounties));

            var decks = new ContactDecks(jobs, new SystemRng(2), bounties);
            // Playtest / non-PBH bounty rows may remain under contact "Bounty";
            // official PBH duplicates must not.
            Assert.True(decks.TryGet("Bounty", out var deck));
            var remaining = deck!.DrawConsider(deck.DrawCount);
            Assert.DoesNotContain(remaining, j => j.Id == CortexBanditsJob);
            Assert.DoesNotContain(remaining, j => j.Id == "job_bounty_wanted-jayne");
            Assert.Contains(remaining, j => j.Id == WantedChild);
        }

        [Fact]
        public void Work_rejects_PBH_bounty_duplicates_and_Various_cortex_jobs()
        {
            var game = NewGame();
            game.CurrentPlayer.JobHand.Add(CortexBanditsJob);
            var work = new WorkAction();
            Assert.False(work.TryWork(game, "p1", CortexBanditsJob, out _, out var error));
            Assert.Contains("Most Wanted", error);

            // Non-PBH Various job (Kids) still blocked from Work-as-Job site path.
            const string kids = "job_bounty_cortex-alert-kids";
            game.CurrentPlayer.JobHand.Clear();
            game.CurrentPlayer.JobHand.Add(kids);
            Assert.False(work.TryWork(game, "p1", kids, out _, out error));
            Assert.Contains("Bounty deck", error);
        }

        [Fact]
        public void Bounty_deck_is_optional_at_setup()
        {
            var core = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            Assert.NotNull(core.Bounties);
            Assert.Null(core.BountyDeck);

            var pbh = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UseBountyDeck = true,
                    Rng = new SystemRng(3)
                });
            Assert.NotNull(pbh.BountyDeck);
            Assert.Equal(3, pbh.BountyDeck!.FaceUp.Count);
        }
    }
}
