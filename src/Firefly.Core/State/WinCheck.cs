using System;
using Firefly.Core.Cards;
using Firefly.Core.Map;

namespace Firefly.Core.State
{
    public enum WinPhase
    {
        Immediate,
        StartOfTurn,
        EndOfTurn
    }

    public sealed class WinResult
    {
        public string PlayerId { get; }
        public string PlayerName { get; }
        public string Reason { get; }

        public WinResult(string playerId, string playerName, string reason)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Reason = reason;
        }
    }

    public static class WinCheck
    {
        public static WinResult? Refresh(GameState game, WinPhase phase = WinPhase.Immediate)
        {
            if (game.GameOver)
                return Current(game);
            if (game.Scenario != null)
            {
                foreach (var player in game.Players)
                    ClaimAutoGoals(game, player);
            }
            var result = Evaluate(game, phase);
            if (result != null)
            {
                game.WinnerId = result.PlayerId;
                game.WinReason = result.Reason;
            }
            return result;
        }

        public static WinResult? Current(GameState game)
        {
            if (string.IsNullOrEmpty(game.WinnerId))
                return null;
            var player = game.GetPlayer(game.WinnerId);
            return new WinResult(player.Id, player.Name, game.WinReason ?? "Won.");
        }

        public static bool TryCompleteGoal(GameState game, PlayerState player, int number, out string? error)
        {
            error = null;
            var scenario = game.Scenario;
            if (scenario == null) { error = "No Story Card is in play."; return false; }
            var goal = scenario.Goal(number);
            if (goal == null) { error = $"Story Card has no Goal {number}."; return false; }
            if (player.CompletedGoals.Contains(number)) { error = $"Goal {number} is already complete."; return false; }
            player.CompletedGoals.Add(number);
            if (goal.GrantsGoalToken)
                player.GoalTokens++;
            Refresh(game);
            return true;
        }

        public static WinResult? Evaluate(GameState game, WinPhase phase)
        {
            var scenario = game.Scenario;
            if (scenario == null || string.IsNullOrWhiteSpace(scenario.WinType))
                return null;
            switch (scenario.WinType)
            {
                case "firstToCompleteGoal":
                    return FirstWhere(game, p => p.CompletedGoals.Contains(scenario.WinGoal), $"completed Goal {scenario.WinGoal}");
                case "firstAtHavenWithCash":
                    return FirstWhere(game, p => p.Cash >= scenario.WinCash && AtHaven(p), $"reached their Haven with ${scenario.WinCash}");
                case "firstSolidWithDistinctContacts":
                    return FirstWhere(game, p => p.SolidCount >= scenario.WinCount, $"Solid with {scenario.WinCount} Contacts");
                case "endTurnWithCash":
                    if (phase != WinPhase.EndOfTurn) return null;
                    return FirstWhere(game, p => p.Cash >= scenario.WinCash, $"ended a turn with ${scenario.WinCash}");
                case "firstWithGoalTokens":
                    return FirstWhere(game, p => p.GoalTokens >= scenario.WinGoalTokens, $"collected {scenario.WinGoalTokens} Goal tokens");
                case "firstWithGoalTokensAtStartOfTurn":
                    if (phase != WinPhase.StartOfTurn) return null;
                    var current = game.CurrentPlayer;
                    if (current.GoalTokens >= scenario.WinGoalTokens)
                        return new WinResult(current.Id, current.Name, $"started a turn with {scenario.WinGoalTokens} Goal tokens");
                    return null;
                case "mostCreditsAfterSettlingJobs":
                case "mostCreditsAfterLastCall":
                case "mostCreditsAfterLastCallAndSettlingJobs":
                case "lastContactBonusTriggersLastCallThenMostCredits":
                case "mostCreditsWhenBankEmptyOrHalfAllMoney":
                    if (phase != WinPhase.EndOfTurn) return null;
                    return Richest(game, "most credits");
                case "mostSolidThenHighestCrewValue":
                    if (phase != WinPhase.EndOfTurn) return null;
                    return MostSolidThenCrewValue(game);
                default:
                    return null;
            }
        }

        public static void ClaimAutoGoals(GameState game, PlayerState player)
        {
            var scenario = game.Scenario;
            if (scenario == null) return;
            foreach (var goal in scenario.Goals)
            {
                if (player.CompletedGoals.Contains(goal.Number)) continue;
                if (!AutoMet(game, player, goal)) continue;
                player.CompletedGoals.Add(goal.Number);
                if (goal.GrantsGoalToken) player.GoalTokens++;
            }
        }

        public static bool TryFinishTravelPay(GameState game, string playerId, out string? error)
        {
            var player = game.GetPlayer(playerId);
            error = null;
            if (game.Scenario == null) { error = "No Story Card is in play."; return false; }
            ScenarioGoal? goal = null;
            foreach (var item in game.Scenario.Goals)
            {
                if (item.Type == "travelPayAndWin" || item.Type == "arriveWithGoalTokens" || item.Type == "dealAndPay")
                    goal = item;
            }
            if (goal == null) { error = "This Story Card has no travel/pay Goal."; return false; }
            if (!AtLocation(game.Map, player.SectorId, goal.Location)) { error = $"Must be at {goal.Location}."; return false; }
            if (player.GoalTokens < goal.GoalTokens) { error = $"Need {goal.GoalTokens} Goal token(s)."; return false; }
            if (player.Cash < goal.Pay) { error = $"Need ${goal.Pay}."; return false; }
            player.Cash -= goal.Pay;
            return TryCompleteGoal(game, player, goal.Number, out error);
        }

        private static bool AutoMet(GameState game, PlayerState player, ScenarioGoal goal)
        {
            switch (goal.Type)
            {
                case "solidWithDistinctContacts": return player.SolidCount >= Math.Max(1, goal.Count);
                case "cashAndGoalToken": return player.Cash >= goal.Cash && player.GoalTokens >= Math.Max(1, goal.GoalTokens);
                case "cashGrantsGoalToken": return player.Cash >= goal.Cash;
                case "solidWithContacts": return IsSolidWithAll(game, player, goal);
                default: return false;
            }
        }

        private static bool IsSolidWithAll(GameState game, PlayerState player, ScenarioGoal goal)
        {
            foreach (var name in goal.Contacts)
            {
                if (player.IsSolidWith(name)) continue;
                if (game.Contacts != null && game.Contacts.TryFindByName(name, out var contact) && player.IsSolidWith(contact.Id)) continue;
                return false;
            }
            return goal.Contacts.Count > 0;
        }

        private static bool AtHaven(PlayerState player) =>
            !string.IsNullOrWhiteSpace(player.HavenSectorId) && string.Equals(player.SectorId, player.HavenSectorId, StringComparison.Ordinal);

        private static bool AtLocation(SectorMap map, string sectorId, string? location) =>
            string.IsNullOrWhiteSpace(location) || map.SatisfiesDestination(sectorId, location);

        private static WinResult? FirstWhere(GameState game, Func<PlayerState, bool> pred, string reason)
        {
            foreach (var player in game.Players)
            {
                if (pred(player)) return new WinResult(player.Id, player.Name, reason);
            }
            return null;
        }

        private static WinResult? Richest(GameState game, string reason)
        {
            PlayerState? best = null;
            foreach (var player in game.Players)
            {
                if (best == null || player.Cash > best.Cash) best = player;
            }
            return best == null ? null : new WinResult(best.Id, best.Name, reason);
        }

        private static WinResult? MostSolidThenCrewValue(GameState game)
        {
            PlayerState? best = null;
            foreach (var player in game.Players)
            {
                if (best == null) { best = player; continue; }
                if (player.SolidCount > best.SolidCount) best = player;
                else if (player.SolidCount == best.SolidCount && CrewValue(player) > CrewValue(best)) best = player;
            }
            return best == null ? null : new WinResult(best.Id, best.Name, "most Solid Contacts");
        }

        private static int CrewValue(PlayerState player)
        {
            var n = 0;
            foreach (var member in player.Roster.Members)
                n += Math.Max(0, member.Card.Cost);
            return n;
        }
    }
}
