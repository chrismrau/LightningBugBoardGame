using System.Collections.Generic;
using Firefly.Core.Abilities;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// PendingChoice consumer (c): Bribe $ amount and Med Foam discard-to-succeed.
    /// GF9 p.6 Bribes; Supplies.tsv Med Foam; GF9 p.18–19 Medic Check.
    /// </summary>
    public class BribeMedFoamChoiceTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";
        private const string MedFoamId = "gear_med-foam_kalidasa";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();

        private static GameState NewCrimeGame(int cash = 500)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: cash, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            game.Crew = Crew;
            game.Gear = GearIndex.LoadDefault();
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);
            return game;
        }

        private static void StartCrime(GameState game)
        {
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out _));
        }

        private static GameState NewKillGame()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 3);
            var game = new GameState(map, new[] { player })
            {
                Gear = GearIndex.LoadDefault()
            };
            game.Crew = Crew;
            return game;
        }

        private static void GiveCarriedMedFoam(GameState game, PlayerState player, string carrierId)
        {
            player.Gear.Add(MedFoamId);
            Assert.True(GearCarriage.TryAssign(game, player, MedFoamId, carrierId, out var error), error);
        }

        [Fact]
        public void Misbehave_bribes_suspends_PendingChoice_when_affordable()
        {
            // GF9 p.6: "Before you roll a dice, you may choose to pay Bribes."
            var game = NewCrimeGame(cash: 500);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_backwater-deputies"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error,
                ScriptedRng.FromDieFaces(5)));
            Assert.Contains("Bribes", error);
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);
            Assert.Equal(500, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Misbehave_bribes_resume_pay_applies_bonus()
        {
            var game = NewCrimeGame(cash: 500);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_backwater-deputies"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(5)));

            // Negotiate 9: die 5 + bribe +4 = 9 Proceed.
            Assert.True(resolver.TryResumeBribeAmount(
                game, "p1",
                new ChoiceSubmission { Amount = 400 },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(400, resolution.SkillCheck!.BribeDollarsPaid);
            Assert.Equal(100, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Misbehave_bribes_resume_decline_pays_zero()
        {
            var game = NewCrimeGame(cash: 500);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_backwater-deputies"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(5)));

            Assert.True(resolver.TryResumeBribeAmount(
                game, "p1",
                new ChoiceSubmission { Amount = 0 },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Equal(0, resolution!.SkillCheck!.BribeDollarsPaid);
            Assert.Equal(500, game.CurrentPlayer.Cash);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
        }

        [Fact]
        public void Misbehave_bribes_unaffordable_auto_declines()
        {
            var game = NewCrimeGame(cash: 50);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_backwater-deputies"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(0, resolution!.SkillCheck!.BribeDollarsPaid);
            Assert.Equal(50, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Nav_bribes_suspends_and_resume_keeps_flying()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Persephone, cash: 300, fuel: 3)
            {
                TalkBonus = 1
            };
            var game = new GameState(map, new[] { player }, decks: decks);
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            var resolver = new NavResolver();
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_local-tariff-patrol"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, 0, out _, out var suspendError, ScriptedRng.FromDieFaces(6)));
            Assert.Contains("Bribes", suspendError);
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);

            // Talk 9: die 6 + bribe +3 = 9 Keep Flying.
            Assert.True(resolver.TryResumeBribeAmount(
                game,
                new ChoiceSubmission { Amount = 300 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Null(game.PendingChoice);
            Assert.True(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.KeepFlying, resolution.Outcome);
            Assert.Equal(0, player.Cash);
        }

        [Fact]
        public void Med_foam_suspends_when_carried_and_Medic_present()
        {
            // Supplies.tsv: "Discard to count as having made a successful Medic Check."
            var game = NewKillGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            GiveCarriedMedFoam(game, player, "crew_doralee");

            var choice = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } };
            Assert.False(CrewKill.TryKillUpTo(
                game, player, 1, ScriptedRng.FromDieFaces(1), out _, out var error, choice));
            Assert.Contains("Med Foam", error);
            Assert.Equal(PendingChoiceKinds.MedFoamDiscard, game.PendingChoice!.Kind);
            Assert.Contains(MedFoamId, player.Gear);
            Assert.Equal(2, player.Roster.Count);
        }

        [Fact]
        public void Med_foam_resume_discard_saves_without_roll()
        {
            var game = NewKillGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            GiveCarriedMedFoam(game, player, "crew_doralee");

            var choice = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } };
            Assert.False(CrewKill.TryKillUpTo(
                game, player, 1, new SystemRng(1), out _, out _, choice));

            Assert.True(CrewKill.TryResumeMedFoamKillUpTo(
                game,
                new ChoiceSubmission { SelectedOptionId = MedFoamDiscardOptions.Discard },
                new SystemRng(1),
                out var killed,
                out var error,
                choice), error);
            Assert.Equal(0, killed);
            Assert.Null(game.PendingChoice);
            Assert.DoesNotContain(MedFoamId, player.Gear);
            Assert.Contains(MedFoamId, game.RemovedFromPlay);
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void Med_foam_resume_decline_rolls_Medic_Check()
        {
            var game = NewKillGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            GiveCarriedMedFoam(game, player, "crew_doralee");

            var choice = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } };
            Assert.False(CrewKill.TryKillUpTo(
                game, player, 1, ScriptedRng.FromDieFaces(2), out _, out _, choice));

            Assert.True(CrewKill.TryResumeMedFoamKillUpTo(
                game,
                new ChoiceSubmission { SelectedOptionId = MedFoamDiscardOptions.Decline },
                ScriptedRng.FromDieFaces(2),
                out var killed,
                out var error,
                choice), error);
            Assert.Equal(1, killed);
            Assert.Contains(MedFoamId, player.Gear);
            Assert.Null(player.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void Med_foam_onboard_only_is_not_usable()
        {
            // FAQ 4.1 p.2: Onboard Ship Gear may not be used in any way.
            var game = NewKillGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            player.Gear.Add(MedFoamId); // owned but not carried

            var choice = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } };
            Assert.True(CrewKill.TryKillUpTo(
                game, player, 1, ScriptedRng.FromDieFaces(2), out var killed, out var error, choice),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(1, killed);
            Assert.Contains(MedFoamId, player.Gear);
        }

        [Fact]
        public void Misbehave_kill_then_Med_foam_PendingChoice()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Cash = 500;
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            GiveCarriedMedFoam(game, game.CurrentPlayer, "crew_doralee");
            game.CurrentPlayer.FightBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Skill die 1 → kill band → victim PendingChoice.
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(1)));
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);

            Assert.False(resolver.TryResumeKillVictims(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_kaylee" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var foamError,
                ScriptedRng.FromDieFaces(1)));
            Assert.Contains("Med Foam", foamError);
            Assert.Equal(PendingChoiceKinds.MedFoamDiscard, game.PendingChoice!.Kind);

            Assert.True(resolver.TryResumeMedFoam(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MedFoamDiscardOptions.Discard },
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    Kill = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(0, resolution!.CrewKilled);
            Assert.DoesNotContain(MedFoamId, game.CurrentPlayer.Gear);
            Assert.NotNull(game.CurrentPlayer.Roster.Find("crew_kaylee"));
        }
    }
}
