using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// GF9 BBG rulebook p.6 (PDF index 6): "On your turn, you may take 2 Actions…"
    /// — may, not must. Players may EndTurn with 0, 1, or 2 actions used.
    /// </summary>
    public sealed class EndTurnTests
    {
        private const string Persephone = "alliance-lux-r1-01";

        [Fact]
        public void EndTurn_allowed_with_zero_actions_used()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "ship_serenity", leaderId: "leader_malcolm") },
                new GameSetupOptions { ScenarioCardId = "scenario_king-of-all-londinium" });

            Assert.Equal(0, game.ActionsUsedThisTurn);
            Assert.False(game.TurnComplete);

            game.EndTurn();

            Assert.Equal(0, game.ActionsUsedThisTurn);
            Assert.Equal(TurnAction.None, game.LastAction);
            Assert.Same(game.Players[0], game.CurrentPlayer); // solo wraps to same seat
        }

        [Fact]
        public void EndTurn_allowed_after_one_action()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "ship_serenity", leaderId: "leader_malcolm") },
                new GameSetupOptions { ScenarioCardId = "scenario_king-of-all-londinium" });

            Assert.True(game.TryConsumeAction(TurnAction.Buy, out var error), error);
            Assert.Equal(1, game.ActionsUsedThisTurn);
            Assert.False(game.TurnComplete);

            game.EndTurn();
            Assert.Equal(0, game.ActionsUsedThisTurn);
            Assert.Equal(TurnAction.None, game.LastAction);
        }

        [Fact]
        public void EndTurn_allowed_after_two_actions_TurnComplete()
        {
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone, shipId: "ship_serenity", leaderId: "leader_malcolm"),
                    new PlayerSeat("p2", "Zoe", "alliance-qin-shi-huang-r1-01", shipId: "ship_bonanza", leaderId: "leader_zoe_jetwash")
                },
                new GameSetupOptions { ScenarioCardId = "scenario_king-of-all-londinium" });

            Assert.True(game.TryConsumeAction(TurnAction.Buy, out var err), err);
            Assert.True(game.TryConsumeAction(TurnAction.Deal, out err), err);
            Assert.True(game.TurnComplete);
            Assert.Equal(0, game.CurrentPlayerIndex);

            game.EndTurn();

            Assert.Equal(1, game.CurrentPlayerIndex);
            Assert.Equal(0, game.ActionsUsedThisTurn);
        }
    }
}
