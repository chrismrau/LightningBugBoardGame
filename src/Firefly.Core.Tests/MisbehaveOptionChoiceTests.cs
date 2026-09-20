using System;
using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// PendingChoice consumer (d): Misbehave option pick + FIRST–NEXT mid-card suspend.
    /// GF9 p.14 — most Misbehave cards have 2 options; you may attempt either.
    /// </summary>
    public class MisbehaveOptionChoiceTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

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
            return game;
        }

        private static void StartCrime(GameState game)
        {
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out _));
        }

        [Fact]
        public void ParseFromDetails_splits_FIRST_NEXT_into_two_steps()
        {
            var details =
                "FIRST, Get Past the Front Desk: Negotiate 9 Bribes; 1-8 Attempt Botched. 9+ Proceed. "
                + "NEXT, Find the Terminal: Tech 7; 1-6 Warrant Issued. 7+ Proceed.";
            var steps = MisbehaveSteps.ParseFromDetails("Alter the Security Protocols", details);
            Assert.Equal(2, steps.Count);
            Assert.Equal("Get Past the Front Desk", steps[0].Name);
            Assert.Contains("Negotiate 9 Bribes", steps[0].Details);
            Assert.Equal("Find the Terminal", steps[1].Name);
            Assert.Contains("Tech 7", steps[1].Details);
        }

        [Fact]
        public void ParseFromDetails_single_option_stays_one_step()
        {
            var steps = MisbehaveSteps.ParseFromDetails(
                "Talk",
                "Negotiate 6; 1-5 Attempt Botched. 6+ Proceed.");
            Assert.Single(steps);
            Assert.Contains("Negotiate 6", steps[0].Details);
        }

        [Fact]
        public void Multi_option_card_suspends_PendingChoice_when_OptionIndex_unset()
        {
            // GF9 p.14: "most Misbehave Cards have 2 options on each card. You may attempt either option."
            // Option 0 Requires FANCY DUDS — only legal option listed is "1".
            var game = NewCrimeGame();
            game.CurrentPlayer.TalkBonus = 2;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice(),
                out _, out var error,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("option", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PendingChoiceKinds.MisbehaveOption, game.PendingChoice!.Kind);
            Assert.Equal(new[] { "1" }, game.PendingChoice.Options);
            Assert.NotNull(game.PendingMisbehave!.FaceUp);
        }

        [Fact]
        public void Multi_option_resume_resolves_chosen_option()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.TalkBonus = 2;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice(),
                out _, out _,
                ScriptedRng.FromDieFaces(6)));

            // Option 1: Work the Crowd — Negotiate 8; die 6 + Talk 2 = 8 Proceed.
            Assert.True(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = "1" },
                new MisbehaveChoice(),
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal("Work the Crowd", resolution.Option!.Name);
        }

        [Fact]
        public void Explicit_OptionIndex_skips_option_PendingChoice()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.TalkBonus = 2;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
        }

        [Fact]
        public void Single_option_card_auto_selects_without_PendingChoice()
        {
            var game = NewCrimeGame();
            StartCrime(game);
            // Alliance Alert! — single option.
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-alert"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice(),
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Null(game.PendingChoice);
            Assert.NotNull(resolution);
        }

        [Fact]
        public void FIRST_NEXT_suspends_for_NEXT_after_FIRST_Continues()
        {
            var game = NewCrimeGame(cash: 0);
            game.CurrentPlayer.TalkBonus = 10;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_central-data-access"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Single option — auto-picked. FIRST Negotiate 9: die 1 + Talk 10 = 11 Proceed → NEXT.
            // Cash 0 → bribes auto-decline (no Bribe PendingChoice).
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0, SkillCheck = new SkillCheckChoice { BribeDollars = 0 } },
                out _, out var error,
                ScriptedRng.FromDieFaces(1)));
            Assert.Contains("next", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PendingChoiceKinds.MisbehaveOption, game.PendingChoice!.Kind);
            Assert.Equal(MisbehaveSteps.StepOptionId(1), Assert.Single(game.PendingChoice.Options!));
            Assert.True(game.PendingMisbehave!.AwaitingNextStep);
            Assert.Equal(1, game.PendingMisbehave.CurrentStepIndex);
            Assert.NotNull(game.PendingMisbehave.FaceUp);
        }

        [Fact]
        public void FIRST_NEXT_resume_NEXT_can_Proceed()
        {
            var game = NewCrimeGame(cash: 0);
            game.CurrentPlayer.TalkBonus = 10;
            game.CurrentPlayer.TechBonus = 10;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_central-data-access"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0, SkillCheck = new SkillCheckChoice { BribeDollars = 0 } },
                out _, out _,
                ScriptedRng.FromDieFaces(1)));

            // NEXT Tech 7: die 1 + Tech 10 = 11 Proceed — card complete.
            Assert.True(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.False(game.PendingMisbehave?.AwaitingNextStep ?? false);
        }

        [Fact]
        public void FIRST_botch_does_not_offer_NEXT()
        {
            var game = NewCrimeGame(cash: 0);
            game.CurrentPlayer.TalkBonus = 0;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_central-data-access"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Negotiate 9: die 1 + Talk 0 = 1 Attempt Botched.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0, SkillCheck = new SkillCheckChoice { BribeDollars = 0 } },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Null(game.PendingChoice);
            Assert.Null(game.PendingMisbehave);
        }

        [Fact]
        public void Structured_steps_preferred_over_prose_FIRST_NEXT()
        {
            var option = new MisbehaveOption(
                "Two Steps",
                "FIRST, Ignore Me: Fight 99; 1-98 Attempt Botched. 99+ Proceed. NEXT, Also Ignore: Tech 99; 1+ Proceed.",
                steps: new[]
                {
                    new MisbehaveStep(
                        "First Structured",
                        "Fight 5; 1-4 Attempt Botched. 5+ Continue.",
                        skillCheck: new MisbehaveSkillCheckSpec(Skill.Fight, 5),
                        bands: new[]
                        {
                            new MisbehaveBand(1, 4, "Attempt Botched", new[]
                            {
                                new MisbehaveEffect(MisbehaveEffectType.Botched)
                            }),
                            new MisbehaveBand(5, null, "Continue", new[]
                            {
                                new MisbehaveEffect(MisbehaveEffectType.Proceed)
                            })
                        }),
                    new MisbehaveStep(
                        "Second Structured",
                        "Tech 5; 1-4 Attempt Botched. 5+ Proceed.",
                        skillCheck: new MisbehaveSkillCheckSpec(Skill.Tech, 5),
                        bands: new[]
                        {
                            new MisbehaveBand(1, 4, "Attempt Botched", new[]
                            {
                                new MisbehaveEffect(MisbehaveEffectType.Botched)
                            }),
                            new MisbehaveBand(5, null, "Proceed", new[]
                            {
                                new MisbehaveEffect(MisbehaveEffectType.Proceed)
                            })
                        })
                });

            var steps = MisbehaveSteps.ForOption(option);
            Assert.Equal(2, steps.Count);
            Assert.Equal("First Structured", steps[0].Name);
            Assert.True(steps[0].HasStructuredBands);
            Assert.Equal(Skill.Fight, steps[0].SkillCheck!.Skill);
        }

        [Fact]
        public void FIRST_NEXT_then_Bribe_on_NEXT_reenters_PendingChoice()
        {
            // unification-day: FIRST Fight Kosherized → NEXT Negotiate Bribes.
            var game = NewCrimeGame(cash: 500);
            // Kosherized Fight uses roster Fight only (GF9 p.6) — hire Jayne.
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out var hireError), hireError);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_unification-day-festivities"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // FIRST Fight 7 Kosherized: Jayne Fight dice; faces sum ≥ 7 → Continue to NEXT.
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var firstError,
                ScriptedRng.FromDieFaces(6, 6, 6)));
            Assert.Contains("next", firstError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PendingChoiceKinds.MisbehaveOption, game.PendingChoice!.Kind);

            // Resume NEXT — Negotiate 8 Bribes with cash → Bribe PendingChoice (re-enter).
            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error,
                ScriptedRng.FromDieFaces(5)));
            Assert.Contains("Bribes", error);
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);
        }
    }
}
