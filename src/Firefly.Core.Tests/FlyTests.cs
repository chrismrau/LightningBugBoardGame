using Firefly.Core.Actions;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class FlyTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";

        private static (GameState Game, FlyAction Fly, PlayerState Player) NewGame(
            int fuel = 3,
            MapTokens? tokens = null,
            bool requiresFuel = true)
        {
            var map = SectorMap.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data", "map"));
            var player = new PlayerState("p1", "Mal", Persephone, fuel: fuel, fullBurnRequiresFuel: requiresFuel);
            var game = new GameState(map, new[] { player }, tokens);
            var fly = new FlyAction(new MovementEngine(map));
            return (game, fly, player);
        }

        [Fact]
        public void Mosey_moves_adjacent_without_fuel_or_nav_and_consumes_action()
        {
            var (game, fly, player) = NewGame();
            Assert.True(fly.TryMosey(game, "p1", Pelorum, out var result, out var error));
            Assert.Null(error);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Equal(3, player.Fuel);
            Assert.Empty(game.PendingNavDraws);
            Assert.True(game.ActionTaken);
            Assert.Equal(TurnAction.Fly, game.LastAction);
            Assert.False(result!.StoppedForEncounter);
        }

        [Fact]
        public void Second_action_on_same_turn_is_rejected()
        {
            var (game, fly, _) = NewGame();
            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out _));
            Assert.False(fly.TryMosey(game, "p1", Persephone, out _, out var error));
            Assert.Contains("already taken an action", error);
        }

        [Fact]
        public void FullBurn_spends_one_fuel_and_queues_nav_draws()
        {
            var (game, fly, player) = NewGame();
            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out var result, out var error));
            Assert.Null(error);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Equal(2, player.Fuel);
            Assert.Single(game.PendingNavDraws);
            Assert.Equal(NavRegion.Alliance, game.PendingNavDraws[0].Region);
            Assert.Equal(1, result!.Plan.NavCardsToDraw);
        }

        [Fact]
        public void FullBurn_fails_without_fuel()
        {
            var (game, fly, player) = NewGame(fuel: 0);
            Assert.False(fly.TryFullBurnTo(game, "p1", Pelorum, out _, out var error));
            Assert.Contains("Not enough fuel", error);
            Assert.Equal(Persephone, player.SectorId);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void FullBurn_without_fuel_requirement_still_moves()
        {
            var (game, fly, player) = NewGame(fuel: 0, requiresFuel: false);
            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out _, out _));
            Assert.Equal(0, player.Fuel);
            Assert.Equal(Pelorum, player.SectorId);
        }

        [Fact]
        public void FullBurn_stops_on_cruiser_and_sets_pending_encounter()
        {
            var tokens = new MapTokens(allianceCruiserSectorId: Pelorum);
            var (game, fly, player) = NewGame(tokens: tokens);
            Assert.True(fly.TryFullBurnTo(game, "p1", Pelorum, out var result, out _));
            Assert.Equal(Pelorum, player.SectorId);
            Assert.True(result!.StoppedForEncounter);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Pelorum, game.PendingEncounterSectorId);
        }

        [Fact]
        public void EndTurn_clears_action_and_pending_events()
        {
            var (game, fly, _) = NewGame();
            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out _));
            game.EndTurn();
            Assert.False(game.ActionTaken);
            Assert.Equal(TurnAction.None, game.LastAction);
            Assert.Empty(game.PendingNavDraws);
            Assert.Null(game.PendingEncounter);
        }
    }
}
