using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// Remaining inert ship upgrades: Booby Trap, Mag-Grappler, Modded Fuel Catalyzer,
    /// Xunsu Emergency Ram Jets. Supplies.tsv / ShipUpgrades.json.
    /// </summary>
    public class RemainingShipUpgradeTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string ContractJumper = "job_amnon-duul_contract-jumper";

        private static (GameState Game, PlayerState Mal, PlayerState Zoe) TwoShips(int malFuel = 4)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: malFuel);
            var zoe = new PlayerState("p2", "Zoe", Persephone, fuel: 2);
            var game = new GameState(map, new[] { mal, zoe })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Leaders = LeaderCatalog.LoadDefault(),
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault(),
                UsePiratesBountyHunters = true
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1), BountyCatalog.LoadDefault());
            return (game, mal, zoe);
        }

        [Fact]
        public void BoobyTrap_Tech_boarding_applies_minus_two_and_kills_on_face_one()
        {
            var (game, mal, zoe) = TwoShips();
            zoe.ShipUpgrades.Add(BoobyTrapAction.CardId);
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            Assert.True(mal.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(mal.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));

            // Tech boarding: Mag none, Booby −2. Dice = TechBonus 5 + Jayne Tech? Jayne is Fight.
            // With TechBonus 5 and −2 → 3 dice. Faces 1,6,6 → sum 13 pass; face 1 kills.
            Assert.Equal(-2, BoardingTest.TechDiceModifier(mal, zoe, Skill.Tech));
            Assert.Equal(0, BoardingTest.TechDiceModifier(mal, zoe, Skill.Talk));

            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper,
                new PiracyChoice
                {
                    RivalId = "p2",
                    BoardSkill = Skill.Tech,
                    AttackSkill = Skill.Fight,
                    DefendSkill = Skill.Talk,
                    BoardingKill = new KillChoice
                    {
                        VictimCrewIds = new List<string> { "crew_jayne" },
                        AttemptMedicCheck = false
                    }
                },
                ScriptedRng.FromDieFaces(1, 6, 6, 6, 6, 6, 6, 1),
                out var result, out var error), error);
            Assert.False(result!.BoardingFailed);
            Assert.Null(mal.Roster.Find("crew_jayne"));
            Assert.Contains(BoobyTrapAction.CardId, zoe.ShipUpgrades);
        }

        [Fact]
        public void BoobyTrap_Negotiate_boarding_skips_penalty_and_face_one_still_kills()
        {
            var (game, mal, zoe) = TwoShips();
            zoe.ShipUpgrades.Add(BoobyTrapAction.CardId);
            mal.JobHand.Add(ContractJumper);
            mal.TalkBonus = 5;
            Assert.True(mal.Roster.TryHire(game.Crew!.Get("crew_inara"), out _));

            Assert.Equal(0, BoardingTest.TechDiceModifier(mal, zoe, Skill.Talk));
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper,
                new PiracyChoice
                {
                    RivalId = "p2",
                    BoardSkill = Skill.Talk,
                    AttackSkill = Skill.Fight,
                    DefendSkill = Skill.Talk,
                    BoardingKill = new KillChoice
                    {
                        VictimCrewIds = new List<string> { "crew_inara" },
                        AttemptMedicCheck = false
                    }
                },
                // Talk dice (Inara 3 + bonus 5 = 8); first face 1 triggers Booby kill.
                ScriptedRng.FromDieFaces(1, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 1),
                out var result, out var error), error);
            Assert.False(result!.BoardingFailed);
            Assert.Null(mal.Roster.Find("crew_inara"));
        }

        [Fact]
        public void BoobyTrap_discard_counts_as_Explosives_for_Requires()
        {
            var (game, mal, _) = TwoShips();
            mal.ShipUpgrades.Add(BoobyTrapAction.CardId);
            Assert.True(MisbehaveResolver.HasTag(game, mal, "Explosives"));
            Assert.True(BoobyTrapAction.TryDiscardAsExplosives(game, mal, out var error), error);
            Assert.DoesNotContain(BoobyTrapAction.CardId, mal.ShipUpgrades);
            Assert.False(MisbehaveResolver.HasTag(game, mal, "Explosives"));
        }

        [Fact]
        public void MagGrappler_adds_plus_three_Tech_on_boarding()
        {
            var (game, mal, zoe) = TwoShips();
            mal.ShipUpgrades.Add(MagGrapplerAction.CardId);
            Assert.Equal(3, BoardingTest.TechDiceModifier(mal, zoe, Skill.Tech));
            Assert.Equal(0, BoardingTest.TechDiceModifier(mal, zoe, Skill.Talk));

            zoe.ShipUpgrades.Add(BoobyTrapAction.CardId);
            Assert.Equal(1, BoardingTest.TechDiceModifier(mal, zoe, Skill.Tech));
        }

        [Fact]
        public void MagGrappler_after_Salvage_Tech8_takes_Ship_Upgrade_not_Drive_Core()
        {
            var (game, mal, _) = TwoShips();
            mal.ShipUpgrades.Add(MagGrapplerAction.CardId);
            mal.TechBonus = 8;
            var upgrade = new SupplyCard(
                "ship-upgrade_cry-baby",
                "Cry Baby",
                400,
                SupplyKind.ShipUpgrade,
                new Dictionary<string, int> { ["Persephone"] = 1 });
            var drive = new SupplyCard(
                "drive-core_mark-i",
                "Mark I",
                0,
                SupplyKind.DriveCore,
                new Dictionary<string, int> { ["Persephone"] = 1 });
            var market = new SupplyMarket("Persephone");
            market.Discard.Add(upgrade);
            market.Discard.Add(drive);
            game.SupplyDecks = new SupplyDecks(new[] { market });

            Assert.True(
                MagGrapplerAction.TryAfterSalvage(
                    game, mal, ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 6, 6),
                    out var roll, out var taken, out var error,
                    takeUpgradeId: upgrade.Id),
                error);
            Assert.True(roll!.Success);
            Assert.Equal(upgrade.Id, taken);
            Assert.Contains(upgrade.Id, mal.ShipUpgrades);
            Assert.DoesNotContain(upgrade, market.Discard);
            Assert.Contains(drive, market.Discard);

            Assert.False(
                MagGrapplerAction.TryTakeShipUpgradeFromDiscard(
                    game, mal, drive.Id, out _, out var driveErr));
            Assert.Contains("Ship Upgrade", driveErr);
        }

        [Fact]
        public void MagGrappler_Salvage_fail_band_takes_nothing()
        {
            var (game, mal, _) = TwoShips();
            mal.ShipUpgrades.Add(MagGrapplerAction.CardId);
            mal.TechBonus = 1;
            var upgrade = new SupplyCard(
                "ship-upgrade_cry-baby",
                "Cry Baby",
                400,
                SupplyKind.ShipUpgrade);
            var market = new SupplyMarket("Persephone");
            market.Discard.Add(upgrade);
            game.SupplyDecks = new SupplyDecks(new[] { market });

            Assert.True(
                MagGrapplerAction.TryAfterSalvage(
                    game, mal, ScriptedRng.FromDieFaces(1),
                    out var roll, out var taken, out var error),
                error);
            Assert.False(roll!.Success);
            Assert.Null(taken);
            Assert.Contains(upgrade, market.Discard);
            Assert.DoesNotContain(upgrade.Id, mal.ShipUpgrades);
        }

        [Fact]
        public void FuelCatalyzer_extra_fuel_adds_plus_two_range_this_turn()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 3) { DriveRange = 5 };
            player.ShipUpgrades.Add(FuelCatalyzerAction.CardId);
            var game = new GameState(map, new[] { player });
            var fly = new FlyAction(new MovementEngine(map));

            Assert.True(fly.TryFullBurnTo(
                game, "p1", Pelorum, out var result, out var error, useFuelCatalyzer: true), error);
            Assert.Equal(Pelorum, player.SectorId);
            // Initiate 1 + Catalyzer 1 = 2 fuel spent for 1-hop burn.
            Assert.Equal(1, player.Fuel);
            Assert.Equal(2, game.CatalyzerRangeBonusThisTurn);
            Assert.Equal(7, FuelCatalyzerAction.DriveRangeForInitiate(game, player, useCatalyzer: false));
            Assert.NotNull(result);
        }

        [Fact]
        public void FuelCatalyzer_rejects_without_extra_fuel()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 1) { DriveRange = 5 };
            player.ShipUpgrades.Add(FuelCatalyzerAction.CardId);
            var game = new GameState(map, new[] { player });
            var fly = new FlyAction(new MovementEngine(map));

            Assert.False(fly.TryFullBurnTo(
                game, "p1", Pelorum, out _, out var error, useFuelCatalyzer: true));
            Assert.Contains("Fuel Catalyzer", error);
            Assert.Equal(1, player.Fuel);
            Assert.Equal(0, game.CatalyzerRangeBonusThisTurn);
        }

        [Fact]
        public void RamJets_FullBurn_discards_and_allows_alongside_Mosey()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 4) { DriveRange = 5 };
            player.ShipUpgrades.Add(RamJetsAction.CardId);
            var market = new SupplyMarket("Persephone");
            var game = new GameState(map, new[] { player })
            {
                SupplyDecks = new SupplyDecks(new[] { market }),
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault()
            };
            var movement = new MovementEngine(map);
            var fly = new FlyAction(movement);

            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out var moseyErr), moseyErr);
            Assert.True(game.ActionWasUsed(TurnAction.Fly));
            Assert.Equal(1, game.ActionsUsedThisTurn);
            Assert.Equal(Pelorum, player.SectorId);

            // Adjacent Full Burn back toward a reachable sector within range.
            Assert.True(
                RamJetsAction.TryFullBurnTo(
                    game, "p1", Persephone, movement, out var burn, out var ramErr),
                ramErr);
            Assert.Equal(Persephone, player.SectorId);
            Assert.DoesNotContain(RamJetsAction.CardId, player.ShipUpgrades);
            Assert.Equal(2, game.ActionsUsedThisTurn);
            Assert.True(game.TurnComplete);
            Assert.Contains(market.Discard, c => c.Id == RamJetsAction.CardId);
            Assert.NotNull(burn);
        }

        [Fact]
        public void RamJets_then_Mosey_both_legal_same_turn()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, fuel: 4) { DriveRange = 5 };
            player.ShipUpgrades.Add(RamJetsAction.CardId);
            var game = new GameState(map, new[] { player })
            {
                SupplyDecks = new SupplyDecks(new[] { new SupplyMarket("Persephone") }),
                ShipUpgradeCatalog = ShipUpgradeIndex.LoadDefault()
            };
            var movement = new MovementEngine(map);
            var fly = new FlyAction(movement);

            Assert.True(
                RamJetsAction.TryFullBurnTo(game, "p1", Pelorum, movement, out _, out var ramErr),
                ramErr);
            Assert.False(game.ActionWasUsed(TurnAction.Fly));
            Assert.Equal(1, game.ActionsUsedThisTurn);
            // Nav from the Ram Jets Full Burn must be resolved before another action (normal).
            game.PendingNavDraws.Clear();
            Assert.True(fly.TryMosey(game, "p1", Persephone, out _, out var moseyErr), moseyErr);
            Assert.Equal(2, game.ActionsUsedThisTurn);
        }
    }
}
