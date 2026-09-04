using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class ShoreLeaveTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string EmptyWhiteSun = "alliance-white-sun-r2-01";

        private static (GameState Game, PlayerState Player, ShoreLeaveAction Action) NewGame(
            string sectorId = Persephone,
            int cash = 3000)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: cash, fuel: 3);
            var game = new GameState(map, new[] { player });
            return (game, player, new ShoreLeaveAction());
        }

        [Fact]
        public void Shore_leave_costs_100_per_crew_including_the_leader()
        {
            var (game, player, action) = NewGame();
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            player.Roster.Disgruntle(player.Roster.Find("crew_kaylee")!);
            player.Roster.Disgruntle(player.Roster.Leader!);

            Assert.True(action.TryShoreLeave(game, "p1", out var result, out var error), error);
            Assert.Equal(200, result!.CashSpent);
            Assert.Equal(2, result.TokensCleared);
            Assert.Equal(2800, player.Cash);
            Assert.Equal(0, player.Roster.DisgruntledCount);
            Assert.False(player.Roster.Leader!.Disgruntled);
            Assert.Equal(TurnAction.Buy, game.LastAction);
            Assert.True(game.ActionWasUsed(TurnAction.Buy));
        }

        [Fact]
        public void Shore_leave_charges_every_crew_even_if_only_one_is_disgruntled()
        {
            var (game, player, action) = NewGame();
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_jayne"), out _));
            player.Roster.Disgruntle(player.Roster.Find("crew_kaylee")!);

            Assert.True(action.TryShoreLeave(game, "p1", out var result, out var error), error);
            Assert.Equal(300, result!.CashSpent);
            Assert.Equal(1, result.TokensCleared);
            Assert.Equal(2700, player.Cash);
        }

        [Fact]
        public void Shore_leave_fails_in_empty_space()
        {
            var (game, _, action) = NewGame(sectorId: EmptyWhiteSun);
            Assert.False(action.TryShoreLeave(game, "p1", out _, out var error));
            Assert.Contains("Planet", error);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void Shore_leave_fails_without_enough_cash()
        {
            var (game, player, action) = NewGame(cash: 50);
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_kaylee"), out _));
            player.Roster.Disgruntle(player.Roster.Find("crew_kaylee")!);

            Assert.False(action.TryShoreLeave(game, "p1", out _, out var error));
            Assert.Contains("$100", error);
            Assert.Contains("1 crew", error);
            Assert.True(player.Roster.Find("crew_kaylee")!.Disgruntled);
            Assert.Equal(50, player.Cash);
        }

        [Fact]
        public void Shore_leave_cannot_be_taken_twice_in_one_turn()
        {
            var (game, player, action) = NewGame();
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            Assert.True(action.TryShoreLeave(game, "p1", out _, out _));
            Assert.False(action.TryShoreLeave(game, "p1", out _, out var error));
            Assert.Contains("already used this turn", error);
            Assert.Equal(2900, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Shore_leave_blocks_a_later_Buy_on_the_same_turn()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 3000, fuel: 0, parts: 0);
            Assert.True(player.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            var game = new GameState(map, new[] { player });
            var market = new SupplyMarket("Persephone", Array.Empty<SupplyCard>());
            game.SupplyDecks = new SupplyDecks(new[] { market });

            Assert.True(new ShoreLeaveAction().TryShoreLeave(game, "p1", out _, out _));
            Assert.False(new BuyAction().TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var error));
            Assert.Contains("already used this turn", error);
            Assert.Equal(2900, player.Cash);
            Assert.Equal(0, player.Fuel);
        }

        [Fact]
        public void Setup_game_can_take_shore_leave_at_persephone()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity", leaderId: "Malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(21) });
            game.CurrentPlayer.Roster.Disgruntle(game.CurrentPlayer.Roster.Leader!);

            var action = new ShoreLeaveAction();
            Assert.True(action.TryShoreLeave(game, "p1", out var result, out var error), error);
            Assert.Equal(1, result!.TokensCleared);
            Assert.False(game.CurrentPlayer.Roster.Leader!.Disgruntled);
            Assert.Equal(2900, game.CurrentPlayer.Cash);
        }
    }
}
