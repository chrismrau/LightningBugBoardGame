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
    /// Leftover <c>may</c> / related abilities after PRs #39–#40.
    /// FAQ 4.1 p.8: mandatory unless “may.”
    /// </summary>
    public class MayAbilityLeftoverTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Persephone = "alliance-lux-r1-01";

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();
        private static LeaderCatalog Leaders => LeaderCatalog.LoadDefault();
        private static GearIndex Gear => GearIndex.LoadDefault();

        [Fact]
        public void Card_json_loads_leftover_ability_batch()
        {
            Assert.Contains(Crew.Get("crew_dalin_piratesbountyhunters").Abilities, a =>
                a.MatchesType(AbilityTypes.MisbehaveDiscardRedraw) && a.Amount == 200 && !a.Mandatory);

            Assert.Contains(Crew.Get("crew_roberta_kalidasa").Abilities, a =>
                a.MatchesType(AbilityTypes.DiscardInsteadOfLoseSolid) && !a.Mandatory);

            Assert.Contains(Crew.Get("crew_the-salesman_bluesun").Abilities, a =>
                a.MatchesType(AbilityTypes.DiscardBuyUpgradeHalf) && !a.Mandatory);

            Assert.Contains(Leaders.Get("leader_corbin").Abilities, a =>
                a.MatchesType(AbilityTypes.HalfPriceDriveAndUpgrade) && !a.Mandatory);

            Assert.True(Gear.TryGet("gear_labor-contract-persephone_kalidasa", out var labor));
            Assert.Contains(labor.Abilities, a =>
                a.MatchesType(AbilityTypes.HireFromSupplyDiscard)
                && a.Location == "Persephone"
                && !a.Mandatory);

            var upgrades = ShipUpgradeIndex.LoadDefault();
            Assert.True(upgrades.TryGet("ship-upgrade_fully-equipped-med-bay", out var medBay));
            Assert.Contains(medBay.Abilities, a =>
                a.MatchesType(AbilityTypes.MedicCheckReroll) && !a.Mandatory);

            Assert.True(Gear.TryGet("gear_two-frys-carbine_breakinatmo", out var carbine));
            Assert.Contains(carbine.Abilities, a =>
                a.MatchesType(AbilityTypes.RerollOnes) && a.Skill == "Fight" && a.Mandatory);
        }

        [Fact]
        public void Bounty_Confrontation_Negotiate_boarding_suspends_Cortland_Bribes()
        {
            // Supplies.tsv Cortland: May pay Bribes before any Negotiate Test.
            // PBH Confrontation boarding may use Negotiate — same as Piracy.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var p1 = new PlayerState("p1", "Mal", Santo, cash: 200, fuel: 0);
            var p2 = new PlayerState("p2", "Zoe", Santo, cash: 0, fuel: 0);
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Santo, shipId: "Serenity", leaderId: "Malcolm"),
                    new PlayerSeat("p2", "Zoe", Santo, shipId: "Jetwash", leaderId: "leader_zoe_jetwash")
                },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UsePiratesBountyHunters = true,
                    Rng = new SystemRng(7)
                });
            p1 = game.GetPlayer("p1");
            p2 = game.GetPlayer("p2");
            p1.Cash = 200;
            p1.SectorId = Santo;
            p2.SectorId = Santo;
            Assert.True(p1.Roster.TryHire(Crew.Get("crew_cortland_bluesun"), out _));

            // Put a Most Wanted that matches a crew on p2.
            Assert.NotNull(game.BountyDeck);
            var wanted = game.BountyDeck!.FaceUp;
            Assert.NotEmpty(wanted);
            var bounty = wanted[0];
            // Hire matching named crew onto p2 when possible; otherwise skip if name mismatch.
            CrewCard? fugitive = null;
            foreach (var c in Crew.Cards.Values)
            {
                if (bounty.MatchesCrewName(c.Name) && !c.IsLeader)
                {
                    fugitive = c;
                    break;
                }
            }
            if (fugitive == null)
                return;

            Assert.True(p2.Roster.TryHire(fugitive, out _));
            var hunt = new BountyAction();
            Assert.False(hunt.TryApprehendRival(
                game, "p1", bounty.Id, "p2", fugitive.Id,
                Skill.Fight, Skill.Fight, Skill.Talk,
                ScriptedRng.FromDieFaces(6, 6, 1),
                out _, out var error));
            Assert.Equal(PendingChoiceKinds.BribeAmount, game.PendingChoice!.Kind);

            Assert.True(hunt.TryResumeBoardingBribe(
                game,
                new ChoiceSubmission { Amount = 0 },
                ScriptedRng.FromDieFaces(6, 6, 1),
                out var result, out error), error);
            Assert.NotNull(result);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Med_Bay_Medic_Check_always_suspends_reroll()
        {
            // Supplies.tsv: "May re-roll Medic Checks."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Leaders = Leaders,
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_simon-tam"), out _)); // Medic
            Assert.True(player.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            player.ShipUpgrades.Add("ship-upgrade_fully-equipped-med-bay");
            Assert.True(AbilityDispatcher.HasMedicCheckReroll(game, player));

            var choice = new KillChoice
            {
                VictimCrewIds = new[] { "crew_kaylee" }
            };
            Assert.False(CrewKill.TryKillUpTo(
                game, player, 1, ScriptedRng.FromDieFaces(2, 6), out _, out _, choice));
            Assert.Equal(PendingChoiceKinds.MedicReroll, game.PendingChoice!.Kind);
            Assert.Equal(2, choice.MedicFirstDie);

            Assert.True(CrewKill.TryResumeMedicRerollKillUpTo(
                game,
                new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Reroll },
                ScriptedRng.FromDieFaces(6),
                out var killed, out var error, choice), error);
            Assert.Equal(0, killed); // die 6 + Simon +2 saves
            Assert.NotNull(player.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void RerollOnes_TwoFry_Carbine_mandatory_on_Fight_ones()
        {
            // Supplies.tsv: "Reroll any Fight Test results of 1." — no “may” → mandatory.
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Gear = Gear, Crew = Crew };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            player.Gear.Add("gear_two-frys-carbine_breakinatmo");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_two-frys-carbine_breakinatmo", player.Roster.Leader!.Id, out _));

            var check = new SkillCheck(Skill.Fight, 5);
            // Malcolm Fight 2 + Carbine Fight 1 = 3 dice; ones re-roll mandatorily.
            Assert.True(check.TryResolve(
                player, ScriptedRng.FromDieFaces(1, 1, 1, 5, 5, 5), out var result, out _,
                game: game));
            Assert.Equal(new[] { 5, 5, 5 }, result.Roll.Faces);
            Assert.True(result.Success);
        }

        [Fact]
        public void Roberta_suspends_instead_of_losing_Solid()
        {
            // Supplies.tsv: "You May Discard Roberta instead of losing Solid Rep with a Contact."
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player })
            {
                Crew = Crew,
                Contacts = ContactCatalog.LoadDefault()
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_roberta_kalidasa"), out _));
            player.BecomeSolid("Badger");

            Assert.False(ContactSolidBenefits.TryLoseSolid(
                game, player, "Badger", null, out var error));
            Assert.Equal(PendingChoiceKinds.DiscardOrLoseSolid, game.PendingChoice!.Kind);

            Assert.True(ContactSolidBenefits.TryResumeDiscardOrLoseSolid(
                game,
                new ChoiceSubmission { SelectedOptionId = DiscardOrLoseSolidOptions.DiscardCrew },
                null,
                out error), error);
            Assert.Null(player.Roster.Find("crew_roberta_kalidasa"));
            Assert.True(player.IsSolidWith("Badger"));
        }

        [Fact]
        public void Fine_Hat_and_Cortex_refresh_DealModifiers()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 0);
            var game = new GameState(map, new[] { player }) { Gear = Gear, Crew = Crew };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(Crew.Get("crew_jayne"), out _)); // May Carry 3

            player.Gear.Add("gear_a-very-fine-hat");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_a-very-fine-hat", "crew_jayne", out _));
            Assert.Equal(4, player.Deal.ConsiderUpTo);

            player.Gear.Add("gear_cortex-uplink_breakinatmo");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_cortex-uplink_breakinatmo", "crew_jayne", out var err), err);
            Assert.True(player.Deal.ConsiderTopCardFromAnyContact);
            Assert.True(player.Deal.CanDealFromAnySector);
        }

        [Fact]
        public void Corbin_halves_Drive_and_Upgrade_Buy_cost()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Corbin", Persephone, cash: 1000, fuel: 0);
            var game = new GameState(map, new[] { player }) { Leaders = Leaders };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_corbin"), out _));
            var upgrade = new SupplyCard(
                "ship-upgrade_stash", "Stash", 1000, SupplyKind.ShipUpgrade);
            Assert.Equal(500, BuyAction.CardBuyCost(game, player, upgrade));
        }

        [Fact]
        public void Labor_Contract_hires_from_named_discard_any_sector()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            var player = game.CurrentPlayer;
            player.Gear.Add("gear_labor-contract-persephone_kalidasa");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_labor-contract-persephone_kalidasa",
                player.Roster.Leader!.Id, out _));

            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            SupplyCard? crewCard = null;
            foreach (var c in market.Discard)
            {
                if (c.Kind == SupplyKind.Crew)
                {
                    crewCard = c;
                    break;
                }
            }
            if (crewCard == null)
            {
                // Prime a crew into discard for the test.
                foreach (var face in market.FaceUp)
                {
                    if (face.Kind == SupplyKind.Crew)
                    {
                        market.TryTake(face.Id, out var taken);
                        market.Discard.Add(taken);
                        crewCard = taken;
                        market.Refill();
                        break;
                    }
                }
            }
            if (crewCard == null)
                return;

            var buy = new BuyAction();
            Assert.True(buy.TryBuy(
                game, "p1",
                new BuyRequest
                {
                    HireFromDiscardId = crewCard.Id,
                    HireFromDiscardPlanet = "Persephone"
                },
                out var result, out var error), error);
            Assert.Equal(0, result!.CashSpent);
            Assert.NotNull(player.Roster.Find(crewCard.Id));
        }
    }
}
