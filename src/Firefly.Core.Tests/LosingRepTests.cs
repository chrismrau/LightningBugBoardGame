using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// GF9 p.16 / Director's Cut p.24 Losing Rep:
    /// Warrant while Working a Job for a Contact → lose Solid with that Job's Contact.
    /// Orthogonal to Zero Tolerance (Harken any Warrant) and Niska Pound of Flesh.
    /// </summary>
    public class LosingRepTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Silverhold = "border-heinlein-r1-02";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        private static GameState NewCrimeGame()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 500, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            game.Crew = CrewCatalog.LoadDefault();
            game.Gear = GearIndex.LoadDefault();
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);
            player.JobHand.Add(Crime);
            return game;
        }

        private static void StartCrime(GameState game)
        {
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
        }

        [Fact]
        public void WarrantIssuer_with_working_Job_strips_that_Contacts_Solid()
        {
            // GF9 p.16: If you receive a Warrant while working a Job for a Contact,
            // any reputation with that Contact is lost.
            var game = NewCrimeGame();
            var player = game.CurrentPlayer;
            var job = game.Jobs!.Get(Crime);
            ContactSolidBenefits.BecomeSolid(game, player, "contact_badger");
            ContactSolidBenefits.BecomeSolid(game, player, "contact_amnon-duul");
            Assert.True(player.IsSolidWith("contact_badger"));

            Assert.True(WarrantIssuer.TryIssue(
                game, player, out var error, workingJob: job), error);
            Assert.Equal(1, player.Warrants);
            Assert.False(player.IsSolidWith("contact_badger"));
            Assert.True(player.IsSolidWith("contact_amnon-duul"));
        }

        [Fact]
        public void Warrant_while_Misbehaving_strips_Job_Contact_Solid()
        {
            var game = NewCrimeGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(game.Crew!.FindByName("Kaylee")!, out _));
            player.FightBonus = 1;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_badger");
            ContactSolidBenefits.BecomeSolid(game, player, "contact_amnon-duul");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(1, resolution!.WarrantsIssued);
            Assert.Equal(1, player.Warrants);
            Assert.False(player.IsSolidWith("contact_badger"));
            Assert.True(player.IsSolidWith("contact_amnon-duul"));
            Assert.Null(game.PendingMisbehave);
        }

        [Fact]
        public void Warrant_while_Working_also_strips_Harken_via_Zero_Tolerance()
        {
            // Losing Rep (Badger) and Zero Tolerance (Harken) are orthogonal.
            var game = NewCrimeGame();
            var player = game.CurrentPlayer;
            var job = game.Jobs!.Get(Crime);
            ContactSolidBenefits.BecomeSolid(game, player, "contact_badger");
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");

            Assert.True(WarrantIssuer.TryIssue(
                game, player, out var error, workingJob: job), error);
            Assert.False(player.IsSolidWith("contact_badger"));
            Assert.False(player.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void Warrant_not_while_Working_does_not_strip_non_Harken_Solid()
        {
            var game = NewCrimeGame();
            var player = game.CurrentPlayer;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_badger");
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");

            Assert.True(WarrantIssuer.TryIssue(game, player, out var error), error);
            Assert.True(player.IsSolidWith("contact_badger"));
            Assert.False(player.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void Increased_Enforcement_after_complete_keeps_Job_Contact_Solid()
        {
            // Warrant after Illegal complete is not "while working" — Losing Rep does not fire.
            // Zero Tolerance still strips Harken (covered in ZeroToleranceTests).
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    Rng = new SystemRng(1),
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    HavenChoices = new Dictionary<string, string> { ["p1"] = Bernadette }
                });
            var player = game.CurrentPlayer;
            player.Warrants = 0;
            player.SectorId = Santo;

            player.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out var err), err);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out var last), last);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(1, player.Warrants);
            Assert.True(player.IsSolidWith("contact_badger"));
        }

        [Fact]
        public void Goal_warrant_does_not_apply_Losing_Rep()
        {
            // Director's Cut p.25: Working a Goal is different than Working a Job.
            // Losing Rep names Jobs for a Contact — Goals are out of scope.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var leaders = LeaderCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", Silverhold, cash: 10000, fuel: 3);
            Assert.True(player.Roster.TryHire(leaders.Get("leader_malcolm"), out _));
            var game = new GameState(map, new[] { player })
            {
                Scenario = ScenarioCatalog.LoadDefault().Get("scenario_patiences-war"),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Misbehave = MisbehaveDeck.FromCatalog(MisbehaveCatalog.LoadDefault(), new SystemRng(1)),
                Tokens = MapTokens.None.WithAllianceCruiser(Londinium)
            };
            player.BecomeSolid("contact_patience");
            player.BecomeSolid("contact_mr-universe");
            foreach (var id in new[] { "crew_zoe", "crew_jayne", "crew_wash", "crew_kaylee" })
                Assert.True(player.Roster.TryHire(game.Crew!.Get(id), out _));

            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            for (var i = 0; i < 3; i++)
                Assert.True(new WorkAction().TryProceedMisbehave(game, "p1", proceed: true, out _, out var mb), mb);

            Assert.True(new GoalWorkAction().TryResume(
                game, "p1", out var done, out var skillErr,
                new GoalWorkChoice
                {
                    Rng = ScriptedRng.FromDieFaces(1, 1, 1, 1, 1, 1, 1, 1),
                    Kill = new KillChoice
                    {
                        VictimCrewIds = new List<string> { "crew_zoe", "crew_jayne" }
                    }
                }), skillErr);
            Assert.Equal(GoalWorkKind.Botched, done!.Kind);
            Assert.Equal(1, player.Warrants);
            Assert.True(player.IsSolidWith("contact_patience"));
            Assert.True(player.IsSolidWith("contact_mr-universe"));
        }

        [Fact]
        public void Niska_Job_warrant_strips_Niska_Solid_and_keeps_Pound_of_Flesh()
        {
            // Losing Rep + Pound of Flesh both fire; PoF kills a Crew (Contacts.json / GF9).
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", "border-georgia-r1-01", cash: 500, fuel: 3);
            var jobs = JobCatalog.LoadDefault();
            var game = new GameState(map, new[] { player })
            {
                Jobs = jobs,
                Contacts = ContactCatalog.LoadDefault(),
                ContactDecks = new ContactDecks(jobs, new SystemRng(1)),
                Crew = CrewCatalog.LoadDefault()
            };

            JobCard? niskaJob = null;
            foreach (var job in jobs.Cards.Values)
            {
                if (ContactSolidBenefits.IsNiskaJob(job))
                {
                    niskaJob = job;
                    break;
                }
            }
            Assert.NotNull(niskaJob);

            ContactSolidBenefits.BecomeSolid(game, player, "contact_niska");
            ContactSolidBenefits.BecomeSolid(game, player, "contact_badger");

            // Losing Rep on shared hook (Pound of Flesh kill runs at Job abandon, not here).
            Assert.True(WarrantIssuer.TryIssue(
                game, player, out var error, workingJob: niskaJob), error);
            Assert.False(player.IsSolidWith("contact_niska"));
            Assert.True(player.IsSolidWith("contact_badger"));
            Assert.Equal(1, player.Warrants);
        }

        [Fact]
        public void Harken_Job_warrant_Losing_Rep_is_idempotent_with_Zero_Tolerance()
        {
            var game = NewCrimeGame();
            var player = game.CurrentPlayer;
            Assert.True(game.Jobs!.TryGet("job_harken_cargo-delivery-albion", out var harkenJob));
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");

            Assert.True(WarrantIssuer.TryIssue(
                game, player, out var error, workingJob: harkenJob), error);
            Assert.False(player.IsSolidWith("contact_harken"));
            Assert.Equal(1, player.Warrants);
        }
    }
}
