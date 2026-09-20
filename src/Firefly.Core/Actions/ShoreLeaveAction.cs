using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class ShoreLeaveResult
    {
        public string SectorId { get; }
        public string? Planet { get; }
        public int CashSpent { get; }
        public int TokensCleared { get; }
        public int FuelLoaded { get; }

        public ShoreLeaveResult(
            string sectorId,
            string? planet,
            int cashSpent,
            int tokensCleared,
            int fuelLoaded = 0)
        {
            SectorId = sectorId;
            Planet = planet;
            CashSpent = cashSpent;
            TokensCleared = tokensCleared;
            FuelLoaded = fuelLoaded;
        }
    }

    /// <summary>
    /// Request for Friends in Low Places (Any Port): one Buy Action may Buy Fuel and Shore Leave.
    /// </summary>
    public sealed class HavenBuyRequest
    {
        public int Fuel { get; set; }
        public bool ShoreLeave { get; set; }
    }

    /// <summary>
    /// Shore Leave counts as a Buy action. At a Planet, pay $100 per crew
    /// member (Leader included) and remove every Disgruntled token.
    /// Any Port Friends in Low Places: own Haven Shore Leave is free; Haven Buy
    /// may combine Fuel + Shore Leave.
    /// </summary>
    public sealed class ShoreLeaveAction
    {
        public const int CostPerCrew = 100;
        public const int MaxFreeHavenFuel = 4;

        public static int CostFor(PlayerState player) =>
            player == null ? 0 : CostPerCrew * player.Roster.Count;

        public bool TryShoreLeave(
            GameState game,
            string playerId,
            out ShoreLeaveResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (!game.CanTakeAction(TurnAction.Buy, out error))
                return false;

            if (!game.Map.TryGet(player.SectorId, out var sector))
            {
                error = $"Unknown sector '{player.SectorId}'.";
                return false;
            }
            if (!HasPlanet(sector))
            {
                error = "Shore Leave requires a Planet in this sector.";
                return false;
            }
            var planet = sector.Planet ?? sector.DisplayName;
            var buyBlock = ActiveAlertRules.BuyBlockReason(game, player, planet);
            if (buyBlock != null)
            {
                error = buyBlock;
                return false;
            }
            if (player.Roster.Count == 0)
            {
                error = "Shore Leave requires crew on the ship.";
                return false;
            }

            // ScenarioCards.json Friends in Low Places: at your own Haven, Shore Leave is free.
            var free = game.Scenario != null
                && game.Scenario.FriendsInLowPlaces
                && HavenRules.IsOwnHaven(player, player.SectorId);
            var cost = free ? 0 : CostFor(player);
            if (player.Cash < cost)
            {
                error = $"Need ${cost} for Shore Leave ({player.Roster.Count} crew).";
                return false;
            }

            if (!game.TryConsumeAction(TurnAction.Buy, out error))
                return false;

            player.Cash -= cost;
            var cleared = player.Roster.ClearDisgruntled();
            result = new ShoreLeaveResult(player.SectorId, sector.Planet ?? sector.DisplayName, cost, cleared);
            return true;
        }

        /// <summary>
        /// Any Port Friends in Low Places: at any Haven, one Buy Action may both Buy Fuel
        /// and give Shore Leave. At your own Haven, Shore Leave is free and you may Load
        /// up to 4 free Fuel.
        /// </summary>
        public bool TryHavenFuelAndShoreLeave(
            GameState game,
            string playerId,
            HavenBuyRequest request,
            out ShoreLeaveResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (game.Scenario == null || !game.Scenario.FriendsInLowPlaces)
            {
                error = "Friends in Low Places is not active.";
                return false;
            }
            if (!game.CanTakeAction(TurnAction.Buy, out error))
                return false;
            if (request == null)
            {
                error = "A Haven buy request is required.";
                return false;
            }
            if (request.Fuel < 0)
            {
                error = "Cannot buy a negative amount of Fuel.";
                return false;
            }
            if (!request.ShoreLeave && request.Fuel == 0)
            {
                error = "Haven Buy must Shore Leave and/or Load Fuel.";
                return false;
            }
            if (!HavenRules.IsAnyHaven(game, player.SectorId))
            {
                error = "Friends in Low Places requires a Haven Sector.";
                return false;
            }
            if (!game.Map.TryGet(player.SectorId, out var sector))
            {
                error = $"Unknown sector '{player.SectorId}'.";
                return false;
            }

            var ownHaven = HavenRules.IsOwnHaven(player, player.SectorId);
            if (ownHaven && request.Fuel > MaxFreeHavenFuel)
            {
                error = $"At your own Haven you may Load up to {MaxFreeHavenFuel} free Fuel.";
                return false;
            }

            var planet = sector.Planet ?? sector.DisplayName;
            var buyBlock = ActiveAlertRules.BuyBlockReason(game, player, planet);
            if (buyBlock != null)
            {
                error = buyBlock;
                return false;
            }

            var shoreCost = 0;
            if (request.ShoreLeave)
            {
                if (player.Roster.Count == 0)
                {
                    error = "Shore Leave requires crew on the ship.";
                    return false;
                }
                if (!ownHaven)
                    shoreCost = CostFor(player);
            }

            var fuelCost = ownHaven ? 0 : request.Fuel * BuyAction.FuelPrice;
            var total = shoreCost + fuelCost;
            if (player.Cash < total)
            {
                error = $"Need ${total}, have ${player.Cash}.";
                return false;
            }
            if (request.Fuel > 0 && !HoldSpace.TryExplain(player, out error, addFuel: request.Fuel))
                return false;

            if (!game.TryConsumeAction(TurnAction.Buy, out error))
                return false;

            player.Cash -= total;
            player.Fuel += request.Fuel;
            var cleared = 0;
            if (request.ShoreLeave)
                cleared = player.Roster.ClearDisgruntled();

            result = new ShoreLeaveResult(
                player.SectorId,
                planet,
                total,
                cleared,
                request.Fuel);
            return true;
        }

        public static bool HasPlanet(Sector sector) =>
            sector.IsPlanetary || !string.IsNullOrWhiteSpace(sector.Planet);
    }
}
