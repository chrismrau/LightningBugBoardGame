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
    /// GF9 p.16 / Director's Cut p.24 Zero Tolerance (Harken).
    /// FAQ 4.1 p.10 Helmsman / Alliance Ident still count as Solid (token loss only).
    /// </summary>
    public class ZeroToleranceTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Albion = "alliance-white-sun-r4-11";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string Silverhold = "border-heinlein-r1-02";

        private static GameState NewGame(string sectorId = Persephone, int cash = 5000)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: cash, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            game.Crew = CrewCatalog.LoadDefault();
            return game;
        }

        [Fact]
        public void WarrantIssuer_strips_Harken_Solid_token()
        {
            // GF9 p.16: Receiving a Warrant for any reason → reputation loss with Harken.
            var game = NewGame();
            var player = game.CurrentPlayer;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            ContactSolidBenefits.BecomeSolid(game, player, "contact_amnon-duul");
            Assert.True(player.IsSolidWith("contact_harken"));

            Assert.True(WarrantIssuer.TryIssue(game, player, out var error), error);
            Assert.Equal(1, player.Warrants);
            Assert.False(player.IsSolidWith("contact_harken"));
            Assert.True(player.IsSolidWith("contact_amnon-duul"));
        }

        [Fact]
        public void CardEffectApplicator_WarrantIssued_uses_Zero_Tolerance()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");

            Assert.True(CardEffectApplicator.TryApply(
                game, player,
                new List<CardEffect> { new CardEffect(CardEffectType.WarrantIssued) },
                ScriptedRng.FromDieFaces(1),
                new CardEffectContext(CardEffectSource.Misbehave),
                out var result, out var error), error);
            Assert.Equal(1, result.WarrantsIssued);
            Assert.Equal(1, player.Warrants);
            Assert.False(player.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void BecomeSolid_Harken_blocked_while_Warrants_held()
        {
            // GF9 p.16: You may not become Solid with Harken while you have a Warrant.
            var game = NewGame();
            var player = game.CurrentPlayer;
            player.Warrants = 1;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.False(player.IsSolidWith("contact_harken"));
            Assert.True(ContactSolidBenefits.BlocksBecomeSolidHarken(game, player, "contact_harken"));

            player.Warrants = 0;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.True(player.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void Starting_warrants_block_BecomeSolid_Harken()
        {
            // Christopher lock: gate applies from setup until warrants cleared.
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Bernadette) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    Rng = new SystemRng(1),
                    ScenarioCardId = "scenario_any-port-in-a-storm",
                    HavenChoices = new Dictionary<string, string> { ["p1"] = Bernadette }
                });
            Assert.True(game.CurrentPlayer.Warrants >= 1);
            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, "contact_harken");
            Assert.False(game.CurrentPlayer.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void Increased_Enforcement_strips_Harken_Solid_after_illegal_complete()
        {
            // ScenarioCards.json Increased Enforcement + Zero Tolerance "any reason".
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
            // Clear starting warrant so BecomeSolid can grant Harken, then Illegal complete re-issues.
            player.Warrants = 0;
            player.SectorId = Santo;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.True(player.IsSolidWith("contact_harken"));

            player.JobHand.Add("job_badger_badgers-11-casino-caper");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", "job_badger_badgers-11-casino-caper", out _, out var err), err);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out var last), last);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(1, player.Warrants);
            Assert.False(player.IsSolidWith("contact_harken"));
            Assert.True(player.IsSolidWith("contact_badger"));
        }

        [Fact]
        public void BecomeSolid_then_IssueWarrant_strips_just_granted_Harken_Solid()
        {
            // Christopher lock Q4-A: Solid grant then IssueWarrant → Zero Tolerance strips immediately.
            // (No Illegal Harken jobs in Jobs.json; order is encoded by the shared hook.)
            var game = NewGame();
            var player = game.CurrentPlayer;
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.True(player.IsSolidWith("contact_harken"));
            Assert.True(WarrantIssuer.TryIssue(game, player, out var error), error);
            Assert.False(player.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void Goal_warrant_strips_Harken_but_keeps_Patience_and_Mr_Universe()
        {
            // Patience's War Proving Your Worth + Zero Tolerance.
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
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            foreach (var id in new[] { "crew_zoe", "crew_jayne", "crew_wash", "crew_kaylee" })
                Assert.True(player.Roster.TryHire(game.Crew!.Get(id), out _));

            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            for (var i = 0; i < 3; i++)
                Assert.True(new WorkAction().TryProceedMisbehave(game, "p1", proceed: true, out _, out var mb), mb);

            // Fail Fight 8 → Warrant Issued.
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
            Assert.False(player.IsSolidWith("contact_harken"));
        }

        [Fact]
        public void Helmsman_stays_on_roster_when_Harken_Solid_token_is_lost()
        {
            // FAQ 4.1 p.10: Helmsman / Ident still count as Solid — Zero Tolerance loses the token only.
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_helmsman_breakinatmo"), out _));
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.True(WarrantIssuer.TryIssue(game, player, out var error), error);
            Assert.False(player.IsSolidWith("contact_harken"));
            Assert.NotNull(player.Roster.Find("crew_helmsman_breakinatmo"));
        }

        [Fact]
        public void Completing_Harken_job_while_Warranted_does_not_grant_Solid()
        {
            var game = NewGame(Albion);
            var player = game.CurrentPlayer;
            player.Warrants = 1;
            const string jobId = "job_harken_cargo-delivery-albion";
            player.JobHand.Add(jobId);
            player.ActiveJobs.Add(new ActiveJob(jobId) { PickedUp = true, Cargo = 2 });
            player.Cargo = 2;
            Assert.True(new WorkAction().TryWork(game, "p1", jobId, out var done, out var error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.False(player.IsSolidWith("contact_harken"));
        }
    }
}
