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
    /// River Gifted printed bands + Misbehave wiring.
    /// FAQ 4.1: roll after option / before test; Simon +2 after die (band from die only).
    /// </summary>
    public class GiftedRollTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        [Fact]
        public void Misbehave_Gifted_1_2_returns_River_to_ship_before_test()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_river-tam"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_jayne"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Gifted 1 → Return to Ship before Fight; Jayne's dice then resolve Ambush kill band.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    Kill = new KillChoice { VictimCrewIds = new List<string> { "crew_jayne" } }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1, 1)), error);

            // River Returned (not killed); Jayne was the kill victim; warrant discards Job.
            Assert.True(game.CurrentPlayer.Roster.HasName("River Tam"));
            Assert.False(game.CurrentPlayer.Roster.HasName("Jayne"));
            Assert.Equal(1, resolution!.CrewKilled);
            Assert.Equal(1, resolution.WarrantsIssued);
        }

        [Fact]
        public void Misbehave_Gifted_Fight_adds_dice_to_Fight_test()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_river-tam"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Gifted 3 → Fight 2; two Fight dice → Ambush 8+ Botched.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(3, 6, 6)), error);

            Assert.False(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_river-tam"));
            Assert.Equal(2, resolution!.SkillCheck!.Roll.Faces.Count);
            Assert.Equal(12, resolution.SkillCheck.Total);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
        }

        [Fact]
        public void Misbehave_Gifted_with_Simon_adds_four_Fight_dice_on_band_3()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_river-tam"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_simon-tam"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Gifted 3 → 2 Fight + Simon 2 = 4 dice.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    Kill = new KillChoice { VictimCrewIds = new List<string> { "crew_simon-tam" } }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(3, 1, 1, 1, 1)), error);

            Assert.Equal(4, resolution!.SkillCheck!.Roll.Faces.Count);
            Assert.Equal(4, resolution.SkillCheck.Total);
            Assert.Equal(1, resolution.WarrantsIssued);
        }

        [Fact]
        public void Misbehave_Gifted_band_6_suspends_skill_pick_then_adds_dice()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_river-tam"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("Gifted", error);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.GiftedSkill, game.PendingChoice!.Kind);

            Assert.True(resolver.TryResumeGiftedSkill(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = GiftedSkillOptions.Fight },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out error,
                ScriptedRng.FromDieFaces(6, 6, 6)), error);

            Assert.Equal(3, resolution!.SkillCheck!.Roll.Faces.Count);
            Assert.Equal(18, resolution.SkillCheck.Total);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
        }

        private static GameState NewCrimeGame(int cash = 500)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: cash, fuel: 3);
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
    }
}
