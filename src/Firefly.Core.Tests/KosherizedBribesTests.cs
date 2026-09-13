using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class KosherizedBribesTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        [Fact]
        public void TryParse_flags_kosherized_and_bribes()
        {
            // GF9 p.6: "Some Fight Tests will say “Kosherized” after the number."
            Assert.True(SkillCheck.TryParse(
                "Fight 6 Kosherized Rules; 1-5 Attempt Botched. 6+ Proceed.", out var kosher));
            Assert.Equal(Skill.Fight, kosher.Skill);
            Assert.Equal(6, kosher.Target);
            Assert.True(kosher.Kosherized);
            Assert.False(kosher.BribesAllowed);

            Assert.True(SkillCheck.TryParse(
                "Fight 8 Kosherized; 1-7 Attempt Botched. 8+ Proceed.", out var kosherShort));
            Assert.True(kosherShort.Kosherized);

            // GF9 p.6: "Some Negotiate Tests will say “Bribes” after their number."
            Assert.True(SkillCheck.TryParse(
                "Negotiate 9 Bribes; 1-8 Attempt Botched. 9+ Proceed.", out var bribes));
            Assert.Equal(Skill.Talk, bribes.Skill);
            Assert.Equal(9, bribes.Target);
            Assert.True(bribes.BribesAllowed);
            Assert.False(bribes.Kosherized);

            Assert.True(SkillCheck.TryParse(
                "Talk 10 Bribes  1-9 Full Stop. 10+ Keep Flying.", out var navBribes));
            Assert.True(navBribes.BribesAllowed);

            Assert.True(SkillCheck.TryParse(
                "Negotiate 6; 1-5 Attempt Botched. 6+ Proceed.", out var plain));
            Assert.False(plain.Kosherized);
            Assert.False(plain.BribesAllowed);
        }

        [Fact]
        public void Kosherized_excludes_fight_bonus_from_gear()
        {
            // GF9 / Director's Cut: "you may not add any Fight Skill from Gear to your total:
            // only the Fight Skill listed on your Crew Cards may be used."
            var player = new PlayerState("p1", "Mal", Santo) { FightBonus = 3 };
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_jayne"), out _));
            // Jayne Fight 2 + bonus 3 = 5 normally; Kosherized = crew only (2).
            var kosher = new SkillCheck(Skill.Fight, 6, kosherized: true);
            Assert.Equal(player.Roster.Fight, kosher.DiceCount(player));
            Assert.Equal(2, kosher.DiceCount(player));

            var normal = new SkillCheck(Skill.Fight, 6);
            Assert.Equal(5, normal.DiceCount(player));
        }

        [Fact]
        public void Bribes_add_plus_one_per_100_before_roll()
        {
            // GF9: "For every $100 you pay the bank, add +1 to your total (Roll+Skill+Bribes)."
            var player = new PlayerState("p1", "Mal", Santo, cash: 500) { TalkBonus = 1 };
            var check = new SkillCheck(Skill.Talk, 6, bribesAllowed: true);
            var result = check.Resolve(
                player,
                ScriptedRng.FromDieFaces(3),
                new SkillCheckChoice { BribeDollars = 200 });
            Assert.Equal(300, player.Cash);
            Assert.Equal(200, result.BribeDollarsPaid);
            Assert.Equal(2, result.BribeBonus);
            Assert.Equal(5, result.Total); // die 3 + bribe +2
            Assert.False(result.Success);
        }

        [Fact]
        public void Bribes_default_zero_does_not_auto_pay()
        {
            var player = new PlayerState("p1", "Mal", Santo, cash: 5000) { TalkBonus = 1 };
            var check = new SkillCheck(Skill.Talk, 9, bribesAllowed: true);
            var result = check.Resolve(player, ScriptedRng.FromDieFaces(1));
            Assert.Equal(5000, player.Cash);
            Assert.Equal(0, result.BribeDollarsPaid);
            Assert.False(result.Success);
        }

        [Fact]
        public void Bribes_rejected_when_not_printed_or_unaffordable()
        {
            var player = new PlayerState("p1", "Mal", Santo, cash: 50) { TalkBonus = 1 };
            var allowed = new SkillCheck(Skill.Talk, 6, bribesAllowed: true);
            Assert.False(allowed.TryResolve(
                player,
                ScriptedRng.FromDieFaces(6),
                out _,
                out var needCash,
                new SkillCheckChoice { BribeDollars = 100 }));
            Assert.Contains("$100", needCash);
            Assert.Equal(50, player.Cash);

            Assert.False(allowed.TryResolve(
                player,
                ScriptedRng.FromDieFaces(6),
                out _,
                out var badInc,
                new SkillCheckChoice { BribeDollars = 150 }));
            Assert.Contains("$100 increments", badInc);

            // FAQ 4.1 p.9 Stitch note: Bribes only on bribable Negotiate — ignored on plain Fight.
            var fight = new SkillCheck(Skill.Fight, 6);
            var fightResult = fight.Resolve(
                player,
                ScriptedRng.FromDieFaces(1),
                new SkillCheckChoice { BribeDollars = 100 });
            Assert.Equal(50, player.Cash);
            Assert.Equal(0, fightResult.BribeDollarsPaid);
        }

        [Fact]
        public void Misbehave_kosherized_fight_ignores_fight_bonus()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.FightBonus = 4; // would make Fight 6+ easy without Kosherized
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_keep-a-low-profile"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Option 0: Fight 6 Kosherized — Jayne Fight 2 only; roll 1+1 fails.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(resolution.SkillCheck!.Check.Kosherized);
            Assert.False(resolution.SkillCheck.Success);
        }

        [Fact]
        public void Misbehave_bribes_can_push_negotiate_over_target()
        {
            var game = NewCrimeGame(cash: 500);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_backwater-deputies"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Negotiate 9 Bribes: die 5 + bribe +4 = 9 Proceed; spent $400.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 400 }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.True(resolution.SkillCheck!.Success);
            Assert.Equal(400, resolution.SkillCheck.BribeDollarsPaid);
            Assert.Equal(100, game.CurrentPlayer.Cash);
            Assert.Equal(-400, resolution.CashDelta);
        }

        [Fact]
        public void Nav_bribes_affect_band_and_keep_flying()
        {
            var (game, resolver, player) = GameWithQueuedDraws();
            player.Cash = 300;
            player.TalkBonus = 1;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_local-tariff-patrol"));
            resolver.DrawNext(game);

            // Talk 9 Bribes: die 6 + bribe +3 = 9 Keep Flying.
            Assert.True(resolver.TryResolve(
                game, 0,
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6),
                new NavResolveChoice { SkillCheck = new SkillCheckChoice { BribeDollars = 300 } }),
                error);
            Assert.NotNull(resolution!.SkillCheck);
            Assert.True(resolution.SkillCheck!.Check.BribesAllowed);
            Assert.True(resolution.SkillCheck.Success);
            Assert.Equal(FlightOutcome.KeepFlying, resolution.Outcome);
            Assert.Equal(0, player.Cash);
            Assert.False(resolution.Stopped);
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

        private static (GameState Game, NavResolver Resolver, PlayerState Player) GameWithQueuedDraws()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Pelorum);
            var game = new GameState(map, new[] { player }, decks: decks);
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            return (game, new NavResolver(), player);
        }
    }
}
