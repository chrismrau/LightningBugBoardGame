using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class BuyTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";

        private static SupplyCard Gear(string id = "gear_test-pistol", int cost = 200) =>
            new SupplyCard(id, "Test Pistol", cost, SupplyKind.Gear, Planet("Persephone"));

        private static SupplyCard Crew(string id, int cost = 200) =>
            new SupplyCard(id, id, cost, SupplyKind.Crew, Planet("Persephone"));

        private static SupplyCard Upgrade(string id = "upgrade_crybaby", int cost = 400) =>
            new SupplyCard(id, "Cry Baby", cost, SupplyKind.ShipUpgrade, Planet("Persephone"));

        private static IReadOnlyDictionary<string, int> Planet(string name) =>
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [name] = 1 };

        private static GameState NewGame(
            string sectorId = Persephone,
            int cash = 2000,
            IEnumerable<SupplyCard>? marketCards = null)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: cash, fuel: 3, parts: 0);
            var game = new GameState(map, new[] { player });
            game.Crew = CrewCatalog.LoadDefault();
            var cards = new List<SupplyCard>(marketCards ?? new[] { Gear(), Upgrade() });
            game.Supply = new SupplyCatalog(cards);
            var market = new SupplyMarket("Persephone", Array.Empty<SupplyCard>());
            foreach (var card in cards)
                market.FaceUp.Add(card);
            game.SupplyDecks = new SupplyDecks(new[] { market });
            return game;
        }

        [Fact]
        public void Default_supply_catalog_includes_gear_and_crew()
        {
            var catalog = SupplyCatalog.LoadDefault();
            Assert.True(catalog.Cards.Count > 50);
            Assert.Contains(catalog.Cards.Values, c => c.Kind == SupplyKind.Gear && c.CopiesAt("Persephone") > 0);
            Assert.Contains(catalog.Cards.Values, c => c.Kind == SupplyKind.Crew && c.CopiesAt("Silverhold") > 0);
            Assert.Contains(catalog.Cards.Values, c => c.Kind == SupplyKind.ShipUpgrade);
        }

        [Fact]
        public void Persephone_market_deals_three_face_up_cards()
        {
            var decks = SupplyDecks.FromCatalog(SupplyCatalog.LoadDefault(), new SystemRng(3));
            Assert.True(decks.TryGet("Persephone", out var market));
            Assert.Equal(3, market.FaceUp.Count);
        }

        [Fact]
        public void Cannot_buy_away_from_a_supply_planet()
        {
            var game = NewGame(Santo);
            var buy = new BuyAction();
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var error));
            Assert.Contains("Supply planet", error);
        }

        [Fact]
        public void Fuel_and_parts_use_standard_prices()
        {
            var game = NewGame(cash: 1000);
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 2, Parts = 1 }, out var result, out var error), error);
            Assert.Equal(500, result!.CashSpent);
            Assert.Equal(5, game.CurrentPlayer.Fuel);
            Assert.Equal(1, game.CurrentPlayer.Parts);
            Assert.Equal(500, game.CurrentPlayer.Cash);
            Assert.True(game.ActionWasUsed(TurnAction.Buy));
        }

        [Fact]
        public void Can_buy_gear_from_the_face_up_market()
        {
            var gear = Gear("gear_pistol", 200);
            var game = NewGame(cash: 200, marketCards: new[] { gear });
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { SupplyCardIds = { gear.Id } }, out var result, out var error), error);
            Assert.Single(result!.CardsBought);
            Assert.Equal(SupplyKind.Gear, result.CardsBought[0].Kind);
            Assert.Contains(gear.Id, game.CurrentPlayer.Gear);
            Assert.Equal(0, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Can_buy_crew_and_ship_upgrades_in_the_same_action()
        {
            var kaylee = Crew("crew_kaylee", 200);
            var crybaby = Upgrade("upgrade_crybaby", 400);
            var game = NewGame(cash: 700, marketCards: new[] { kaylee, crybaby, Gear() });
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest
            {
                Fuel = 1,
                SupplyCardIds = { kaylee.Id, crybaby.Id }
            }, out var result, out var error), error);
            Assert.Equal(700, result!.CashSpent);
            Assert.Equal(1, game.CurrentPlayer.Roster.Count);
            Assert.True(game.CurrentPlayer.Roster.HasName("Kaylee"));
            Assert.Contains(crybaby.Id, game.CurrentPlayer.ShipUpgrades);
        }

        [Fact]
        public void Rejects_a_card_that_is_not_on_the_table()
        {
            var game = NewGame(marketCards: new[] { Gear() });
            var buy = new BuyAction();
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { SupplyCardIds = { "gear_missing" } }, out _, out var error));
            Assert.Contains("not for sale", error);
        }

        [Fact]
        public void Rejects_when_the_player_cannot_afford_the_purchase()
        {
            var game = NewGame(cash: 150);
            var buy = new BuyAction();
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1, Parts = 1 }, out _, out var error));
            Assert.Contains("Need $400", error);
            Assert.Equal(3, game.CurrentPlayer.Fuel);
        }

        [Fact]
        public void Bought_cards_are_replaced_from_the_planet_deck()
        {
            var first = Gear("gear_one", 100);
            var next = Gear("gear_two", 100);
            var game = NewGame(cash: 100, marketCards: new[] { first });
            game.SupplyDecks!.TryGet("Persephone", out var market);
            market.Deck.Add(next);

            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { SupplyCardIds = { first.Id } }, out _, out var error), error);
            Assert.DoesNotContain(market.FaceUp, c => c.Id == first.Id);
            Assert.Contains(market.FaceUp, c => c.Id == next.Id);
        }

        [Fact]
        public void Cannot_buy_twice_in_the_same_turn()
        {
            var game = NewGame(cash: 300);
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out _));
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var error));
            Assert.Contains("already used", error);
        }
    }
}
