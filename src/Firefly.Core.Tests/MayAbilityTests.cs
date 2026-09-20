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
    /// Open <c>may</c> crew abilities: skillReroll, bribesOnAnyNegotiate, freeShoreLeaveAtSupply.
    /// FAQ 4.1 p.8: mandatory unless they say “may”; may always suspends PendingChoice.
    /// </summary>
    public class MayAbilityTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Persephone = "alliance-lux-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

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
            Assert.True(new WorkAction().TryWork(game, "p1", Crime, out _, out _));
        }

        [Fact]
        public void Crew_json_loads_may_ability_batch()
        {
            var kaylee = Crew.Get("crew_kaylee");
            Assert.Contains(kaylee.Abilities, a =>
                a.MatchesType(AbilityTypes.SkillReroll)
                && a.Skill == "Tech"
                && !a.Mandatory);

            var zoe = Crew.Get("crew_zoe");
            Assert.Contains(zoe.Abilities, a =>
                a.MatchesType(AbilityTypes.SkillReroll)
                && a.Skill == "Fight"
                && !a.Mandatory);

            var inara = Crew.Get("crew_inara");
            Assert.Contains(inara.Abilities, a =>
                a.MatchesType(AbilityTypes.SkillReroll)
                && a.Skill == "Negotiate"
                && !a.Mandatory);

            var cortland = Crew.Get("crew_cortland_bluesun");
            Assert.Contains(cortland.Abilities, a =>
                a.MatchesType(AbilityTypes.BribesOnAnyNegotiate) && !a.Mandatory);

            var barkeep = Crew.Get("crew_barkeep_kalidasa");
            Assert.Contains(barkeep.Abilities, a =>
                a.MatchesType(AbilityTypes.FreeShoreLeaveAtSupply) && a.Mandatory);
        }

        [Fact]
        public void Kaylee_Tech_reroll_always_suspends_even_on_success()
        {
            // Supplies.tsv: "Natural Know How: May re-roll [Tech] Tests."
            // FAQ 4.1 p.8: may → always PendingChoice (do not auto-skip).
            var game = NewCrimeGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_kill-the-alarm"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Tech 5 with Tech 3 crew: die 6 succeeds — still must ask.
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("re-roll", error, System.StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PendingChoiceKinds.SkillReroll, game.PendingChoice!.Kind);
            Assert.Contains(SkillRerollOptions.Keep, game.PendingChoice.Options!);
            Assert.Contains(SkillRerollOptions.Reroll, game.PendingChoice.Options!);
        }

        [Fact]
        public void Kaylee_Tech_reroll_accept_uses_second_roll()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            // Kaylee Tech 3 → three dice.
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_kill-the-alarm"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // First roll 1+1+1 = 3 (fail Tech 5).
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(1, 1, 1)));

            // Re-roll 2+2+1 = 5 Proceed.
            Assert.True(resolver.TryResumeSkillReroll(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Reroll },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(2, 2, 1)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(5, resolution.SkillCheck!.Roll.Sum);
        }

        [Fact]
        public void Kaylee_Tech_reroll_decline_keeps_first_roll()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_kill-the-alarm"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(1, 1, 1)));

            Assert.True(resolver.TryResumeSkillReroll(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Keep },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6, 6, 6)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Equal(3, resolution.SkillCheck!.Roll.Sum);
        }

        [Fact]
        public void Zoe_Fight_reroll_does_not_trigger_on_Tech()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_zoe"), out _));
            game.CurrentPlayer.TechBonus = 5; // Tech dice without Kaylee skillReroll
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_kill-the-alarm"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1, 1, 1, 1)), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
        }

        [Fact]
        public void Cortland_enables_Bribes_on_plain_Negotiate_and_always_suspends()
        {
            // Supplies.tsv: "May pay Bribes before any Negotiate Test. Bribes may not be used on SHOWDOWNS."
            var game = NewCrimeGame(cash: 50); // unaffordable — still suspend for may
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_cortland_bluesun"), out _));
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Option 1: Negotiate 6 (no printed Bribes).
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out _, out var error,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("Bribes", error);
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);
            Assert.Equal(50, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Cortland_Bribes_resume_pay_applies_on_plain_Negotiate()
        {
            var game = NewCrimeGame(cash: 300);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_cortland_bluesun"), out _));
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out _, out _,
                ScriptedRng.FromDieFaces(4)));

            // Negotiate 6: die 4 + bribe +2 = 6 Proceed.
            Assert.True(resolver.TryResumeBribeAmount(
                game, "p1",
                new ChoiceSubmission { Amount = 200 },
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(4)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(200, resolution.SkillCheck!.BribeDollarsPaid);
            Assert.Equal(100, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Without_Cortland_plain_Negotiate_does_not_offer_Bribes()
        {
            var game = NewCrimeGame(cash: 500);
            game.CurrentPlayer.TalkBonus = 1;
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
            Assert.Equal(0, resolution!.SkillCheck!.BribeDollarsPaid);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
        }

        [Fact]
        public void Inara_Negotiate_reroll_suspends_after_Cortland_Bribes_resolved()
        {
            // Both may abilities: Bribes first, then re-roll after the roll.
            var game = NewCrimeGame(cash: 200);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_cortland_bluesun"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_inara"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out _, out _,
                ScriptedRng.FromDieFaces(1, 1, 1)));
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);

            Assert.False(resolver.TryResumeBribeAmount(
                game, "p1",
                new ChoiceSubmission { Amount = 0 },
                new MisbehaveChoice { OptionIndex = 1 },
                out _, out _,
                ScriptedRng.FromDieFaces(1, 1, 1)));
            Assert.Equal(PendingChoiceKinds.SkillReroll, game.PendingChoice!.Kind);

            Assert.True(resolver.TryResumeSkillReroll(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Reroll },
                new MisbehaveChoice
                {
                    OptionIndex = 1,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 0 }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(2, 2, 2)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(6, resolution.SkillCheck!.Roll.Sum);
        }

        [Fact]
        public void Barkeep_Shore_Leave_at_Supply_Planet_is_free()
        {
            // Supplies.tsv: "Good Times: Giving your Crew Shore Leave at Supply Planets is free."
            // No “may” — mandatory cost rule, not a PendingChoice.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 100, fuel: 3);
            var game = new GameState(map, new[] { player }) { Crew = Crew };
            Assert.True(map.TryGet(Persephone, out var sector));
            Assert.True(sector.HasSupplyDeck);
            Assert.True(player.Roster.TryHire(Crew.Get("crew_barkeep_kalidasa"), out _));
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            player.Roster.Disgruntle(player.Roster.Leader!);

            Assert.True(new ShoreLeaveAction().TryShoreLeave(game, "p1", out var result, out var error), error);
            Assert.Equal(0, result!.CashSpent);
            Assert.Equal(100, player.Cash);
            Assert.Equal(1, result.TokensCleared);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Barkeep_Shore_Leave_still_costs_on_non_Supply_planet()
        {
            // Santo is a planet without a Supply Deck.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 500, fuel: 3);
            var game = new GameState(map, new[] { player }) { Crew = Crew };
            Assert.True(map.TryGet(Santo, out var sector));
            Assert.False(sector.HasSupplyDeck);
            Assert.True(player.Roster.TryHire(Crew.Get("crew_barkeep_kalidasa"), out _));
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));

            Assert.True(new ShoreLeaveAction().TryShoreLeave(game, "p1", out var result, out var error), error);
            Assert.Equal(200, result!.CashSpent);
            Assert.Equal(300, player.Cash);
        }

        [Fact]
        public void Nav_Kaylee_Tech_reroll_suspends_and_keep_works()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 3);
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var game = new GameState(map, new[] { player }, decks: decks) { Crew = Crew };
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            var resolver = new NavResolver();
            game.Decks!.Alliance.PlaceOnTop(
                game.Decks.Catalog.Get("nav_minor-technical-difficulty"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, 0, out _, out var suspendError, ScriptedRng.FromDieFaces(1, 1, 1)));
            Assert.Equal(PendingChoiceKinds.SkillReroll, game.PendingChoice!.Kind);
            Assert.Contains("re-roll", suspendError, System.StringComparison.OrdinalIgnoreCase);

            Assert.True(resolver.TryResumeSkillReroll(
                game,
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Keep },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6, 6, 6)), error);
            Assert.Null(game.PendingChoice);
            Assert.NotNull(resolution);
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
        }
    }
}
