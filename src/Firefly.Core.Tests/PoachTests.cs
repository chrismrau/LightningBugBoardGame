using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class PoachTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";

        private static (GameState Game, PlayerState Mal, PlayerState Zoe) TwoShips(
            string malSector = Persephone,
            string zoeSector = Persephone,
            int malCash = 3000)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", malSector, cash: malCash);
            var zoe = new PlayerState("p2", "Zoe", zoeSector, cash: 3000);
            var game = new GameState(map, new[] { mal, zoe });
            var crew = CrewCatalog.LoadDefault();
            Assert.True(zoe.Roster.TryHire(crew.Get("crew_kaylee"), out _));
            return (game, mal, zoe);
        }

        [Fact]
        public void Poach_pays_the_bank_and_clears_the_token()
        {
            var (game, mal, zoe) = TwoShips();
            var kaylee = zoe.Roster.Find("crew_kaylee")!;
            var cost = kaylee.Card.Cost;
            zoe.Roster.Disgruntle(kaylee);

            Assert.True(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out var result, out var error), error);
            Assert.Equal(cost, result!.CashSpent);
            Assert.Equal(3000 - cost, mal.Cash);
            Assert.Equal(3000, zoe.Cash);
            Assert.Null(zoe.Roster.Find("crew_kaylee"));
            Assert.NotNull(mal.Roster.Find("crew_kaylee"));
            Assert.False(mal.Roster.Find("crew_kaylee")!.Disgruntled);
            Assert.False(game.ActionTaken);
        }

        [Fact]
        public void Poach_requires_the_same_sector()
        {
            var (game, _, zoe) = TwoShips(malSector: Persephone, zoeSector: Santo);
            zoe.Roster.Disgruntle(zoe.Roster.Find("crew_kaylee")!);
            Assert.False(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out _, out var error));
            Assert.Contains("sector", error);
            Assert.NotNull(zoe.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void Cannot_poach_a_happy_crew()
        {
            var (game, _, _) = TwoShips();
            Assert.False(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out _, out var error));
            Assert.Contains("not Disgruntled", error);
        }

        [Fact]
        public void Cannot_poach_a_leader()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 5000);
            var zoe = new PlayerState("p2", "Zoe", Persephone, cash: 3000);
            var game = new GameState(map, new[] { mal, zoe });
            Assert.True(zoe.Roster.TryHire(LeaderCatalog.LoadDefault().Get("leader_malcolm"), out _));
            zoe.Roster.Disgruntle(zoe.Roster.Leader!);

            Assert.False(new PoachAction().TryPoach(game, "p1", "p2", "leader_malcolm", out _, out var error));
            Assert.Contains("Leader", error);
            Assert.NotNull(zoe.Roster.Leader);
        }

        [Fact]
        public void Cannot_poach_off_turn()
        {
            var (game, _, zoe) = TwoShips();
            zoe.Roster.Disgruntle(zoe.Roster.Find("crew_kaylee")!);
            game.EndTurn();
            Assert.False(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out _, out var error));
            Assert.Contains("turn", error);
        }

        [Fact]
        public void Poach_does_not_spend_an_action()
        {
            var (game, mal, zoe) = TwoShips();
            zoe.Roster.Disgruntle(zoe.Roster.Find("crew_kaylee")!);
            Assert.True(game.TryConsumeAction(TurnAction.Buy, out _));
            Assert.True(game.ActionWasUsed(TurnAction.Buy));
            Assert.True(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out _, out var error), error);
            Assert.NotNull(mal.Roster.Find("crew_kaylee"));
            Assert.Equal(1, game.ActionsUsedThisTurn);
        }

        [Fact]
        public void Poach_fails_when_the_roster_is_full()
        {
            var (game, mal, zoe) = TwoShips();
            mal.Roster.MaxCrew = 0;
            zoe.Roster.Disgruntle(zoe.Roster.Find("crew_kaylee")!);
            Assert.False(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out _, out var error));
            Assert.Contains("full", error);
            Assert.NotNull(zoe.Roster.Find("crew_kaylee"));
        }

        [Fact]
        public void Poach_fails_without_the_hiring_cost()
        {
            var (game, mal, zoe) = TwoShips(malCash: 0);
            var kaylee = zoe.Roster.Find("crew_kaylee")!;
            zoe.Roster.Disgruntle(kaylee);
            Assert.True(kaylee.Card.Cost > 0);
            Assert.False(new PoachAction().TryPoach(game, "p1", "p2", "crew_kaylee", out _, out var error));
            Assert.Contains("$", error);
            Assert.Equal(0, mal.Cash);
        }
    }
}
