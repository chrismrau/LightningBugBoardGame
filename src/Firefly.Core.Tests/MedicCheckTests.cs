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
    public class MedicCheckTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";
        private const string Hera = "border-murphy-r1-02";
        private const string Scrap = "job_amnon-duul_haulin-military-scrap";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();

        private static GameState NewGame(string sectorId = Persephone)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: 0, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.Crew = Crew;
            return game;
        }

        [Fact]
        public void Medic_check_5_returns_crew_to_ship()
        {
            // GF9: "5-6: Crew is Returned to the Ship."
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var kaylee = player.Roster.Find("crew_kaylee")!;

            var result = CrewKill.Apply(game, player, kaylee, ScriptedRng.FromDieFaces(5));
            Assert.Equal(CrewOutcome.ReturnedToShip, result.Outcome);
            Assert.True(result.MedicSaved);
            Assert.Equal(5, result.MedicDie);
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
            Assert.DoesNotContain("crew_kaylee", game.RemovedFromPlay);
        }

        [Fact]
        public void Medic_check_4_removes_crew_from_play()
        {
            // GF9: "1-4: Crew Dies, Remove from Play"
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var kaylee = player.Roster.Find("crew_kaylee")!;

            var result = CrewKill.Apply(game, player, kaylee, ScriptedRng.FromDieFaces(4));
            Assert.Equal(CrewOutcome.Killed, result.Outcome);
            Assert.False(result.MedicSaved);
            Assert.True(result.MedicAttempted);
            Assert.Null(player.Roster.Find("crew_kaylee"));
            Assert.Contains("crew_kaylee", game.RemovedFromPlay);
        }

        [Fact]
        public void Medic_may_check_when_they_are_the_one_at_risk()
        {
            // GF9: "Medics may make a Medic Check even if they are the ones at risk of dying."
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            var doralee = player.Roster.Find("crew_doralee")!;

            var result = CrewKill.Apply(game, player, doralee, ScriptedRng.FromDieFaces(6));
            Assert.Equal(CrewOutcome.ReturnedToShip, result.Outcome);
            Assert.NotNull(player.Roster.Find("crew_doralee"));
        }

        [Fact]
        public void Kill_up_to_makes_one_medic_check_per_victim()
        {
            // GF9: "Only make one Medic Check per Crew Killed, regardless of how many Medics…"
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_wash"), out _));

            var choice = new KillChoice
            {
                VictimCrewIds = new List<string> { "crew_wash", "crew_kaylee" }
            };
            Assert.Equal(1, CrewKill.KillUpTo(game, player, 2, ScriptedRng.FromDieFaces(3, 5), choice));
            Assert.Contains("crew_wash", game.RemovedFromPlay);
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
            Assert.NotNull(player.Roster.Find("crew_doralee"));
            Assert.Equal(2, player.Roster.Count);
        }

        [Fact]
        public void Kill_up_to_without_victims_suspends_PendingChoice()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_zoe"), out _));

            Assert.False(CrewKill.TryKillUpTo(
                game, player, 1, new SystemRng(1), out var killed, out var error));
            Assert.Equal(0, killed);
            Assert.Contains("Choose which crew", error);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);
            Assert.Equal("1", game.PendingChoice.ContextId);
            Assert.Equal(3, player.Roster.Count);
        }

        [Fact]
        public void Kill_up_to_resume_PendingChoice_applies_medic_on_chosen_victims()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_wash"), out _));

            Assert.False(CrewKill.TryKillUpTo(
                game, player, 2, ScriptedRng.FromDieFaces(3, 5), out _, out _));

            Assert.True(CrewKill.TryResumeKillUpTo(
                game,
                new ChoiceSubmission
                {
                    Values = new List<string> { "crew_wash", "crew_kaylee" }
                },
                ScriptedRng.FromDieFaces(3, 5),
                out var killed,
                out var error), error);
            Assert.Equal(1, killed);
            Assert.Null(game.PendingChoice);
            Assert.Contains("crew_wash", game.RemovedFromPlay);
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
            Assert.NotNull(player.Roster.Find("crew_doralee"));
        }

        [Fact]
        public void Kill_up_to_whole_roster_needs_no_PendingChoice()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_jayne"), out _));

            Assert.True(CrewKill.TryKillUpTo(
                game, player, 2, new SystemRng(1), out var killed, out var error), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(2, killed);
            Assert.Equal(0, player.Roster.Count);
        }

        [Fact]
        public void Leader_may_be_chosen_as_kill_victim_then_Really_Lucky()
        {
            // FAQ 4.1 p.3: Leader can take the hit; fail Medic → Really Lucky Disgruntle.
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));

            var choice = new KillChoice
            {
                VictimCrewIds = new List<string> { player.Roster.Leader!.Id }
            };
            Assert.Equal(0, CrewKill.KillUpTo(game, player, 1, ScriptedRng.FromDieFaces(2), choice));
            Assert.True(player.Roster.Leader!.Disgruntled);
            Assert.Equal(3, player.Roster.Count);
        }

        [Fact]
        public void Simon_adds_plus_two_to_medic_checks()
        {
            // Supplies.tsv: Simon Tam "I am very smart: +2 to Medic Checks."
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_simon-tam"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var kaylee = player.Roster.Find("crew_kaylee")!;

            // Die 3 + Simon 2 = 5 → save
            var result = CrewKill.Apply(game, player, kaylee, ScriptedRng.FromDieFaces(3));
            Assert.Equal(CrewOutcome.ReturnedToShip, result.Outcome);
            Assert.Equal(5, result.MedicTotal);
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void Leader_medic_success_returns_unscathed()
        {
            // FAQ 4.1 p.3: successful Medic Check → Leader returns unscathed (no Disgruntle).
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));

            var result = CrewKill.Apply(game, player, player.Roster.Leader!, ScriptedRng.FromDieFaces(5));
            Assert.Equal(CrewOutcome.ReturnedToShip, result.Outcome);
            Assert.False(player.Roster.Leader!.Disgruntled);
            Assert.Equal(2, player.Roster.Count);
        }

        [Fact]
        public void Leader_medic_fail_applies_really_lucky()
        {
            // FAQ 4.1 p.3: fail Medic → Leaders are REALLY Lucky (Disgruntle).
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));

            var result = CrewKill.Apply(game, player, player.Roster.Leader!, ScriptedRng.FromDieFaces(2));
            Assert.Equal(CrewOutcome.Disgruntled, result.Outcome);
            Assert.True(result.MedicAttempted);
            Assert.False(result.MedicSaved);
            Assert.True(player.Roster.Leader!.Disgruntled);
            Assert.Equal(2, player.Roster.Count);
        }

        [Fact]
        public void No_medic_kills_without_a_check()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var kaylee = player.Roster.Find("crew_kaylee")!;

            var result = CrewKill.Apply(game, player, kaylee, ScriptedRng.FromDieFaces(6));
            Assert.Equal(CrewOutcome.Killed, result.Outcome);
            Assert.False(result.MedicAttempted);
            Assert.Contains("crew_kaylee", game.RemovedFromPlay);
        }

        [Fact]
        public void Victim_choice_hook_selects_named_crew()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_zoe"), out _));

            var choice = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } };
            Assert.Equal(1, CrewKill.KillUpTo(game, player, 1, new SystemRng(1), choice));
            Assert.Null(player.Roster.Find("crew_kaylee"));
            Assert.NotNull(player.Roster.Find("crew_jayne"));
            Assert.NotNull(player.Roster.Find("crew_zoe"));
        }

        [Fact]
        public void Count_as_successful_medic_hook_saves_without_roll()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var choice = new KillChoice { CountAsSuccessfulMedicCheck = true };

            var result = CrewKill.Apply(
                game, player, player.Roster.Find("crew_kaylee")!, new SystemRng(1), choice);
            Assert.Equal(CrewOutcome.ReturnedToShip, result.Outcome);
            Assert.True(result.MedicSaved);
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void Misbehave_kill_runs_medic_check()
        {
            var game = NewGame(Santo);
            game.CurrentPlayer.Cash = 500;
            game.CurrentPlayer.JobHand.Add(Crime);
            game.ContactDecks = new ContactDecks(game.Jobs!, new SystemRng(1));
            game.Gear = GearIndex.LoadDefault();
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);

            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            game.CurrentPlayer.FightBonus = 1;
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out _));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Skill die 1 (Ambush kill band), then PendingChoice for victim, Medic die 5.
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var suspendError,
                ScriptedRng.FromDieFaces(1)));
            Assert.Contains("Choose which crew", suspendError);
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);

            Assert.True(resolver.TryResumeKillVictims(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_kaylee" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Equal(0, resolution!.CrewKilled);
            Assert.Equal(2, game.CurrentPlayer.Roster.Count);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Reaver_contact_kill_runs_medic_check()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { CutterStart });
            // Zero Fight so ScriptedRng faces are only Medic Checks.
            var player = new PlayerState("p1", "Mal", CutterStart)
            {
                Passengers = 1,
                Fugitives = 0,
                FightBonus = 0
            };
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_wash"), out _));
            var game = new GameState(map, new[] { player }, tokens);
            game.PendingEncounter = TokenKind.ReaverCutter;
            game.PendingEncounterSectorId = CutterStart;

            // Fight fail → Kill 2; suspend for victims.
            Assert.False(ReaverContact.TryResolve(
                game,
                ScriptedRng.FromDieFaces(),
                CutterAdjacent,
                out _,
                out var suspendError));
            Assert.Contains("Choose which crew", suspendError);
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);

            // Medic 5 save (wash), 3 fail (kaylee).
            Assert.True(ReaverContact.TryResumeKillVictims(
                game,
                new ChoiceSubmission
                {
                    Values = new List<string> { "crew_wash", "crew_kaylee" }
                },
                ScriptedRng.FromDieFaces(5, 3),
                out var result,
                out var error), error);
            Assert.Equal(1, result!.CrewKilled);
            Assert.Equal(2, player.Roster.Count);
            Assert.Contains("crew_kaylee", game.RemovedFromPlay);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Mechanic_profession_parts_bonus_on_job_complete()
        {
            // GF9 p.15: Bonus Tab once; "Mechanic +1 Part" is Parts not cash.
            var game = NewGame(Hera);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            game.CurrentPlayer.JobHand.Add(Scrap);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Scrap, out _, out var error), error);
            Assert.Equal(2, game.CurrentPlayer.Cargo);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Persephone;
            game.CurrentPlayer.Parts = 0;
            Assert.True(work.TryWork(game, "p1", Scrap, out var done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(1500, done.Pay);
            Assert.Equal(1, game.CurrentPlayer.Parts);
        }
    }
}
