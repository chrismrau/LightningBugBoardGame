using System;
using System.Collections.Generic;
using System.Linq;
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
        // Batch 1 — FIRST–NEXT / 2 Steps
        [InlineData("misbehave_central-data-access")]
        [InlineData("misbehave_unification-day-festivities")]
        [InlineData("misbehave_secure-perimeter")]
        [InlineData("misbehave_alliance-restricted-activity-zone")]
        [InlineData("misbehave_recalcitrant-official")]
        [InlineData("misbehave_theyve-got-us-dead-to-rights")]
        // Batch 1 — simple skill / require family
        [InlineData("misbehave_a-formal-affair")]
        [InlineData("misbehave_alliance-operatives")]
        [InlineData("misbehave_gun-play")]
        [InlineData("misbehave_gun-play_2")]
        [InlineData("misbehave_kill-the-alarm")]
        [InlineData("misbehave_kill-the-alarm_2")]
        [InlineData("misbehave_tight-security")]
        [InlineData("misbehave_tight-security_2")]
        [InlineData("misbehave_we-need-a-distraction")]
        [InlineData("misbehave_old-fashioned-shoot-out")]
        [InlineData("misbehave_hired-local-goons")]
        [InlineData("misbehave_disable-the-cortex-uplink")]
        [InlineData("misbehave_the-sheriffs-justice")]
        [InlineData("misbehave_purple-bellies")]
        // Batch 2 — Alliance Patrol / warrant-scaled + Alliance-tech / simple skill family
        [InlineData("misbehave_alliance-patrol")]
        [InlineData("misbehave_alliance-patrol_2")]
        [InlineData("misbehave_alliance-patrol_3")]
        [InlineData("misbehave_dockside-scanner")]
        [InlineData("misbehave_port-control-land-lock")]
        [InlineData("misbehave_state-of-the-art-security-system")]
        [InlineData("misbehave_improbably-complex-alliance-gizmo")]
        [InlineData("misbehave_it-takes-a-womans-touch")]
        [InlineData("misbehave_knife-fight")]
        [InlineData("misbehave_a-bit-of-local-color")]
        [InlineData("misbehave_a-bit-of-local-color_2")]
        [InlineData("misbehave_kidnapped-by-hill-folk")]
        [InlineData("misbehave_time-for-some-thrillin-heroics")]
        [InlineData("misbehave_lets-go-to-the-crappy-town-where-im-a-hero")]
        [InlineData("misbehave_double-dealing")]
        [InlineData("misbehave_magisterial-entanglements")]
        [InlineData("misbehave_dangerous-gossip")]
        [InlineData("misbehave_invitation-only-gala")]
        [InlineData("misbehave_ill-advised-rendezvous")]
        [InlineData("misbehave_locals-in-need")]
        // Batch 3 — Seize gear / FIREARM + Check Point Wanted roll
        [InlineData("misbehave_stop-and-frisk")]
        [InlineData("misbehave_security-scare")]
        [InlineData("misbehave_check-point")]
        [InlineData("misbehave_check-point_2")]
        [InlineData("misbehave_you-set-off-the-alarm")]
        [InlineData("misbehave_grumpy-malcontent")]
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
                    || option.HasStructuredSteps
                    || !string.IsNullOrWhiteSpace(option.ProceedIfTag),
                    $"{id}/{option.Name} missing structured overlay");
                if (option.HasStructuredSteps)
                {
                    foreach (var step in option.Steps)
                    {
                        Assert.True(
                            step.HasStructuredBands
                            || step.HasStructuredEffects
                            || step.SkillCheck != null,
                            $"{id}/{option.Name}/{step.Name} step missing structured overlay");
                    }
                }
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

        [Fact]
        public void Batch1_central_data_access_loads_structured_steps()
        {
            var catalog = MisbehaveCatalog.LoadDefault();
            var card = catalog.Get("misbehave_central-data-access");
            var option = Assert.Single(card.Options);
            Assert.True(option.HasStructuredSteps);
            Assert.Equal(2, option.Steps.Count);
            Assert.Equal(Skill.Talk, option.Steps[0].SkillCheck!.Skill);
            Assert.True(option.Steps[0].SkillCheck.BribesAllowed);
            Assert.Equal(Skill.Tech, option.Steps[1].SkillCheck!.Skill);
            Assert.Single(option.Steps[1].SkillCheck.Bonuses);
            Assert.Equal(2, option.Steps[1].SkillCheck.Bonuses[0].Amount);
            Assert.Equal("HACKING RIG", option.Steps[1].SkillCheck.Bonuses[0].Tag);
        }

        [Fact]
        public void Batch1_secure_perimeter_nextFightKosherized_carry_on_Continue()
        {
            // FIRST Tech 7 fail band: next Fight Test is Kosherized, Continue → NEXT Fight 10 Kosherized.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.TechBonus = 0;
            game.CurrentPlayer.FightBonus = 20; // gear proxy — Kosherized must ignore on NEXT
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_secure-perimeter"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // FIRST: die 1 + Tech 0 = 1 → nextFightKosherized + Continue
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var firstError,
                ScriptedRng.FromDieFaces(1)));
            Assert.Contains("next", firstError, StringComparison.OrdinalIgnoreCase);
            Assert.True(game.PendingMisbehave!.NextFightKosherized);

            // NEXT Fight 10 Kosherized: Jayne dice only; faces 1,1 → kill+botch
            Assert.True(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1)), error);
            Assert.True(resolution!.SkillCheck!.Check.Kosherized);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
            Assert.Equal(1, resolution.CrewKilled);
        }

        [Fact]
        public void Batch1_recalcitrant_official_nextTalkBonus_from_structured_effects()
        {
            // FIRST Tech 10 → 5-9: +1 Negotiate to next Test, Continue.
            // DiceCount uses Tech/Talk skill — set bonuses so scripted faces are consumed.
            var game = NewCrimeGame();
            game.CurrentPlayer.TechBonus = 1;
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_recalcitrant-official"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // FIRST: die 6 + Tech 1 = 7 → +1 nextTalk, Continue (5-9 band)
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var firstError,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("next", firstError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, game.PendingMisbehave!.NextTalkBonus);

            // NEXT Negotiate 10: die 5 + carry 1 = 6 → Attempt Botched (6-9).
            // Without the +1 carry, die 5 alone hits 1-5 Warrant Issued.
            Assert.True(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Equal(0, resolution.WarrantsIssued);
            Assert.Equal(0, game.PendingMisbehave?.NextTalkBonus ?? 0);
        }

        [Fact]
        public void Batch1_gun_play_disgruntleAllCrew_via_structured_effects()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_gun-play"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.All(game.CurrentPlayer.Roster.Members, m => Assert.True(m.Disgruntled));
        }

        [Fact]
        public void Batch1_kill_the_alarm_tech_proceed_via_structured_bands()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.TechBonus = 4;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_kill-the-alarm"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(2)), error);
            Assert.True(resolution!.Option!.HasStructuredBands);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
        }

        [Fact]
        public void Batch1_unification_day_fail_marks_Crew_now_Wanted()
        {
            // Locked: typo fix — "Crew is now Wanted" (not "not Wanted").
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_unification-day-festivities"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Wanted);
            var failBand = Assert.Single(
                resolution.Option!.Steps[0].Bands,
                b => b.Min == 1 && b.Max == 6);
            Assert.Contains("now Wanted", failBand.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(failBand.Effects, e => e.Is(MisbehaveLocalEffectType.Wanted));
        }

        [Fact]
        public void Batch1_unification_day_Negotiate_success_is_8_plus()
        {
            // Locked: 1–7 Botched, 8+ Proceed (printed 9+ was inverted/wrong).
            var catalog = MisbehaveCatalog.LoadDefault();
            var step = catalog.Get("misbehave_unification-day-festivities").Options[0].Steps[1];
            Assert.Equal(8, step.SkillCheck!.Target);
            var success = Assert.Single(step.Bands, b => b.Min == 8 && b.Max == null);
            Assert.Contains("8+", success.Text);
            Assert.DoesNotContain(step.Bands, b => b.Min == 9);

            var game = NewCrimeGame(cash: 700);
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_wash"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_unification-day-festivities"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // FIRST Fight 7 Kosherized: Jayne Fight 2 → Continue
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var firstError,
                ScriptedRng.FromDieFaces(6, 6)));
            Assert.Contains("next", firstError, StringComparison.OrdinalIgnoreCase);

            // NEXT step resume clears SkillCheck → Bribe PendingChoice (GF9 p.6).
            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var bribeError,
                ScriptedRng.FromDieFaces(1)));
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);
            Assert.Contains("Bribes", bribeError);

            // Wash Talk 1 + bribe $700 = 1+7 → 8 Proceed (was a gap under literal 9+)
            Assert.True(resolver.TryResumeBribeAmount(
                game, "p1",
                new ChoiceSubmission { Amount = 700 },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(8, resolution.SkillCheck!.Total);
        }

        [Fact]
        public void Batch1_secure_perimeter_Take_2_Goods_suspends_GoodsMix_then_Proceed()
        {
            // Blue Sun: "Goods are Cargo, Contraband, Fuel and Parts… you may choose to Load a mix."
            // Fess has Tech 2 without skillReroll may (Kaylee would suspend re-roll).
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_fess_kalidasa"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_jayne"), out _));
            game.CurrentPlayer.Cargo = 0;
            game.CurrentPlayer.Fuel = 0;
            game.CurrentPlayer.Parts = 0;
            game.CurrentPlayer.Contraband = 0;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_secure-perimeter"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // FIRST Tech 7: Fess Tech 2 → 7+ Continue (no nextFightKosherized)
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var firstError,
                ScriptedRng.FromDieFaces(4, 3)));
            Assert.Contains("next", firstError, StringComparison.OrdinalIgnoreCase);
            Assert.False(game.PendingMisbehave!.NextFightKosherized);

            // NEXT Fight 10: Jayne Fight 2 → 10 → Take 2 Goods → GoodsMix
            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var goodsError,
                ScriptedRng.FromDieFaces(5, 5)));
            Assert.Contains("Goods", goodsError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PendingChoiceKinds.GoodsMix, game.PendingChoice!.Kind);
            Assert.Equal(GoodsMixContexts.Load(2), game.PendingChoice.ContextId);

            Assert.True(
                resolver.TryResumeGoodsMix(
                    game,
                    "p1",
                    new ChoiceSubmission
                    {
                        Values = new List<string> { "1", "0", "1", "0" }
                    },
                    new MisbehaveChoice { OptionIndex = 0 },
                    out var resolution,
                    out var error),
                error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(2, resolution.GoodsLoaded);
            Assert.Equal(1, game.CurrentPlayer.Fuel);
            Assert.Equal(1, game.CurrentPlayer.Cargo);
            Assert.Equal(0, game.CurrentPlayer.Parts);
            Assert.Equal(0, game.CurrentPlayer.Contraband);
        }

        [Fact]
        public void Batch1_dead_to_rights_may_skip_discard_and_Proceed()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 3;
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_fess_kalidasa"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_wash"), out _));
            game.CurrentPlayer.Roster.MarkWanted("crew_wash");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_theyve-got-us-dead-to-rights"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // FIRST Fight 8 Kosherized: Jayne Fight 2 → Continue
            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var firstError,
                ScriptedRng.FromDieFaces(6, 6)));
            Assert.Contains("next", firstError, StringComparison.OrdinalIgnoreCase);

            // NEXT Tech 9: Fess Tech 2 → 9 → may discard → suspend
            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var choiceError,
                ScriptedRng.FromDieFaces(6, 3)));
            Assert.Equal(PendingChoiceKinds.MisbehaveWarrantOrWanted, game.PendingChoice!.Kind);
            Assert.Contains("discard", choiceError, StringComparison.OrdinalIgnoreCase);

            Assert.True(
                resolver.TryResumeWarrantOrWanted(
                    game,
                    "p1",
                    new ChoiceSubmission { SelectedOptionId = MisbehaveWarrantOrWantedOptions.None },
                    new MisbehaveChoice { OptionIndex = 0 },
                    out var resolution,
                    out var error),
                error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(3, game.CurrentPlayer.Warrants);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_wash")!.Wanted);
        }

        [Fact]
        public void Batch1_dead_to_rights_discard_up_to_2_Warrants()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 3;
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_fess_kalidasa"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_theyve-got-us-dead-to-rights"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6, 6)));

            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6, 3)));

            Assert.True(
                resolver.TryResumeWarrantOrWanted(
                    game,
                    "p1",
                    new ChoiceSubmission
                    {
                        SelectedOptionId = MisbehaveWarrantOrWantedOptions.Warrants,
                        Amount = 2
                    },
                    new MisbehaveChoice { OptionIndex = 0 },
                    out var resolution,
                    out var error),
                error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(1, game.CurrentPlayer.Warrants);
        }

        [Fact]
        public void Batch1_dead_to_rights_discard_up_to_2_Wanted_Tokens_chosen_crew()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 2;
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_fess_kalidasa"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_wash"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_accountant_breakinatmo"), out _));
            game.CurrentPlayer.Roster.MarkWanted("crew_fess_kalidasa");
            game.CurrentPlayer.Roster.MarkWanted("crew_wash");
            game.CurrentPlayer.Roster.MarkWanted("crew_accountant_breakinatmo");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_theyve-got-us-dead-to-rights"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6, 6)));

            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6, 2, 1)));

            Assert.True(
                resolver.TryResumeWarrantOrWanted(
                    game,
                    "p1",
                    new ChoiceSubmission
                    {
                        SelectedOptionId = MisbehaveWarrantOrWantedOptions.WantedTokens,
                        Values = new List<string> { "crew_fess_kalidasa", "crew_accountant_breakinatmo" }
                    },
                    new MisbehaveChoice { OptionIndex = 0 },
                    out var resolution,
                    out var error),
                error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(2, game.CurrentPlayer.Warrants); // warrants untouched
            Assert.False(game.CurrentPlayer.Roster.Find("crew_fess_kalidasa")!.Wanted);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_wash")!.Wanted);
            Assert.False(game.CurrentPlayer.Roster.Find("crew_accountant_breakinatmo")!.Wanted);
        }

        [Fact]
        public void Batch1_dead_to_rights_rejects_mixing_Warrants_and_Wanted()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 3;
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_fess_kalidasa"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_wash"), out _));
            game.CurrentPlayer.Roster.MarkWanted("crew_wash");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_theyve-got-us-dead-to-rights"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6, 6)));

            Assert.False(resolver.TryResumeMisbehaveOption(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveSteps.StepOptionId(1) },
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6, 3)));

            Assert.False(
                resolver.TryResumeWarrantOrWanted(
                    game,
                    "p1",
                    new ChoiceSubmission
                    {
                        SelectedOptionId = MisbehaveWarrantOrWantedOptions.Warrants,
                        Amount = 1,
                        Values = new List<string> { "crew_wash" }
                    },
                    new MisbehaveChoice { OptionIndex = 0 },
                    out _,
                    out var error));
            Assert.Contains("mix", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, game.CurrentPlayer.Warrants);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_wash")!.Wanted);
            Assert.Equal(PendingChoiceKinds.MisbehaveWarrantOrWanted, game.PendingChoice!.Kind);
        }

        [Fact]
        public void Batch2_alliance_patrol_loads_minusPerWarrant_target_modifier()
        {
            var catalog = MisbehaveCatalog.LoadDefault();
            var card = catalog.Get("misbehave_alliance-patrol");
            var check = card.Options[0].SkillCheck!;
            Assert.Equal(Skill.Talk, check.Skill);
            Assert.Equal(8, check.Target);
            Assert.Single(check.TargetModifiers);
            Assert.Equal(MisbehaveTargetModifierType.MinusPerWarrant, check.TargetModifiers[0].Type);
            Assert.Equal(1, check.TargetModifiers[0].Amount);
            Assert.True(card.Options[0].HasStructuredBands);
            Assert.True(card.Options[1].HasStructuredEffects);
        }

        [Fact]
        public void Batch2_alliance_patrol_warrants_lower_effective_negotiate_TN()
        {
            // Printed: Negotiate 8 - 1 for each of your Warrants. 1-7 Warrant Issued. 8+ Proceed.
            // GF9 p.6: Skill Test target + result bands under the target.
            // 2 Warrants → +2 to total vs printed 8+ band (equivalent to TN 6).
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 2;
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-patrol"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // die 6 + Talk 1 + warrant bonus 2 = 8 → Proceed (without warrants, total 6 hits 1-7).
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(0, resolution.WarrantsIssued);
            Assert.Equal(2, game.CurrentPlayer.Warrants); // existing warrants kept
        }

        [Fact]
        public void Batch2_alliance_patrol_zero_warrants_still_needs_8()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 0;
            game.CurrentPlayer.TalkBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-patrol"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // die 5 + Talk 1 = 6 → Warrant Issued
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(5)), error);
            Assert.Equal(1, resolution!.WarrantsIssued);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
        }

        [Fact]
        public void Batch2_alliance_patrol_transport_option_botches_without_warrant()
        {
            // Requires Transport. Attempt Botched. — clean abort when Transport present
            // (avoids Warrant Issued on the Negotiate option).
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.Gear.Add("gear_4wd-mule");
            Assert.True(GearCarriage.TryAssign(game, game.CurrentPlayer, "gear_4wd-mule", "crew_jayne", out var gearError), gearError);
            Assert.True(MisbehaveResolver.HasTag(game, game.CurrentPlayer, "Transport"));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-patrol"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Equal(0, resolution.WarrantsIssued);
        }

        [Fact]
        public void Batch2_dockside_scanner_tech_warrant_via_structured_bands()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.TechBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_dockside-scanner"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(3)), error);
            Assert.Equal(1, resolution!.WarrantsIssued);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
        }

        [Fact]
        public void Batch2_knife_fight_knife_bonus_via_structured_skillCheck()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.Gear.Add("gear_knife");
            Assert.True(GearCarriage.TryAssign(game, game.CurrentPlayer, "gear_knife", "crew_jayne", out var gearError), gearError);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_knife-fight"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Fight dice + structured +3 with Knife → Proceed at target 8.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 6, 6)), error);
            Assert.Equal(3, resolution!.Option!.SkillCheck!.Bonuses[0].Amount);
            Assert.Equal("Knife", resolution.Option.SkillCheck.Bonuses[0].Tag);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution.Outcome);
        }

        [Fact]
        public void Batch2_locals_in_need_discardCargo_clears_disgruntled()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_zoe"), out _));
            game.CurrentPlayer.Roster.DisgruntleWhere(_ => true);
            game.CurrentPlayer.Cargo = 2;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_locals-in-need"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(1, game.CurrentPlayer.Cargo);
            Assert.All(game.CurrentPlayer.Roster.Members, m => Assert.False(m.Disgruntled));
        }

        [Fact]
        public void Batch2_locals_in_need_discardCargo_fails_without_cargo()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Cargo = 0;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_locals-in-need"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var error));
            Assert.Contains("Cargo", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Batch2_a_bit_of_local_color_kosherized_disgruntleAllCrew()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_wash"), out _));
            game.CurrentPlayer.FightBonus = 10; // Kosherized ignores gear proxy
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-bit-of-local-color"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Decline Zoe-style re-roll if any; Jayne Fight dice only under Kosherized.
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { AcceptReroll = false }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1, 1, 1, 1)), error);
            Assert.True(resolution!.SkillCheck!.Check.Kosherized);
            Assert.Equal(MisbehaveOutcome.Botched, resolution.Outcome);
            Assert.All(game.CurrentPlayer.Roster.Members, m => Assert.True(m.Disgruntled));
        }

        [Fact]
        public void Batch2_lock_dangerous_gossip_discards_inactive_hand_only()
        {
            // FAQ 4.1: Jobs in hand = Inactive; Active Jobs clear only by complete / Warrant while working.
            // Christopher lock: Discard all Jobs in Hand = inactive hand only.
            var game = NewCrimeGame();
            var handJob = game.Jobs!.Cards.Values.First(j => j.Id != Crime).Id;
            game.CurrentPlayer.JobHand.Add(handJob);
            Assert.Contains(Crime, game.CurrentPlayer.JobHand);
            StartCrime(game);
            Assert.NotNull(game.CurrentPlayer.FindActive(Crime));
            Assert.DoesNotContain(Crime, game.CurrentPlayer.JobHand); // moved to Active on Work
            Assert.Contains(handJob, game.CurrentPlayer.JobHand);

            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_dangerous-gossip"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Empty(game.CurrentPlayer.JobHand);
            Assert.NotNull(game.CurrentPlayer.FindActive(Crime)); // Active untouched
        }

        [Fact]
        public void Batch2_lock_improbably_complex_may_skip_warrant_discard()
        {
            // Christopher lock: optional; zero discards OK (Dead to Rights spirit).
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 2;
            game.CurrentPlayer.TechBonus = 4;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_improbably-complex-alliance-gizmo"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out var suspendError,
                ScriptedRng.FromDieFaces(6)));
            Assert.Contains("Warrant", suspendError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PendingChoiceKinds.MisbehaveWarrantOrWanted, game.PendingChoice!.Kind);
            Assert.DoesNotContain(
                MisbehaveWarrantOrWantedOptions.WantedTokens,
                game.PendingChoice.Options!);

            Assert.True(resolver.TryResumeWarrantOrWanted(
                game, "p1",
                new ChoiceSubmission { SelectedOptionId = MisbehaveWarrantOrWantedOptions.None },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(2, game.CurrentPlayer.Warrants);
        }

        [Fact]
        public void Batch2_lock_improbably_complex_may_discard_one_warrant()
        {
            var game = NewCrimeGame();
            game.CurrentPlayer.Warrants = 2;
            game.CurrentPlayer.TechBonus = 4;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_improbably-complex-alliance-gizmo"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out _, out _,
                ScriptedRng.FromDieFaces(6)));

            Assert.True(resolver.TryResumeWarrantOrWanted(
                game, "p1",
                new ChoiceSubmission
                {
                    SelectedOptionId = MisbehaveWarrantOrWantedOptions.Warrants,
                    Amount = 1
                },
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(1, game.CurrentPlayer.Warrants);
        }

        [Fact]
        public void Batch2_lock_thrillin_heroics_clears_moral_disgruntled_only()
        {
            // Christopher lock: Remove Disgruntled from Moral Crew = moral only.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_zoe"), out _)); // Moral
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_jayne"), out _)); // Immoral
            game.CurrentPlayer.Roster.DisgruntleWhere(_ => true);
            game.CurrentPlayer.FightBonus = 10;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_time-for-some-thrillin-heroics"));
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
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 6, 6, 6, 6)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.False(game.CurrentPlayer.Roster.Find("crew_zoe")!.Disgruntled);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
        }

        [Fact]
        public void Batch2_lock_locals_in_need_clears_moral_disgruntled_only()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_zoe"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_jayne"), out _));
            game.CurrentPlayer.Roster.DisgruntleWhere(_ => true);
            game.CurrentPlayer.Cargo = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_locals-in-need"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(0, game.CurrentPlayer.Cargo);
            Assert.False(game.CurrentPlayer.Roster.Find("crew_zoe")!.Disgruntled);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
        }

        [Fact]
        public void Batch3_stop_and_frisk_submit_seizes_FIREARM_and_disgruntles_Wanted()
        {
            // Director's Cut C&P p.49 Equipment Seizures: Gear removed from the game.
            // Printed: All FIREARMS are Seized. If you discard any, disgruntle all Wanted Crew.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.Roster.MarkWanted("crew_jayne");
            GiveCarriedGear(game, "gear_pistol", "crew_jayne");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_stop-and-frisk"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.DoesNotContain("gear_pistol", game.CurrentPlayer.Gear);
            Assert.Contains("gear_pistol", game.RemovedFromPlay);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
        }

        [Fact]
        public void Batch3_stop_and_frisk_bribes_fail_seizes_FIREARM_and_botches()
        {
            var game = NewCrimeGame(cash: 0);
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            // Negotiate needs ≥1 Talk die so the 1-11 seize band can match (empty roster Talk = 0 dice).
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_inara"), out _));
            GiveCarriedGear(game, "gear_pistol", "crew_jayne");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_stop-and-frisk"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 0, AcceptReroll = false }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Contains("gear_pistol", game.RemovedFromPlay);
            Assert.False(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
        }

        [Fact]
        public void Batch3_security_scare_seizes_sniper_and_explosives_disgruntles_carriers()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            GiveCarriedGear(game, "gear_vera", "crew_jayne"); // Firearm + Sniper Rifle
            GiveCarriedGear(game, "gear_explosive-charge", "crew_zoe");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_security-scare"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Contains("gear_vera", game.RemovedFromPlay);
            Assert.Contains("gear_explosive-charge", game.RemovedFromPlay);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_zoe")!.Disgruntled);
        }

        [Fact]
        public void Batch3_check_point_returns_Wanted_to_Ship()
        {
            // Director's Cut C&P p.49 I'll Be in my Bunk.
            // Jayne/Zoe are printed Wanted — keep Kaylee (not Wanted) on the Job so
            // "No one left" does not Botch.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_kaylee"), out _));
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_check-point"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
            var active = game.CurrentPlayer.FindActive(Crime);
            Assert.NotNull(active);
            Assert.True(active!.IsReturnedToShip("crew_jayne"));
            Assert.False(active.IsReturnedToShip("crew_kaylee"));
            Assert.Equal(1, JobWorkCrew.AvailableCount(game.CurrentPlayer));
        }

        [Fact]
        public void Batch3_check_point_all_Wanted_returned_Botches_no_one_left()
        {
            // Director's Cut C&P p.49: all Crew Returned to Ship → Botched.
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _)); // Wanted
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_check-point"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.FindActive(Crime)!.IsReturnedToShip("crew_jayne"));
        }

        [Fact]
        public void Batch3_check_point_Wanted_roll_seize_issues_Warrant()
        {
            // Die 1 = seized (Alliance Wanted Crew Roll / Cruiser Contact).
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.Roster.MarkWanted("crew_jayne");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_check-point"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Contains("crew_jayne", game.RemovedFromPlay);
            Assert.Null(game.CurrentPlayer.Roster.Find("crew_jayne"));
        }

        [Fact]
        public void Batch3_check_point_Wanted_roll_dodge_Proceeds()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            game.CurrentPlayer.Roster.MarkWanted("crew_jayne");
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_check-point_2"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(0, resolution.WarrantsIssued);
            Assert.NotNull(game.CurrentPlayer.Roster.Find("crew_jayne"));
        }

        [Fact]
        public void Batch3_alarm_Wanted_roll_and_loseSolid_if_able_botch()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            // Not Solid — loseSolidIfAble must no-op (printed "if able").
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_you-set-off-the-alarm"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
        }

        [Fact]
        public void Batch3_grumpy_returns_highest_Fight_to_Ship()
        {
            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _)); // Fight 2
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew.Get("crew_kaylee"), out _)); // Fight 0
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_grumpy-malcontent"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 1 },
                out var resolution, out var error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            var active = game.CurrentPlayer.FindActive(Crime)!;
            Assert.True(active.IsReturnedToShip("crew_jayne"));
            Assert.True(game.CurrentPlayer.Roster.Find("crew_jayne")!.Disgruntled);
            Assert.False(active.IsReturnedToShip("crew_kaylee"));
        }

        private static void GiveCarriedGear(GameState game, string gearId, string crewId)
        {
            game.CurrentPlayer.Gear.Add(gearId);
            Assert.True(GearCarriage.TryAssign(game, game.CurrentPlayer, gearId, crewId, out var error), error);
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
