using System.Collections.Generic;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class PendingChoiceTests
    {
        private const string Persephone = "alliance-lux-r1-01";

        private static GameState NewGame()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone, cash: 0, fuel: 3);
            return new GameState(map, new[] { player });
        }

        [Fact]
        public void TrySetPendingChoice_sets_single_pending()
        {
            var game = NewGame();
            var pending = new PendingChoice(
                "p1",
                PendingChoiceKinds.NavPayOrDecline,
                contextId: "nav_example",
                options: new[] { "pay", "decline" },
                prompt: "Pay or Full Stop?");

            Assert.True(game.TrySetPendingChoice(pending, out var error));
            Assert.Null(error);
            Assert.Same(pending, game.PendingChoice);
            Assert.True(game.HasPendingEvents);
        }

        [Fact]
        public void TrySetPendingChoice_rejects_when_one_already_pending()
        {
            var game = NewGame();
            Assert.True(game.TrySetPendingChoice(
                new PendingChoice("p1", PendingChoiceKinds.KillVictim), out _));

            Assert.False(game.TrySetPendingChoice(
                new PendingChoice("p1", PendingChoiceKinds.BribeAmount), out var error));
            Assert.Contains("already pending", error);
            Assert.Equal(PendingChoiceKinds.KillVictim, game.PendingChoice!.Kind);
        }

        [Fact]
        public void TrySubmitChoice_clears_and_returns_resolved()
        {
            var game = NewGame();
            var pending = new PendingChoice(
                "p1",
                PendingChoiceKinds.MisbehaveOption,
                options: new[] { "sneak", "talk" });
            Assert.True(game.TrySetPendingChoice(pending, out _));

            var submission = new ChoiceSubmission { SelectedOptionId = "talk" };
            Assert.True(game.TrySubmitChoice("p1", submission, out var resolved, out var error));
            Assert.Null(error);
            Assert.Same(pending, resolved);
            Assert.Null(game.PendingChoice);
            Assert.False(game.HasPendingEvents);
        }

        [Fact]
        public void TrySubmitChoice_rejects_when_none_pending()
        {
            var game = NewGame();
            Assert.False(game.TrySubmitChoice(
                "p1",
                new ChoiceSubmission { Accepted = true },
                out var resolved,
                out var error));
            Assert.Null(resolved);
            Assert.Equal("No choice is pending.", error);
        }

        [Fact]
        public void TrySubmitChoice_rejects_wrong_player_and_illegal_option()
        {
            var game = NewGame();
            Assert.True(game.TrySetPendingChoice(
                new PendingChoice("p1", "test", options: new[] { "a", "b" }), out _));

            Assert.False(game.TrySubmitChoice(
                "p2",
                new ChoiceSubmission { SelectedOptionId = "a" },
                out _,
                out var wrongPlayer));
            Assert.Contains("owns the pending choice", wrongPlayer);
            Assert.NotNull(game.PendingChoice);

            Assert.False(game.TrySubmitChoice(
                "p1",
                new ChoiceSubmission { SelectedOptionId = "c" },
                out _,
                out var illegal));
            Assert.Contains("not legal", illegal);
            Assert.NotNull(game.PendingChoice);
        }

        [Fact]
        public void ClearPendingChoice_and_ClearPendingEvents_clear()
        {
            var game = NewGame();
            Assert.True(game.TrySetPendingChoice(
                new PendingChoice("p1", PendingChoiceKinds.HavenOrRivalSector), out _));

            game.ClearPendingChoice();
            Assert.Null(game.PendingChoice);

            Assert.True(game.TrySetPendingChoice(
                new PendingChoice("p1", PendingChoiceKinds.HavenOrRivalSector), out _));
            game.ClearPendingEvents();
            Assert.Null(game.PendingChoice);
            Assert.False(game.HasPendingEvents);
        }

        [Fact]
        public void PendingChoice_blocks_taking_another_action()
        {
            var game = NewGame();
            Assert.True(game.TrySetPendingChoice(
                new PendingChoice("p1", PendingChoiceKinds.NavPayOrDecline), out _));
            Assert.False(game.CanTakeAction(TurnAction.Fly, out var error));
            Assert.Contains("choices", error);
        }
    }
}
