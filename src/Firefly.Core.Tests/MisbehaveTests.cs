using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class MisbehaveTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
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
        public void Catalog_loads_core_misbehave_cards()
        {
            var catalog = MisbehaveCatalog.LoadDefault();
            Assert.True(catalog.Cards.Count >= 70);
            Assert.True(catalog.TryGet("misbehave_a-formal-affair", out var affair));
            Assert.Equal(2, affair.Options.Count);
            Assert.Equal("Companion", affair.Ace);
        }

        [Fact]
        public void Negotiate_is_a_talk_test()
        {
            Assert.True(SkillCheck.TryParse("Negotiate 6; 1-5 Attempt Botched. 6+ Proceed.", out var check));
            Assert.Equal(Skill.Talk, check.Skill);
            Assert.Equal(6, check.Target);
        }

        [Fact]
        public void Fancy_duds_option_is_rejected_without_the_gear()
        {
            var game = NewCrimeGame();
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out var error));
            Assert.Contains("FANCY DUDS", error, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(game.PendingMisbehave);
        }

        [Fact]
        public void Failed_negotiate_botches_the_start_and_leaves_the_job_in_hand()
        {
            var game = NewCrimeGame();
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Contains(Crime, game.CurrentPlayer.JobHand);
            Assert.Null(game.CurrentPlayer.FindActive(Crime));
            Assert.Null(game.PendingMisbehave);
            Assert.Equal(TurnAction.Work, game.LastAction);
        }

        [Fact]
        public void Successful_negotiate_counts_as_a_proceed()
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
                ScriptedRng.FromDieFaces(3, 3)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.True(resolution.SkillCheck!.Success);
            Assert.NotNull(game.PendingMisbehave);
            Assert.Equal(2, game.PendingMisbehave!.Remaining);
            Assert.Contains(Crime, game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Companion_ace_proceeds_without_a_test()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.FindByName("Inara")!, out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, "p1", new MisbehaveChoice { UseAce = true }, out var resolution, out var error), error);
            Assert.True(resolution!.UsedAce);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
            Assert.Equal(2, game.PendingMisbehave!.Remaining);
        }

        [Fact]
        public void Fancy_duds_gear_unlocks_the_require_option()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Gear.Add("gear_a-very-fine-hat");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal("Look'n the Part", resolution.Option!.Name);
        }

        [Fact]
        public void Replace_card_does_not_spend_a_misbehave_step()
        {
            var game = NewCrimeGame();
            StartCrime(game);
            var detour = game.Misbehave!.Catalog.Get("misbehave_port-control-land-lock");
            game.Misbehave.PlaceOnTop(detour);
            var remaining = game.PendingMisbehave!.Remaining;
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 1 }, out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Replaced, resolution!.Outcome);
            Assert.Equal(remaining, game.PendingMisbehave!.Remaining);
            Assert.Null(game.PendingMisbehave.FaceUp);
        }

        [Fact]
        public void Inverted_fight_line_is_a_fight_test()
        {
            Assert.True(SkillCheck.TryParse("8+ Fight; 1-7 Kill a Crew, Warrant Issued. 8+ Attempt Botched.", out var check));
            Assert.Equal(Skill.Fight, check.Skill);
            Assert.Equal(8, check.Target);
        }

        [Fact]
        public void Failed_ambush_kills_a_crew_and_issues_a_warrant()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.FindByName("Kaylee")!, out _));
            game.CurrentPlayer.FightBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Equal(1, resolution.CrewKilled);
            Assert.Equal(1, game.CurrentPlayer.Warrants);
            Assert.Equal(0, game.CurrentPlayer.Roster.Count);
            Assert.NotNull(game.PendingMisbehave);
            Assert.Equal(2, game.PendingMisbehave!.Remaining);
        }

        [Fact]
        public void No_disgruntled_option_loads_cargo_and_proceeds()
        {
            var game = NewCrimeGame();
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-vote-of-no-confidence"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(1, resolution.GoodsLoaded);
            Assert.Equal(1, game.CurrentPlayer.Cargo);
        }

        [Fact]
        public void Solid_option_is_rejected_without_a_contact()
        {
            var game = NewCrimeGame();
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_double-dealing"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out var error));
            Assert.Contains("Solid", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Declining_an_or_botch_pay_option_botches()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Cash = 5000;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_invitation-only-gala"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1, AcceptPay = false },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Equal(5000, game.CurrentPlayer.Cash);
            Assert.Null(game.PendingMisbehave);
        }

        [Fact]
        public void Transport_keyword_on_gear_unlocks_the_require_option()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Gear.Add("gear_4wd-mule");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_everything-thats-not-nailed-down"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(3, game.CurrentPlayer.Contraband);
        }
    }
}
