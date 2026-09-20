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
    /// PR 2: live Misbehave.json cards migrated to structured skillCheck / bands / effects.
    /// Prose <c>details</c> remain display/source of truth; structured is overlay.
    /// GF9 p.6 / Director's Cut p.14: Bribes ($100 = +1) and Kosherized (crew Fight only).
    /// </summary>
    public class MisbehaveMigrationTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        [Theory]
        [InlineData("misbehave_keep-a-low-profile")]
        [InlineData("misbehave_keep-a-low-profile_2")]
        [InlineData("misbehave_backwater-deputies")]
        [InlineData("misbehave_denied-docking-rights")]
        [InlineData("misbehave_the-local-law")]
        [InlineData("misbehave_the-local-law_2")]
        [InlineData("misbehave_all-out-brawl")]
        [InlineData("misbehave_all-out-brawl_2")]
        [InlineData("misbehave_packed-market")]
        [InlineData("misbehave_it-was-the-best-day-ever")]
        public void Migrated_cards_load_structured_overlay_without_stripping_prose(string id)
        {
            var catalog = MisbehaveCatalog.LoadDefault();
            Assert.True(catalog.TryGet(id, out var card));
            Assert.NotEmpty(card.Options);
            foreach (var option in card.Options)
            {
                Assert.False(string.IsNullOrWhiteSpace(option.Details));
                Assert.True(
                    option.HasStructuredBands
                    || option.HasStructuredEffects
                    || !string.IsNullOrWhiteSpace(option.ProceedIfTag),
                    $"{id}/{option.Name} missing structured overlay");
            }
        }

        [Fact]
        public void Keep_a_low_profile_kosherized_fail_kills_via_structured_bands()
        {
            // Printed: Fight 6 Kosherized Rules; 1-5 Kill a Crew, Attempt Botched. 6+ Proceed.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.FightBonus = 4; // gear proxy — Kosherized must ignore
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_keep-a-low-profile"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1)), error);
            Assert.True(resolution!.Option!.HasStructuredBands);
            Assert.True(resolution.SkillCheck!.Check.Kosherized);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
            Assert.Equal(1, resolution.CrewKilled);
        }

        [Fact]
        public void Keep_a_low_profile_bribes_negotiate_warrant_band()
        {
            // Printed: Negotiate 9 Bribes; 1-8 Warrant Issued. 9+ Proceed.
            // GF9 p.6: "For every $100 you pay the bank, add +1 to your total (Roll+Skill+Bribes)."
            var game = NewCrimeGame(cash: 300);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_keep-a-low-profile"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // die 5 + bribe +3 = 8 → Warrant Issued (structured), Job abandoned.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 1,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 300 }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.True(resolution!.Option!.SkillCheck!.BribesAllowed);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
            Assert.Equal(0, game.CurrentPlayer.Cash);
            Assert.DoesNotContain(Crime, game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Backwater_deputies_bribes_proceed_via_structured_bands()
        {
            // Printed: Negotiate 9 Bribes; 1-8 Attempt Botched. 9+ Proceed.
            var game = NewCrimeGame(cash: 400);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_backwater-deputies"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

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
            Assert.True(resolution.Option!.HasStructuredBands);
            Assert.Equal(400, resolution.SkillCheck!.BribeDollarsPaid);
            Assert.NotNull(game.PendingMisbehave);
        }

        [Fact]
        public void Denied_docking_rights_tech_warrant_via_structured_bands()
        {
            // Printed: Tech 6; 1-5 Warrant Issued. 6+ Proceed.
            var game = NewCrimeGame();
            game.CurrentPlayer.TechBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_denied-docking-rights"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.True(resolution!.Option!.HasStructuredBands);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
        }

        [Fact]
        public void The_local_law_bribes_botch_band_without_bribe_pay()
        {
            // Printed: Negotiate 11 Bribes; 1-10 Attempt Botched. 11+ Proceed.
            var game = NewCrimeGame(cash: 1000);
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_the-local-law"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 0 }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(2)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(resolution.Option!.SkillCheck!.BribesAllowed);
            Assert.Equal(0, resolution.SkillCheck.BribeDollarsPaid);
            Assert.Equal(1000, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void All_out_brawl_kosherized_clean_proceed()
        {
            // Printed: Fight 9 Kosherized; 1-8 Attempt Botched. 9+ Proceed
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            // Jayne 2 + Zoe 2 = 4 crew Fight; Kosherized ignores FightBonus.
            game.CurrentPlayer.FightBonus = 10;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_all-out-brawl"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { AcceptReroll = false }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(3, 3, 2, 1)), error);
            Assert.True(resolution!.SkillCheck!.Check.Kosherized);
            Assert.Equal(9, resolution.SkillCheck.Total);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
            Assert.True(resolution.Option!.HasStructuredBands);
        }

        [Fact]
        public void All_out_brawl_dirty_success_disgruntles_moral_via_structured()
        {
            // Printed: Fight 9; … 9+ Disgruntle Moral Crew. Proceed
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_kaylee"), out _));
            Assert.True(game.CurrentPlayer.Roster.Find("crew_kaylee")!.Moral);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_all-out-brawl"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Non-Kosherized Fight 4 (Jayne+Zoe); 3+3+2+1 = 9 Proceed + Disgruntle Moral.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 1,
                    SkillCheck = new SkillCheckChoice { AcceptReroll = false }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(3, 3, 2, 1)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_kaylee")!.Disgruntled);
        }

        [Fact]
        public void Packed_market_kosherized_wanted_proceed_band()
        {
            // Printed: Fight 8 Kosherized; 1-7 Attempt Botched. 8+ Crew is Now Wanted. Proceed
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            Assert.False(game.CurrentPlayer.Roster.Find("crew_kaylee")!.Wanted);
            game.CurrentPlayer.FightBonus = 0;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_packed-market"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // crew Fight 4; dice 2+2+2+2 = 8 → Wanted (first crew) + Proceed
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { AcceptReroll = false }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(2, 2, 2, 2)), error);
            Assert.True(resolution!.SkillCheck!.Check.Kosherized);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_kaylee")!.Wanted);
        }

        [Fact]
        public void Packed_market_firearm_option_uses_structured_effects()
        {
            // Printed: Requires FIREARM. … Wanted. Disgruntle all Moral Crew. Proceed.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            game.CurrentPlayer.Gear.Add("gear_pistol");
            Assert.True(GearCarriage.TryAssign(
                game, game.CurrentPlayer, "gear_pistol", "crew_kaylee", out var gearError), gearError);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_packed-market"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error), error);
            Assert.True(resolution!.Option!.HasStructuredEffects);
            Assert.Null(resolution.SkillCheck);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_kaylee")!.Disgruntled);
        }

        [Fact]
        public void Best_day_ever_medic_proceedIfTag_skips_tech_roll()
        {
            // Printed: If you have Medic, Proceed. Otherwise, Tech 6; …
            // Simon Tam: Medic profession (Supplies.tsv / Crew.json).
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_simon-tam"), out _));
            game.CurrentPlayer.TechBonus = 0;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_it-was-the-best-day-ever"));
            var option = game.Misbehave.Catalog.Get("misbehave_it-was-the-best-day-ever").Options[1];
            Assert.Equal("Medic", option.ProceedIfTag);
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error); // unused if shortcut fires
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Null(resolution.SkillCheck);
            Assert.NotNull(game.PendingMisbehave);
        }

        [Fact]
        public void Best_day_ever_without_medic_rolls_structured_tech()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.TechBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_it-was-the-best-day-ever"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.NotNull(resolution!.SkillCheck);
            Assert.Equal(Skill.Tech, resolution.SkillCheck!.Check.Skill);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
            Assert.True(resolution.Option!.HasStructuredBands);
        }

        [Fact]
        public void Best_day_ever_fancy_duds_proceedIfTag_skips_negotiate()
        {
            // Printed: If you have FANCY DUDS, Proceed. Otherwise, Negotiate 6; …
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_simon-tam"), out _));
            // Simon carries Fancy Duds keyword on the crew card itself.
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_it-was-the-best-day-ever"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Null(resolution.SkillCheck);
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
