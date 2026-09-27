using System.Collections.Generic;
using System.Linq;
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
    /// GF9 / Director's Cut Working Goals + FAQ 4.1 p.7 (Rival Crew / Work Action scope).
    /// </summary>
    public class GoalWorkTests
    {
        private const string Silverhold = "border-heinlein-r1-02";
        private const string ThreeHills = "border-georgia-r3-07";
        private const string Deadwood = "rim-blue-sun-r3-04";
        private const string Valentine = "alliance-white-sun-r4-08";
        private const string Ariel = "alliance-white-sun-r4-15";
        private const string Jiangyin = "border-red-sun-r1-01";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Boros = "border-georgia-r2-03";

        private static GameState Story(string scenarioId, string sectorId, int cash = 10000)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var leaders = LeaderCatalog.LoadDefault();
            var player = new PlayerState("p1", "Mal", sectorId, cash: cash, fuel: 3);
            Assert.True(player.Roster.TryHire(leaders.Get("leader_malcolm"), out _));
            var game = new GameState(map, new[] { player })
            {
                Scenario = ScenarioCatalog.LoadDefault().Get(scenarioId),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Misbehave = MisbehaveDeck.FromCatalog(MisbehaveCatalog.LoadDefault(), new SystemRng(1)),
                Bounties = BountyCatalog.LoadDefault(),
                Tokens = MapTokens.None.WithAllianceCruiser(Londinium)
            };
            return game;
        }

        private static void SolidPatienceMrU(PlayerState player)
        {
            player.BecomeSolid("contact_patience");
            player.BecomeSolid("contact_mr-universe");
        }

        private static void HireFighters(GameState game, int count)
        {
            var crew = game.Crew!;
            var ids = new[] { "crew_zoe", "crew_jayne", "crew_wash", "crew_book", "crew_simon" };
            var hired = 0;
            foreach (var id in ids)
            {
                if (hired >= count)
                    break;
                if (crew.TryGet(id, out var card) && game.CurrentPlayer.Roster.TryHire(card, out _))
                    hired++;
            }
        }

        private static void ForceMisbehaveProceed(GameState game, int count)
        {
            var work = new WorkAction();
            for (var i = 0; i < count; i++)
                Assert.True(work.TryProceedMisbehave(game, "p1", proceed: true, out _, out var err), err);
        }

        private static string FirstNeighbor(GameState game, string sectorId) =>
            game.Map.Neighbors(sectorId).First();

        private static KillChoice KillNonLeaders(PlayerState player, int n)
        {
            var ids = new List<string>();
            foreach (var m in player.Roster.Members)
            {
                if (m.IsLeader)
                    continue;
                ids.Add(m.Id);
                if (ids.Count >= n)
                    break;
            }
            return new KillChoice { VictimCrewIds = ids };
        }

        [Fact]
        public void Scenario_catalog_loads_workable_goal_fields()
        {
            var war = ScenarioCatalog.LoadDefault().Get("scenario_patiences-war");
            Assert.True(war.GoalWorkRequiresSolidPatienceAndMrUniverse);
            var g1 = war.Goal(1)!;
            Assert.True(g1.IsWorkable);
            Assert.Equal(3, g1.Misbehave);
            Assert.Equal(Skill.Fight, g1.Skill);
            Assert.Equal(8, g1.Target);
            Assert.Equal("Silverhold", g1.Location);

            var king = ScenarioCatalog.LoadDefault().Get("scenario_king-of-all-londinium").Goal(1)!;
            Assert.Equal(3, king.Bands.Count);
            Assert.Contains("Pay $5,000", king.Bands[2].Text);

            var jail = ScenarioCatalog.LoadDefault().Get("scenario_jail-break").Goal(1)!;
            Assert.NotNull(jail.Boarding);
            Assert.Equal(1, jail.Boarding!.Dice);
            Assert.Contains("Harken", jail.RequiresSolidWith);
        }

        [Fact]
        public void Patience_war_requires_solid_and_order()
        {
            var game = Story("scenario_patiences-war", Silverhold);
            var work = new WorkAction();
            Assert.False(work.TryWorkGoal(game, "p1", 1, out _, out var error));
            Assert.Contains("Patience", error);

            SolidPatienceMrU(game.CurrentPlayer);
            Assert.False(work.TryWorkGoal(game, "p1", 2, out _, out var orderErr));
            Assert.Contains("Goal 1", orderErr);
        }

        [Fact]
        public void Patience_war_goal1_completes_after_misbehave_and_fight()
        {
            var game = Story("scenario_patiences-war", Silverhold);
            SolidPatienceMrU(game.CurrentPlayer);
            HireFighters(game, 4);
            var work = new WorkAction();
            Assert.True(work.TryWorkGoal(game, "p1", 1, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(game.WorkGearLocked);
            Assert.Equal(3, game.PendingMisbehave!.Remaining);
            Assert.True(game.PendingMisbehave.IsGoalWork);

            ForceMisbehaveProceed(game, 3);
            Assert.Null(game.PendingMisbehave);
            Assert.Equal(GoalWorkPhase.Skill, game.PendingGoalWork!.Phase);

            var evadeTo = FirstNeighbor(game, Silverhold);
            var goals = new GoalWorkAction();
            Assert.True(goals.TryResume(
                game, "p1", out var done, out var skillErr,
                new GoalWorkChoice
                {
                    Rng = ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 6, 6),
                    Kill = KillNonLeaders(game.CurrentPlayer, 1),
                    EvadeToSectorId = evadeTo
                }), skillErr);

            // Success: Kill 1 & Goal Complete — no Evade on G1 success text.
            Assert.Equal(GoalWorkKind.Completed, done!.Kind);
            Assert.Contains(1, game.CurrentPlayer.CompletedGoals);
            Assert.Equal(1, game.CurrentPlayer.GoalTokens);
            Assert.False(game.WorkGearLocked);
            Assert.Null(game.PendingGoalWork);
        }

        [Fact]
        public void TryCompleteGoal_always_grants_a_token()
        {
            var game = Story("scenario_patiences-war", Silverhold);
            SolidPatienceMrU(game.CurrentPlayer);
            Assert.True(WinCheck.TryCompleteGoal(game, game.CurrentPlayer, 1, out var error), error);
            Assert.Equal(1, game.CurrentPlayer.GoalTokens);
        }

        [Fact]
        public void Goal_misbehave_warrant_ends_attempt_without_job_abandon()
        {
            var game = Story("scenario_patiences-war", Silverhold);
            SolidPatienceMrU(game.CurrentPlayer);
            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            game.CurrentPlayer.Warrants = 0;
            // Simulate Misbehave Warrant Issued path for Goals.
            game.CurrentPlayer.Warrants++;
            game.PendingMisbehave = null;
            game.PendingGoalWork = null;
            game.WorkGearLocked = false;
            game.TryConsumeAction(TurnAction.Work, out _);
            Assert.Equal(1, game.CurrentPlayer.Warrants);
            Assert.Empty(game.CurrentPlayer.CompletedGoals);
        }

        [Fact]
        public void Rival_crew_deal_option_illegal_on_goals()
        {
            var game = Story("scenario_patiences-war", Silverhold);
            SolidPatienceMrU(game.CurrentPlayer);
            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            var catalog = MisbehaveCatalog.LoadDefault();
            var card = catalog.Get("misbehave_a-rival-crew");
            game.PendingMisbehave!.FaceUp = card;
            var resolver = new MisbehaveResolver();
            // Option 1 = Maybe We Can Make a Deal (halve pay) — FAQ illegal on Goals.
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 1 }, out _, out var optErr,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("not legal while Working a Goal", optErr);
        }

        [Fact]
        public void Harken_folly_crying_wolf_moves_cruiser_and_evades()
        {
            var game = Story("scenario_harkens-folly", Valentine);
            game.CurrentPlayer.CompletedGoals.Add(1);
            game.CurrentPlayer.GoalTokens = 1;
            HireFighters(game, 3);
            // Boost Tech.
            game.CurrentPlayer.TechBonus = 10;
            var work = new WorkAction();
            Assert.True(work.TryWorkGoal(game, "p1", 2, out _, out var error), error);
            ForceMisbehaveProceed(game, 3);
            var evadeTo = FirstNeighbor(game, Valentine);
            Assert.True(new GoalWorkAction().TryResume(
                game, "p1", out var done, out var skillErr,
                new GoalWorkChoice
                {
                    Rng = ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6),
                    EvadeToSectorId = evadeTo
                }), skillErr);
            Assert.Equal(GoalWorkKind.Completed, done!.Kind);
            Assert.Equal(Valentine, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(evadeTo, game.CurrentPlayer.SectorId);
            Assert.Contains(2, game.CurrentPlayer.CompletedGoals);
            Assert.Equal(2, game.CurrentPlayer.GoalTokens);
        }

        [Fact]
        public void King_goal1_pay_or_botch_completes_when_paying()
        {
            var game = Story("scenario_king-of-all-londinium", Jiangyin, cash: 20000);
            HireFighters(game, 2);
            game.CurrentPlayer.TalkBonus = 20;
            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            ForceMisbehaveProceed(game, 2);
            var goals = new GoalWorkAction();
            // High roll → 9+ band: Pay $5,000 to Complete or Botch.
            Assert.False(goals.TryResume(
                game, "p1", out _, out var needPay,
                new GoalWorkChoice { Rng = ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 6, 6) }));
            Assert.Equal(PendingChoiceKinds.GoalPayOrBotch, game.PendingChoice!.Kind);
            Assert.True(goals.TryResumePayOrBotch(
                game,
                new ChoiceSubmission { SelectedOptionId = GoalPayOrBotchOptions.PayComplete },
                out var done, out var payErr), payErr);
            Assert.Equal(GoalWorkKind.Completed, done!.Kind);
            Assert.Equal(15000, game.CurrentPlayer.Cash);
            Assert.Contains(1, game.CurrentPlayer.CompletedGoals);
        }

        [Fact]
        public void Jail_break_boarding_fail_botches()
        {
            var game = Story("scenario_jail-break", Londinium);
            game.CurrentPlayer.BecomeSolid("contact_harken");
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out var start, out var error), error);
            Assert.False(start!.AwaitingMisbehave);
            Assert.Equal(GoalWorkPhase.Boarding, game.PendingGoalWork!.Phase);
            Assert.True(new GoalWorkAction().TryResume(
                game, "p1", out var done, out var boardErr,
                new GoalWorkChoice { Rng = ScriptedRng.FromDieFaces(3) }), boardErr);
            Assert.Equal(GoalWorkKind.Botched, done!.Kind);
            Assert.Empty(game.CurrentPlayer.CompletedGoals);
        }

        [Fact]
        public void Jail_break_boarding_then_fight_success_rescues_and_evades()
        {
            var game = Story("scenario_jail-break", Londinium);
            game.CurrentPlayer.BecomeSolid("contact_harken");
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.JailBreakPrisonerBountyId = "bounty_billy";
            HireFighters(game, 4);
            game.CurrentPlayer.FightBonus = 10;
            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            var evadeTo = FirstNeighbor(game, Londinium);
            // Boarding die 6, then Fight dice all 6s.
            Assert.True(new GoalWorkAction().TryResume(
                game, "p1", out var done, out var skillErr,
                new GoalWorkChoice
                {
                    Rng = ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 6, 6, 6),
                    Kill = KillNonLeaders(game.CurrentPlayer, 1),
                    EvadeToSectorId = evadeTo
                }), skillErr);
            Assert.Equal(GoalWorkKind.Completed, done!.Kind);
            Assert.Equal(evadeTo, game.CurrentPlayer.SectorId);
            Assert.Equal(1, game.CurrentPlayer.Warrants);
            Assert.True(BoundFugitives.HasBound(game.CurrentPlayer));
            Assert.Contains(1, game.CurrentPlayer.CompletedGoals);
        }

        [Fact]
        public void Gear_lock_blocks_switch_during_goal_work()
        {
            var game = Story("scenario_patiences-war", Silverhold);
            SolidPatienceMrU(game.CurrentPlayer);
            Assert.True(new WorkAction().TryWorkGoal(game, "p1", 1, out _, out var error), error);
            Assert.True(game.WorkGearLocked);
            Assert.False(Abilities.GearCarriage.TryAssign(
                game, game.CurrentPlayer, "gear_pistol", "crew_zoe", out var gearErr));
            Assert.Contains("Work", gearErr);
        }
    }
}
