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
    /// Director's Cut C&amp;P p.49 Splitting Up / I'll Be in my Bunk / No one left —
    /// Fork in the Road and They're right on our Tails.
    /// </summary>
    public class MisbehaveSplitCrewTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        [Theory]
        [InlineData("misbehave_fork-in-the-road")]
        [InlineData("misbehave_fork-in-the-road_2")]
        [InlineData("misbehave_theyre-right-on-our-tails")]
        public void Split_crew_cards_load_structured_overlay(string id)
        {
            var catalog = MisbehaveCatalog.LoadDefault();
            Assert.True(catalog.TryGet(id, out var card));
            Assert.True(card.Options[0].HasStructuredEffects);
            Assert.False(string.IsNullOrWhiteSpace(card.Options[0].Details));
        }

        [Fact]
        public void Split_Crew_one_crew_left_botches_without_team_choice()
        {
            // Director's Cut C&P p.49 Splitting Up:
            // "If you only have one Crew member left Working the Job when called upon to
            // Split your Crew, you've Botched the Job."
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Fork_suspends_for_team_assignment_PendingChoice()
        {
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error));
            Assert.Equal(PendingChoiceKinds.MisbehaveSplitCrew, game.PendingChoice!.Kind);
            Assert.Contains("Team A", error);
        }

        [Fact]
        public void Fork_both_teams_Proceed_Proceeds_past_Fork()
        {
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);

            // Deck top after Fork draw: team0 card, then team1 card.
            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed_b", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_proceed_a", "proceed"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal("misbehave_fork-in-the-road", resolution.Card.Id);
            Assert.Equal(2, JobWorkCrew.AvailableCount(game.CurrentPlayer));
            Assert.Null(game.PendingMisbehave?.SplitCrew);
        }

        [Fact]
        public void Fork_botching_team_returns_to_Ship_other_Proceed_past()
        {
            // Printed: "Any team with Botches their card must return to the Ship."
            // Other team Proceeds → still Crew Working → Proceed past Fork.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_botch", "botched"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_jayne"));
            Assert.False(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("leader_malcolm"));
            Assert.Equal(1, JobWorkCrew.AvailableCount(game.CurrentPlayer));
        }

        [Fact]
        public void Fork_both_teams_Botch_No_one_left_Botches_Job()
        {
            // Printed + DC p.49 No one left: all Crew Return to the Ship → Botched.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_botch_b", "botched"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_botch_a", "botched"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Equal(0, JobWorkCrew.AvailableCount(game.CurrentPlayer));
        }

        [Fact]
        public void Fork_team_Warrant_abandons_Job_does_not_Proceed_past()
        {
            // Printed: "If neither team has a Warrant Issued, Proceed Past this card."
            // FAQ/GF9: Warrant while Working discards the Job.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_warrant", "warrantIssued"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.Warrants >= 1);
            Assert.Null(game.CurrentPlayer.FindActive(Crime));
        }

        [Fact]
        public void Fork_does_not_take_ReplaceCard_Draw_2_path()
        {
            // Landmine: Fork prose contains "Draw 2 Misbehave" — must not Remaining+1 Replace.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);
            var remainingBefore = game.PendingMisbehave!.Remaining;

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed_b", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_proceed_a", "proceed"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));
            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.NotEqual(MisbehaveOutcome.Replaced, resolution.Outcome);
            // Nested draws must not bump Remaining the way Hotel "Draw 2" Replace does.
            if (game.PendingMisbehave != null)
                Assert.True(game.PendingMisbehave.Remaining <= remainingBefore);
        }

        [Fact]
        public void Fork_nested_skill_uses_only_team_dice()
        {
            // Team A = Jayne (Fight 2); Team B = Mal. Nested Fight 7 with only team dice.
            // Jayne alone: 2 dice of 1+1 = 2 → Botch → Jayne Returns; Mal Proceeds past.
            // FightBonus would otherwise carry a full-roster success — must be ignored for teams.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            game.CurrentPlayer.FightBonus = 10;
            StartCrime(game);

            var fightFail = new MisbehaveCard(
                "nested_fight_team",
                "Team Fight",
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Scrap",
                        "Fight 7; 1-6 Attempt Botched. 7+ Proceed.",
                        skillCheck: new MisbehaveSkillCheckSpec(Skill.Fight, 7),
                        bands: new[]
                        {
                            new MisbehaveBand(1, 6, "1-6 Attempt Botched",
                                new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched) }),
                            new MisbehaveBand(7, null, "7+ Proceed",
                                new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed) })
                        })
                });

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed", "proceed"));
            game.Misbehave.PlaceOnTop(fightFail);
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1)), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_jayne"));
            Assert.False(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("leader_malcolm"));
        }

        [Fact]
        public void Tails_Fight_fail_kills_non_Leader_team_then_Proceeds()
        {
            // Printed: 1-11 Kill all Crew in Team; Next Proceed with Leader team.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);
            var tails = game.Misbehave!.Catalog.Get("misbehave_theyre-right-on-our-tails");
            Assert.True(
                HasSplitTails(tails),
                "expected splitCrewTails overlay on Tails");
            game.Misbehave.PlaceOnTop(tails);
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SplitTeam0CrewIds = new List<string> { "crew_jayne" }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1)), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Null(game.CurrentPlayer.Roster.Find("crew_jayne"));
            Assert.NotNull(game.CurrentPlayer.Roster.Find("leader_malcolm"));
            Assert.Equal(1, JobWorkCrew.AvailableCount(game.CurrentPlayer));
        }

        [Fact]
        public void Tails_Fight_success_returns_non_Leader_team_then_Proceeds()
        {
            // Printed: 12+ Return Team to Ship; Proceed with Leader team.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_zoe"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(
                game.Misbehave.Catalog.Get("misbehave_theyre-right-on-our-tails"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SplitTeam0CrewIds = new List<string> { "crew_jayne", "crew_zoe" }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6, 6, 6, 6)), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.NotNull(game.CurrentPlayer.Roster.Find("crew_jayne"));
            Assert.True(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_jayne"));
            Assert.True(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_zoe"));
            Assert.False(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("leader_malcolm"));
        }

        [Fact]
        public void Tails_suspends_then_rejects_Leader_on_non_Leader_team()
        {
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(
                game.Misbehave.Catalog.Get("misbehave_theyre-right-on-our-tails"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));
            Assert.Equal(PendingChoiceKinds.MisbehaveSplitCrew, game.PendingChoice!.Kind);

            Assert.False(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "leader_malcolm" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error));
            Assert.Contains("non-Leader", error);
        }

        private static bool HasSplitTails(MisbehaveCard card)
        {
            foreach (var effect in card.Options[0].Effects)
            {
                if (effect.Is(MisbehaveLocalEffectType.SplitCrewTails))
                    return true;
            }
            return false;
        }

        [Fact]
        public void Lock_nested_Kill_N_rejects_victim_outside_active_team()
        {
            // Christopher lock PR #55: nested Kill N victims must be on the active team.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_zoe"), out _));
            StartCrime(game);

            var killOne = new MisbehaveCard(
                "nested_kill_one",
                "Kill One",
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Spill",
                        "Kill a Crew. Attempt Botched.",
                        effects: new[]
                        {
                            MisbehaveEffect.Of(CardEffectType.KillCrew, 1),
                            MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched)
                        })
                });

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed", "proceed"));
            game.Misbehave.PlaceOnTop(killOne);
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            // Team A = Jayne + Zoe; Mal on Team B. Kill N suspends for victim pick.
            Assert.False(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne", "crew_zoe" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _));
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);

            // Off-team Leader is illegal.
            Assert.False(resolver.TryResumeKillVictims(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "leader_malcolm" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error));
            Assert.Contains("active Split Crew team", error);

            // On-team Jayne is legal → Botch → Jayne killed, Zoe still Working with Mal → Proceed past.
            Assert.True(resolver.TryResumeKillVictims(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var okError), okError);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Null(game.CurrentPlayer.Roster.Find("crew_jayne"));
            Assert.NotNull(game.CurrentPlayer.Roster.Find("crew_zoe"));
            Assert.NotNull(game.CurrentPlayer.Roster.Find("leader_malcolm"));
        }

        [Fact]
        public void Lock_nested_Warrant_without_Botch_does_not_Return_team()
        {
            // Christopher lock PR #55: Return team only on Botch; Warrant alone does not Return.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_warrant", "warrantIssued"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            // Warrant abandons Job at aggregation (lock 1); Jayne was not Returned before abandon.
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.Warrants >= 1);
            Assert.Null(game.CurrentPlayer.FindActive(Crime));
            Assert.NotNull(game.CurrentPlayer.Roster.Find("crew_jayne"));
        }

        [Fact]
        public void Lock_nested_Ace_Proceeds_that_team_card()
        {
            // Christopher lock PR #55: Ace auto-Proceeds the nested team card.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            GiveCarriedGear(game, "gear_pistol", "crew_jayne");
            StartCrime(game);

            // Two options so nested suspends for MisbehaveOption before Botch applies.
            var aceCard = new MisbehaveCard(
                "nested_ace",
                "Ace Nested",
                "Hearts",
                "FIREARM",
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Would Botch A",
                        "Attempt Botched.",
                        effects: new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched) }),
                    new MisbehaveOption(
                        "Would Botch B",
                        "Attempt Botched.",
                        effects: new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched) })
                });

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed", "proceed"));
            game.Misbehave.PlaceOnTop(aceCard);
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.False(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _));
            Assert.Equal(PendingChoiceKinds.MisbehaveOption, game.PendingChoice!.Kind);
            Assert.Equal("nested_ace", game.PendingMisbehave!.FaceUp!.Id);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { UseAce = true },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.False(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_jayne"));
        }

        [Fact]
        public void Lock_nested_Replace_redraws_team_card_only()
        {
            // Christopher lock PR #55: nested Replace redraws that team's card; Remaining unchanged.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            StartCrime(game);
            var remainingBefore = game.PendingMisbehave!.Remaining;

            var replaceCard = new MisbehaveCard(
                "nested_replace",
                "Replace Nested",
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Draw Another",
                        "Draw Another Misbehave Card to replace this card.",
                        effects: new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.ReplaceCard) })
                });

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed_b", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_proceed_after_replace", "proceed"));
            game.Misbehave.PlaceOnTop(replaceCard);
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal("misbehave_fork-in-the-road", resolution.Card.Id);
            // Nested replace must not bump Remaining the way Hotel "Draw 2" does.
            if (game.PendingMisbehave != null)
                Assert.True(game.PendingMisbehave.Remaining < remainingBefore
                    || game.PendingMisbehave.Remaining == remainingBefore - 1);
        }

        [Fact]
        public void Lock_Dalin_not_reoffered_mid_nested()
        {
            // Christopher lock PR #55: Dalin once per Work — not re-offered on nested team cards.
            var game = NewCrimeGame(cash: 500);
            HireMalAndJayne(game);
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                game.Crew!.Get("crew_dalin_piratesbountyhunters"), out _));
            StartCrime(game);

            game.Misbehave!.PlaceOnTop(MakeEffectsOnly("nested_proceed_b", "proceed"));
            game.Misbehave.PlaceOnTop(MakeEffectsOnly("nested_proceed_a", "proceed"));
            game.Misbehave.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_fork-in-the-road"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out _));
            Assert.Equal(PendingChoiceKinds.MisbehaveDiscardRedraw, game.PendingChoice!.Kind);

            Assert.False(resolver.TryResumeDalinRedraw(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = DalinRedrawOptions.Decline },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _));
            Assert.Equal(PendingChoiceKinds.MisbehaveSplitCrew, game.PendingChoice!.Kind);

            Assert.True(resolver.TryResumeSplitCrew(
                game, "p1",
                new ChoiceSubmission { Values = new List<string> { "crew_jayne" } },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Lock_Tails_Fight_uses_team_gear_Fight_addend()
        {
            // Christopher lock PR #55: Tails Fight 12 = normal Fight + team carried gear.
            var game = NewCrimeGame();
            HireMalAndJayne(game);
            // Vera: +2 Fight carried → Jayne 2 crew + 2 gear = 4 dice.
            GiveCarriedGear(game, "gear_vera", "crew_jayne");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(
                game.Misbehave.Catalog.Get("misbehave_theyre-right-on-our-tails"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SplitTeam0CrewIds = new List<string> { "crew_jayne" }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6, 6, 6, 6)), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.NotNull(resolution.SkillCheck);
            Assert.True(resolution.SkillCheck!.Roll.Faces.Count >= 4);
            Assert.True(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_jayne"));
        }

        private static void GiveCarriedGear(GameState game, string gearId, string crewId)
        {
            game.CurrentPlayer.Gear.Add(gearId);
            Assert.True(
                GearCarriage.TryAssign(game, game.CurrentPlayer, gearId, crewId, out var error),
                error);
        }

        private static MisbehaveCard MakeEffectsOnly(string id, string effectType)
        {
            MisbehaveEffect effect;
            if (effectType == "warrantIssued")
                effect = MisbehaveEffect.Of(CardEffectType.WarrantIssued);
            else if (effectType == "botched")
                effect = MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched);
            else
                effect = MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed);

            return new MisbehaveCard(
                id,
                id,
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Only",
                        effectType == "warrantIssued" ? "Warrant Issued." :
                        effectType == "botched" ? "Attempt Botched." : "Proceed.",
                        effects: new[] { effect })
                });
        }

        private static void HireMalAndJayne(GameState game)
        {
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
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
