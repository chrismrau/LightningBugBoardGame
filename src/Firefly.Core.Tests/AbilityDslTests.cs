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
    /// Slice 6 first batch: typed ability DSL + gear carriage.
    /// FAQ 4.1 p.8 mandatory-unless-may; FAQ 4.1 p.2 / GF9 p.14 gear carriage.
    /// </summary>
    public class AbilityDslTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Persephone = "alliance-lux-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();
        private static LeaderCatalog Leaders => LeaderCatalog.LoadDefault();
        private static GearIndex Gear => GearIndex.LoadDefault();

        private static GameState NewGame(string sector = Persephone)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sector, cash: 500, fuel: 3, driveRange: 5);
            var game = new GameState(map, new[] { player })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = Crew,
                Leaders = Leaders,
                Gear = Gear
            };
            return game;
        }

        [Fact]
        public void Crew_json_loads_typed_abilities_for_first_batch()
        {
            var fendris = Crew.Get("crew_fendris");
            Assert.Contains(fendris.Abilities, a => a.MatchesType(AbilityTypes.RedirectLeaderDisgruntle));

            var simon = Crew.Get("crew_simon-tam");
            Assert.Contains(simon.Abilities, a => a.MatchesType(AbilityTypes.MedicCheckBonus) && a.Amount == 2);
            Assert.Contains(simon.Abilities, a =>
                a.MatchesType(AbilityTypes.GiftedRollBonus) && a.Amount == 2
                && a.Subject == "River Tam");

            var wash = Crew.Get("crew_wash");
            Assert.Contains(wash.Abilities, a => a.MatchesType(AbilityTypes.FullBurnRangeBonus) && a.Amount == 1);

            var jayne = Crew.Get("crew_jayne");
            Assert.Contains(jayne.Abilities, a => a.MatchesType(AbilityTypes.GearCarryLimit) && a.Amount == 3);

            var promo = Crew.Get("crew_kaylee_promo");
            Assert.Contains(promo.Abilities, a =>
                a.MatchesType(AbilityTypes.MisbehaveProceedCash) && a.Amount == 100 && a.JobOnly);
        }

        [Fact]
        public void Fendris_redirects_leader_disgruntle_instead()
        {
            // FAQ 4.1 p.8: "if your leader is Disgruntled, you must Disgruntle Fendris instead."
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_fendris"), out _));
            var leader = player.Roster.Leader!;
            var fendris = player.Roster.Find("crew_fendris")!;

            var outcome = player.Roster.Disgruntle(leader);
            Assert.Equal(CrewOutcome.Disgruntled, outcome);
            Assert.False(leader.Disgruntled);
            Assert.True(fendris.Disgruntled);
        }

        [Fact]
        public void Entire_crew_disgruntle_with_Fendris_already_tokened_makes_him_jump()
        {
            // FAQ 4.1 p.8: entire Crew Disgruntled → Fendris receives two tokens and leaves.
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_fendris"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            player.Roster.Find("crew_fendris")!.Disgruntled = true;

            player.Roster.DisgruntleWhere(_ => true);
            Assert.Null(player.Roster.Find("crew_fendris"));
            Assert.False(player.Roster.Leader!.Disgruntled); // redirect consumed by Fendris jump
            Assert.True(player.Roster.Find("crew_kaylee")!.Disgruntled);
        }

        [Fact]
        public void Simon_medic_bonus_comes_from_typed_ability()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_simon-tam"), out _));
            Assert.Equal(2, AbilityDispatcher.MedicCheckBonus(player));
            Assert.Equal(2, CrewKill.MedicBonus(player));
        }

        [Fact]
        public void Simon_adds_plus_two_to_River_Gifted_roll_total()
        {
            // FAQ 4.1 p.8: Simon +2 to River's Gifted rolls is mandatory.
            // Full Gifted outcome bands are not in Supplies.tsv ("See card") — total only.
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_river-tam"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_simon-tam"), out _));

            var result = GiftedRoll.Roll(player, ScriptedRng.FromDieFaces(3));
            Assert.Equal(3, result.Die);
            Assert.Equal(2, result.Bonus);
            Assert.Equal(5, result.Total);
        }

        [Fact]
        public void Gifted_roll_without_Simon_has_no_bonus()
        {
            var game = NewGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_river-tam"), out _));
            var result = GiftedRoll.Roll(game.CurrentPlayer, ScriptedRng.FromDieFaces(4));
            Assert.Equal(4, result.Total);
            Assert.Equal(0, result.Bonus);
        }

        [Fact]
        public void Wash_adds_one_to_full_burn_range()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.Equal(5, player.EffectiveDriveRange);
            Assert.True(player.Roster.TryHire(Crew.Get("crew_wash"), out _));
            Assert.Equal(6, player.EffectiveDriveRange);
        }

        [Fact]
        public void Carried_gear_skills_refresh_player_bonuses_onboard_does_not()
        {
            // FAQ 4.1 p.2: Onboard Ship Gear may not be used in any way.
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            player.Gear.Add("gear_kaylees-reprogrammer"); // Tech 2
            GearCarriage.RefreshSkillBonuses(game, player);
            Assert.Equal(0, player.TechBonus);

            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_kaylees-reprogrammer", "crew_kaylee", out var error), error);
            Assert.Equal(2, player.TechBonus);
            Assert.Equal(player.Roster.Tech + 2, player.Tech);
        }

        [Fact]
        public void Jayne_may_carry_three_gear_default_crew_one()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            Assert.Equal(3, AbilityDispatcher.GearCarryLimit(player.Roster.Find("crew_jayne")!));
            Assert.Equal(1, AbilityDispatcher.GearCarryLimit(player.Roster.Find("crew_kaylee")!));

            player.Gear.Add("gear_vera");
            player.Gear.Add("gear_glucklich-jia-642x");
            player.Gear.Add("gear_bona-fide-credentials");
            Assert.True(GearCarriage.TryAssign(game, player, "gear_vera", "crew_jayne", out _));
            Assert.True(GearCarriage.TryAssign(game, player, "gear_glucklich-jia-642x", "crew_jayne", out _));
            Assert.True(GearCarriage.TryAssign(game, player, "gear_bona-fide-credentials", "crew_jayne", out _));

            player.Gear.Add("gear_a-very-fine-hat");
            Assert.True(GearCarriage.TryAssign(game, player, "gear_a-very-fine-hat", "crew_kaylee", out _));
            player.Gear.Add("gear_hastily-forged-documents");
            Assert.False(GearCarriage.TryAssign(
                game, player, "gear_hastily-forged-documents", "crew_kaylee", out var err));
            Assert.Contains("maximum Gear", err);
        }

        [Fact]
        public void Exempt_gear_does_not_count_toward_limit()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            var hat = "gear_jaynes-cunning-hat";
            Assert.True(Gear.TryGet(hat, out var entry));
            Assert.True(AbilityDispatcher.GearExemptFromLimit(entry));
            player.Gear.Add(hat);
            player.Gear.Add("gear_a-very-fine-hat");
            Assert.True(GearCarriage.TryAssign(game, player, hat, "crew_kaylee", out _));
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_a-very-fine-hat", "crew_kaylee", out var error), error);
        }

        [Fact]
        public void Cannot_switch_gear_during_work_action()
        {
            // FAQ 4.1 p.2: "The only time you may not switch Gear is during a Work Action."
            var game = NewGame(Santo);
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            player.Gear.Add("gear_a-very-fine-hat");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_a-very-fine-hat", "crew_kaylee", out _));
            player.JobHand.Add(Crime);
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(1), catalog);
            game.ContactDecks = new ContactDecks(game.Jobs!, new SystemRng(1));

            Assert.True(new WorkAction().TryWork(game, "p1", Crime, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(game.WorkGearLocked);

            player.Gear.Add("gear_4wd-mule");
            Assert.False(GearCarriage.TryAssign(
                game, player, "gear_4wd-mule", "crew_kaylee", out var switchError));
            Assert.Contains("Work Action", switchError);
        }

        [Fact]
        public void Onboard_gear_does_not_satisfy_misbehave_requires()
        {
            var game = NewGame(Santo);
            var player = game.CurrentPlayer;
            player.Gear.Add("gear_a-very-fine-hat"); // owned but Onboard
            player.JobHand.Add(Crime);
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(1), catalog);
            game.ContactDecks = new ContactDecks(game.Jobs!, new SystemRng(1));
            Assert.True(new WorkAction().TryWork(game, "p1", Crime, out _, out var error), error);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(game, "p1", new MisbehaveChoice { OptionIndex = 0 }, out _, out var reqError));
            Assert.Contains("FANCY DUDS", reqError, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Big_Damn_Heroes_pays_on_misbehave_proceed()
        {
            var game = NewGame(Santo);
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee_promo"), out _));
            player.JobHand.Add(Crime);
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(1), catalog);
            game.ContactDecks = new ContactDecks(game.Jobs!, new SystemRng(1));
            Assert.True(new WorkAction().TryWork(game, "p1", Crime, out _, out var error), error);
            // Companion Ace path
            Assert.True(player.Roster.TryHire(Crew.Get("crew_inara"), out _));
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_a-formal-affair"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            var cashBefore = player.Cash;
            Assert.True(resolver.TryResolve(
                game, "p1", new MisbehaveChoice { UseAce = true }, out var resolution, out error), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(cashBefore + 100, player.Cash);
        }

        [Fact]
        public void Job_only_proceed_cash_does_not_apply_while_working_goal_context()
        {
            // GF9 / Director's Cut: Job abilities do not apply while Working Goals.
            var game = NewGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee_promo"), out _));
            var goalCtx = new AbilityContext { IsWorkingGoal = true };
            Assert.Equal(0, AbilityDispatcher.MisbehaveProceedCash(game.CurrentPlayer, goalCtx));
            Assert.Equal(100, AbilityDispatcher.MisbehaveProceedCash(
                game.CurrentPlayer, AbilityContext.WorkingJob));
        }

        [Fact]
        public void May_abilities_are_not_applied_by_dispatcher()
        {
            var optional = new AbilityDefinition(
                AbilityTypes.MisbehaveProceedCash, mandatory: false, amount: 100, jobOnly: true);
            Assert.False(AbilityDispatcher.Applies(optional, AbilityContext.WorkingJob));
        }
    }
}
