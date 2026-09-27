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
    /// Slice 3 crew ability trio: Lund Cheap Shot, Two-Fry Dead-Eye, Sheriff Bourne Jurisdiction
    /// (+ Holdout / Vector Kosherized gear). FAQ 4.1 Lund/Two-Fry; Supplies.tsv printed text.
    /// </summary>
    public class CrewAbilityTrioTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Regina = "border-georgia-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();
        private static GearIndex Gear => GearIndex.LoadDefault();

        private static GameState NewGame(string sector = Persephone)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sector, cash: 500, fuel: 3, driveRange: 5);
            var catalog = MisbehaveCatalog.LoadDefault();
            return new GameState(map, new[] { player })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                ContactDecks = new ContactDecks(JobCatalog.LoadDefault(), new SystemRng(1)),
                Crew = Crew,
                Leaders = LeaderCatalog.LoadDefault(),
                Gear = Gear,
                Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog)
            };
        }

        private static void GiveCarriedGear(GameState game, string gearId, string crewId)
        {
            var player = game.CurrentPlayer;
            if (!player.Gear.Contains(gearId))
                player.Gear.Add(gearId);
            Assert.True(
                GearCarriage.TryAssign(game, player, gearId, crewId, out var error),
                error);
        }

        private static void ForceActiveAlert(GameState game, string name)
        {
            var catalog = game.AllianceAlerts ?? AllianceAlertCatalog.LoadDefault();
            game.AllianceAlerts = catalog;
            Assert.True(catalog.TryResolve(name, out var card), name);
            game.AllianceAlertDeck = new AllianceAlertDeck(new[] { card }, new SystemRng(1), catalog);
            game.AllianceAlertDeck.DrawAndActivate();
        }

        [Fact]
        public void Trio_json_loads_typed_abilities()
        {
            var lund = Crew.Get("crew_lund");
            Assert.Contains(lund.Abilities, a =>
                a.MatchesType(AbilityTypes.UseCarriedGearInKosherized) && a.Mandatory);

            var twoFry = Crew.Get("crew_two-fry");
            Assert.Contains(twoFry.Abilities, a =>
                a.MatchesType(AbilityTypes.MisbehaveDrawReduce)
                && a.Amount == 1
                && a.Subject == "Sniper Rifle"
                && a.JobOnly
                && a.Mandatory);

            var bourne = Crew.Get("crew_sheriff-bourne_piratesbountyhunters");
            Assert.Contains(bourne.Abilities, a =>
                a.MatchesType(AbilityTypes.SkillAddend)
                && a.Skill == "Fight"
                && a.Amount == 2
                && a.Location == "Border"
                && a.Mandatory);

            Assert.True(Gear.TryGet("gear_jaynes-holdout-pistol_kalidasa", out var holdout));
            Assert.Contains(holdout.Abilities, a => a.MatchesType(AbilityTypes.UseInKosherized));
            Assert.Contains(holdout.Abilities, a => a.MatchesType(AbilityTypes.ExemptFromGearLimit));

            Assert.True(Gear.TryGet("gear_dobsons-vector-pistol_piratesbountyhunters", out var vector));
            Assert.Contains(vector.Abilities, a => a.MatchesType(AbilityTypes.UseInKosherized));
        }

        [Fact]
        public void Lund_adds_carried_gear_fight_in_kosherized()
        {
            // FAQ 4.1: "Lund's special ability allows him to add Fight Skill from any Gear he is carrying."
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_lund"), out _));
            GiveCarriedGear(game, "gear_vera", "crew_lund"); // Fight 2

            var kosher = new SkillCheck(Skill.Fight, 6, kosherized: true);
            // Lund Fight 2 + Vera 2
            Assert.Equal(4, kosher.DiceCount(player, game));

            // Same gear on Jayne (no Lund ability) stays excluded.
            var other = NewGame();
            Assert.True(other.CurrentPlayer.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            GiveCarriedGear(other, "gear_vera", "crew_jayne");
            Assert.Equal(2, kosher.DiceCount(other.CurrentPlayer, other));
        }

        [Fact]
        public void Holdout_and_Vector_add_fight_in_kosherized()
        {
            // Printed: "May be used in Kosherized Fights."
            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            GiveCarriedGear(game, "gear_jaynes-holdout-pistol_kalidasa", "crew_jayne");

            var kosher = new SkillCheck(Skill.Fight, 6, kosherized: true);
            // Jayne Fight 2 + Holdout Fight 1
            Assert.Equal(3, kosher.DiceCount(player, game));

            var game2 = NewGame();
            Assert.True(game2.CurrentPlayer.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            GiveCarriedGear(game2, "gear_dobsons-vector-pistol_piratesbountyhunters", "crew_jayne");
            Assert.Equal(3, kosher.DiceCount(game2.CurrentPlayer, game2));
        }

        [Fact]
        public void Ordinary_gear_still_excluded_from_kosherized()
        {
            var game = NewGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            GiveCarriedGear(game, "gear_pistol", "crew_jayne");
            var kosher = new SkillCheck(Skill.Fight, 6, kosherized: true);
            Assert.Equal(2, kosher.DiceCount(game.CurrentPlayer, game));
        }

        [Fact]
        public void Bourne_adds_two_fight_in_border_including_kosherized()
        {
            // Jurisdiction: +2 Fight while in Border Space — counts in Kosherized (Christopher).
            var border = NewGame(Regina);
            Assert.True(border.CurrentPlayer.Roster.TryHire(
                Crew.Get("crew_sheriff-bourne_piratesbountyhunters"), out _));
            Assert.True(border.CurrentPlayer.Roster.TryHire(Crew.Get("crew_jayne"), out _));

            var normal = new SkillCheck(Skill.Fight, 6);
            // Jayne 2 + Bourne card Fight 0 + Jurisdiction +2
            Assert.Equal(4, normal.DiceCount(border.CurrentPlayer, border));

            var kosher = new SkillCheck(Skill.Fight, 6, kosherized: true);
            Assert.Equal(4, kosher.DiceCount(border.CurrentPlayer, border));

            var alliance = NewGame(Persephone);
            Assert.True(alliance.CurrentPlayer.Roster.TryHire(
                Crew.Get("crew_sheriff-bourne_piratesbountyhunters"), out _));
            Assert.True(alliance.CurrentPlayer.Roster.TryHire(Crew.Get("crew_jayne"), out _));
            Assert.Equal(2, normal.DiceCount(alliance.CurrentPlayer, alliance));
            Assert.Equal(2, kosher.DiceCount(alliance.CurrentPlayer, alliance));
        }

        [Fact]
        public void TwoFry_reduces_misbehave_draw_when_carrying_sniper_rifle()
        {
            // Dead-Eye: carrying Sniper Rifle on Jobs → draw 1 fewer Misbehave, min 1.
            // Casino Caper: Misbehave 3 at Santo.
            var game = NewGame(Santo);
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_two-fry"), out _));
            GiveCarriedGear(game, "gear_vera", "crew_two-fry");
            player.JobHand.Add(Crime);

            var job = game.Jobs!.Get(Crime);
            var terms = JobTerms.Pickup(job);
            Assert.Equal(3, terms.Misbehave);

            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var result, out var error), error);
            Assert.True(result!.AwaitingMisbehave);
            Assert.NotNull(game.PendingMisbehave);
            Assert.Equal(2, game.PendingMisbehave!.Remaining);
        }

        [Fact]
        public void TwoFry_does_not_reduce_without_sniper_or_below_one()
        {
            var noRifle = NewGame(Santo);
            Assert.True(noRifle.CurrentPlayer.Roster.TryHire(Crew.Get("crew_two-fry"), out _));
            noRifle.CurrentPlayer.JobHand.Add(Crime);

            var work = new WorkAction();
            Assert.True(work.TryWork(noRifle, "p1", Crime, out _, out var error), error);
            Assert.Equal(3, noRifle.PendingMisbehave!.Remaining);

            // Min 1: cannot reduce a 1-card draw to 0.
            var game = NewGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_two-fry"), out _));
            GiveCarriedGear(game, "gear_vera", "crew_two-fry");
            Assert.Equal(1, AbilityDispatcher.ApplyMisbehaveDrawReduce(
                game, game.CurrentPlayer, 1));
            Assert.Equal(0, AbilityDispatcher.ApplyMisbehaveDrawReduce(
                game, game.CurrentPlayer, 0));
        }

        [Fact]
        public void TwoFry_applies_after_alert_extra_illegal_misbehave()
        {
            var game = NewGame(Santo);
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_two-fry"), out _));
            GiveCarriedGear(game, "gear_vera", "crew_two-fry");
            player.JobHand.Add(Crime);
            player.Warrants = 1;
            ForceActiveAlert(game, "Criminal Activity");

            // Misbehave 3 + Criminal Activity +1 = 4 → Two-Fry −1 → 3
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out var error), error);
            Assert.Equal(3, game.PendingMisbehave!.Remaining);
        }
    }
}
