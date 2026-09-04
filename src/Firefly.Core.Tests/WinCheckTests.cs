using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class WinCheckTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Ezra = "border-georgia-r1-02";

        private static GameState Story(string scenarioId, string sectorId = Persephone, int cash = 3000)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: cash);
            var game = new GameState(map, new[] { player })
            {
                Scenario = ScenarioCatalog.LoadDefault().Get(scenarioId),
                Contacts = ContactCatalog.LoadDefault()
            };
            return game;
        }

        [Fact]
        public void First_time_captain_parses_three_goals_and_firstToCompleteGoal()
        {
            var card = ScenarioCatalog.LoadDefault().Get("scenario_first-time-in-the-captains-chair");
            Assert.Equal("firstToCompleteGoal", card.WinType);
            Assert.Equal(3, card.WinGoal);
            Assert.Equal(3, card.Goals.Count);
            Assert.Equal("solidWithDistinctContacts", card.Goal(1)!.Type);
            Assert.Equal("travelPayAndWin", card.Goal(3)!.Type);
        }

        [Fact]
        public void Two_solids_claim_makin_friends_and_a_goal_token()
        {
            var game = Story("scenario_first-time-in-the-captains-chair");
            var player = game.CurrentPlayer;
            player.BecomeSolid("contact_harken");
            player.BecomeSolid("contact_amnon-duul");
            WinCheck.Refresh(game);
            Assert.Contains(1, player.CompletedGoals);
            Assert.Equal(1, player.GoalTokens);
            Assert.False(game.GameOver);
        }

        [Fact]
        public void Six_thousand_and_a_token_claim_seein_daylight()
        {
            var game = Story("scenario_first-time-in-the-captains-chair", cash: 6000);
            var player = game.CurrentPlayer;
            player.BecomeSolid("a");
            player.BecomeSolid("b");
            WinCheck.Refresh(game);
            Assert.Contains(1, player.CompletedGoals);
            Assert.Contains(2, player.CompletedGoals);
            Assert.Equal(2, player.GoalTokens);
            Assert.False(game.GameOver);
        }

        [Fact]
        public void Paying_niska_at_ezra_wins_first_time_captain()
        {
            var game = Story("scenario_first-time-in-the-captains-chair", sectorId: Ezra, cash: 12000);
            var player = game.CurrentPlayer;
            player.BecomeSolid("a");
            player.BecomeSolid("b");
            WinCheck.Refresh(game);
            Assert.Equal(2, player.GoalTokens);
            Assert.True(WinCheck.TryFinishTravelPay(game, "p1", out var error), error);
            Assert.True(game.GameOver);
            Assert.Equal("p1", game.WinnerId);
            Assert.Equal(6000, player.Cash);
            Assert.Contains(3, player.CompletedGoals);
        }

        [Fact]
        public void Any_port_wins_at_haven_with_twelve_thousand()
        {
            var game = Story("scenario_any-port-in-a-storm", cash: 12000);
            var result = WinCheck.Refresh(game);
            Assert.NotNull(result);
            Assert.Equal("p1", result!.PlayerId);
        }

        [Fact]
        public void Down_and_out_needs_five_solids()
        {
            var game = Story("scenario_down-and-out");
            var player = game.CurrentPlayer;
            player.BecomeSolid("1");
            player.BecomeSolid("2");
            player.BecomeSolid("3");
            player.BecomeSolid("4");
            Assert.Null(WinCheck.Refresh(game));
            player.BecomeSolid("5");
            Assert.NotNull(WinCheck.Refresh(game));
        }

        [Fact]
        public void Wanted_men_checks_cash_at_end_of_turn()
        {
            var game = Story("scenario_wanted-men", cash: 20000);
            Assert.Null(WinCheck.Refresh(game, WinPhase.Immediate));
            game.EndTurn();
            Assert.True(game.GameOver);
        }

        [Fact]
        public void Where_the_wind_takes_us_wins_on_three_tokens()
        {
            var game = Story("scenario_where-the-wind-takes-us");
            game.CurrentPlayer.GoalTokens = 3;
            Assert.NotNull(WinCheck.Refresh(game));
        }
    }
}
