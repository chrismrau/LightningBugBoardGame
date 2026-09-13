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

        private static readonly string[] RemovedPbhDuplicateJobIds =
        {
            "job_bounty_cortex-alert-bandits",
            "job_bounty_cortex-alert-enforcers",
            "job_bounty_cortex-alert-scrappers",
            "job_bounty_wanted-billy",
            "job_bounty_wanted-bree",
            "job_bounty_wanted-crow",
            "job_bounty_wanted-dalin",
            "job_bounty_wanted-grange-brothers",
            "job_bounty_wanted-helen",
            "job_bounty_wanted-interrogator",
            "job_bounty_wanted-jayne",
            "job_bounty_wanted-jesse",
            "job_bounty_wanted-river-tam",
            "job_bounty_wanted-simon-tam",
            "job_bounty_wanted-stitch",
            "job_bounty_wanted-the-fixer",
            "job_bounty_wanted-the-specialist",
            "job_bounty_wanted-tracey",
            "job_bounty_wanted-two-fry",
            "job_bounty_wanted-zoe",
        };

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
        public void PBH_Wanted_and_Cortex_duplicates_are_removed_from_Jobs_json()
        {
            // #19 excluded these from ContactDecks; they are deleted now —
            // Bounties.json is the only authority for official PBH bounties.
            var jobs = JobCatalog.LoadDefault();
            Assert.Equal(348, jobs.Cards.Count);
            foreach (var id in RemovedPbhDuplicateJobIds)
                Assert.False(jobs.TryGet(id, out _), id);

            var bounties = BountyCatalog.LoadDefault();
            Assert.False(BountyJobAuthority.IsCoveredByBountyDeck(jobs.Get(WantedChild), bounties));

            var decks = new ContactDecks(jobs, new SystemRng(2), bounties);
            Assert.True(decks.TryGet("Bounty", out var deck));
            var remaining = deck!.DrawConsider(deck.DrawCount);
            Assert.Contains(remaining, j => j.Id == WantedChild);
            Assert.DoesNotContain(remaining, j => j.Name.StartsWith("Wanted: Jayne"));
            Assert.DoesNotContain(remaining, j => j.Name.StartsWith("Cortex Alert: Bandits"));
        }

        [Fact]
        public void Work_rejects_orphan_PBH_name_and_Various_cortex_jobs()
        {
            // Defensive: a synthetic Wanted row matching Bounties.json still
            // cannot be Worked as a Contact Job (Most Wanted path only).
            var orphan = new JobCard(
                "job_orphan_wanted-jayne", "Wanted: Jayne", "Bounty", null,
                legal: true, immoral: false,
                pickupLocation: "Persephone", pickupDetails: null,
                dropoffLocation: "Ariel", dropoffDetails: null,
                payBase: 2000, payRaw: "2000", bonus: null, special: null, description: null);
            var game = NewGame();
            game.Jobs = new JobCatalog(new[] { orphan, game.Jobs!.Get(WantedChild) });
            game.CurrentPlayer.JobHand.Add(orphan.Id);
            var work = new WorkAction();
            Assert.False(work.TryWork(game, "p1", orphan.Id, out _, out var error));
            Assert.Contains("Most Wanted", error);

            // Non-PBH Various job (Kids) still blocked from Work-as-Job site path.
            const string kids = "job_bounty_cortex-alert-kids";
            game.Jobs = JobCatalog.LoadDefault();
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
