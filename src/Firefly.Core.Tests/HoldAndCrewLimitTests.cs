using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class HoldAndCrewLimitTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Harvest = "border-red-sun-r2-02";
        private const string Shipping = "job_amnon-duul_feeding-alliance-fat-cats";

        private static PlayerState Firefly(string shipName = "Serenity", int fuel = 0, int parts = 0)
        {
            var player = new PlayerState("p1", "Mal", Persephone, cash: 3000, fuel: fuel, parts: parts);
            var ships = ShipCatalog.LoadDefault();
            Assert.True(ships.TryResolve(shipName, out var ship));
            player.ApplyShip(ship);
            return player;
        }

        [Fact]
        public void Reference_sheet_core_fireflies_are_6_crew_8_cargo_4_stash()
        {
            var ships = ShipCatalog.LoadDefault();
            foreach (var name in new[] { "Serenity", "Bonanza", "Bonnie Mae", "Yun Qi" })
            {
                Assert.True(ships.TryResolve(name, out var ship), name);
                Assert.Equal(6, ship.MaxCrew);
                Assert.Equal(8, ship.CargoHolds);
                Assert.Equal(4, ship.Stash);
                Assert.Equal(0, ship.FuelStash);
            }
        }

        [Fact]
        public void Jetwash_and_Esmeralda_have_six_fuel_only_stash_spaces()
        {
            var ships = ShipCatalog.LoadDefault();
            Assert.True(ships.TryResolve("Jetwash", out var jet));
            Assert.True(ships.TryResolve("Esmeralda", out var esme));
            foreach (var ship in new[] { jet, esme })
            {
                Assert.Equal(5, ship.MaxCrew);
                Assert.Equal(12, ship.CargoHolds);
                Assert.Equal(0, ship.Stash);
                Assert.Equal(6, ship.FuelStash);
            }
        }

        [Fact]
        public void Fuel_and_parts_share_a_hold_two_per_space()
        {
            var player = Firefly("Interceptor");
            Assert.Equal(4, player.GeneralHolds);
            player.Fuel = 8;
            Assert.Equal(4, player.UsedHolds);
            Assert.False(HoldSpace.Fits(player, addFuel: 1));
            player.Fuel = 7;
            player.Parts = 1;
            Assert.Equal(4, player.UsedHolds);
            player.Fuel = 6;
            player.Parts = 2;
            Assert.Equal(4, player.UsedHolds);
            Assert.True(HoldSpace.Fits(player));
        }

        [Fact]
        public void One_cargo_token_fills_an_entire_hold()
        {
            var player = Firefly("Interceptor", fuel: 0, parts: 0);
            player.Cargo = 4;
            Assert.Equal(4, player.UsedHolds);
            Assert.False(HoldSpace.Fits(player, addCargo: 1));
            Assert.False(HoldSpace.Fits(player, addFuel: 1));
        }

        [Fact]
        public void Jetwash_fuel_stash_does_not_consume_cargo_holds()
        {
            var player = Firefly("Jetwash", fuel: 6, parts: 0);
            Assert.Equal(0, player.UsedHolds);
            Assert.Equal(12, player.FreeHolds);
            player.Cargo = 12;
            Assert.True(HoldSpace.Fits(player));
            Assert.False(HoldSpace.Fits(player, addFuel: 1));
        }

        [Fact]
        public void Seventh_Jetwash_fuel_needs_a_general_hold()
        {
            var player = Firefly("Jetwash", fuel: 6, parts: 0);
            player.Cargo = 12;
            Assert.False(HoldSpace.Fits(player, addFuel: 1));
            player.Cargo = 11;
            Assert.True(HoldSpace.Fits(player, addFuel: 1));
            player.Fuel = 7;
            player.Cargo = 11;
            Assert.Equal(12, player.UsedHolds);
        }

        [Fact]
        public void Buy_rejects_fuel_that_will_not_fit()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = Firefly("Interceptor", fuel: 6, parts: 2);
            player.Cash = 3000;
            var game = new GameState(map, new[] { player });
            var market = new SupplyMarket("Persephone", Array.Empty<SupplyCard>());
            game.SupplyDecks = new SupplyDecks(new[] { market });

            var buy = new BuyAction();
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var error));
            Assert.Contains("cargo/stash", error);
            Assert.Equal(6, player.Fuel);
            Assert.Equal(3000, player.Cash);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void Work_pickup_fails_when_the_holds_are_full()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = Firefly("Interceptor", fuel: 0, parts: 0);
            player.Cargo = 4;
            player.JobHand.Add(Shipping);
            var game = new GameState(map, new[] { player })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault()
            };
            player.SectorId = Harvest;

            var work = new WorkAction();
            Assert.False(work.TryWork(game, "p1", Shipping, out _, out var error));
            Assert.Contains("cargo/stash", error);
            Assert.Null(player.FindActive(Shipping));
            Assert.Contains(Shipping, player.JobHand);
        }

        [Fact]
        public void Leader_counts_against_the_ship_crew_limit()
        {
            var player = Firefly("Interceptor");
            var leaders = LeaderCatalog.LoadDefault();
            var crew = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(crew.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(crew.Get("crew_zoe"), out _));
            Assert.Equal(4, player.Roster.Count);
            Assert.False(player.Roster.TryHire(crew.Get("crew_inara"), out var error));
            Assert.Contains("full", error);
        }

        [Fact]
        public void Buy_will_not_hire_past_the_ship_crew_limit()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = Firefly("Interceptor");
            var leaders = LeaderCatalog.LoadDefault();
            var crew = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(leaders.Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(crew.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(crew.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(crew.Get("crew_zoe"), out _));

            var hire = new SupplyCard("crew_inara", "Inara", 300, SupplyKind.Crew,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Persephone"] = 1 });
            var market = new SupplyMarket("Persephone", Array.Empty<SupplyCard>());
            market.FaceUp.Add(hire);
            var game = new GameState(map, new[] { player })
            {
                Crew = crew,
                SupplyDecks = new SupplyDecks(new[] { market })
            };

            Assert.False(new BuyAction().TryBuy(game, "p1", new BuyRequest { SupplyCardIds = { hire.Id } }, out _, out var error));
            Assert.Contains("full", error);
            Assert.Equal(4, player.Roster.Count);
        }
    }
}
