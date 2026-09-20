using Firefly.Core.Abilities;
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
    /// Null <see cref="Fuel"/> at own Haven suspends <see cref="PendingChoiceKinds.HavenFuelAmount"/>.
    /// </summary>
    public sealed class HavenBuyRequest
    {
        /// <summary>
        /// Fuel to load. Null at own Haven → PendingChoice (0–4 free). Non-null skips suspend.
        /// </summary>
        public int? Fuel { get; set; }
        public bool ShoreLeave { get; set; }
    }

    /// <summary>
    /// Shore Leave counts as a Buy action. At a Planet, pay $100 per crew
    /// member (Leader included) and remove every Disgruntled token.
    /// Any Port Friends in Low Places: own Haven Shore Leave is free; Haven Buy
    /// may combine Fuel + Shore Leave.
    /// Board Game Collection: Shore Leave in any Sector (planet not required).
    /// </summary>
    public sealed class ShoreLeaveAction
    {
        public const int CostPerCrew = 100;
        public const int MaxFreeHavenFuel = 4;

        private string? _pendingHavenPlayerId;
        private HavenBuyRequest? _pendingHavenRequest;

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

            // Board Game Collection: Buy Action Shore Leave in any Sector (Supplies.tsv).
            var anySector = AbilityDispatcher.HasShoreLeaveAnySector(game, player);
            if (!anySector && !HasPlanet(sector))
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
            // Barkeep Good Times: Shore Leave at Supply Planets is free (Supplies.tsv).
            var free = (game.Scenario != null
                    && game.Scenario.FriendsInLowPlaces
                    && HavenRules.IsOwnHaven(player, player.SectorId))
                || (AbilityDispatcher.HasFreeShoreLeaveAtSupply(player) && sector.HasSupplyDeck);
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
        /// up to 4 free Fuel. Null Fuel at own Haven always suspends HavenFuelAmount.
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

            // ScenarioCards.json: “may Load up to 4 free Fuel” — always suspend when unset.
            if (ownHaven && request.Fuel == null)
            {
                var pending = new PendingChoice(
                    playerId,
                    PendingChoiceKinds.HavenFuelAmount,
                    options: new[] { "0", "1", "2", "3", "4" },
                    prompt: $"Load how many free Fuel (0–{MaxFreeHavenFuel})?");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                _pendingHavenPlayerId = playerId;
                _pendingHavenRequest = new HavenBuyRequest
                {
                    ShoreLeave = request.ShoreLeave,
                    Fuel = null
                };
                error = "Choose how many free Fuel to Load at your Haven.";
                return false;
            }

            var fuel = request.Fuel ?? 0;
            if (fuel < 0)
            {
                error = "Cannot buy a negative amount of Fuel.";
                return false;
            }
            if (!request.ShoreLeave && fuel == 0)
            {
                error = "Haven Buy must Shore Leave and/or Load Fuel.";
                return false;
            }
            if (ownHaven && fuel > MaxFreeHavenFuel)
            {
                error = $"At your own Haven you may Load up to {MaxFreeHavenFuel} free Fuel.";
                return false;
            }

            return FinishHavenBuy(game, player, sector, request.ShoreLeave, fuel, ownHaven, out result, out error);
        }

        public bool TryResumeHavenFuel(
            GameState game,
            ChoiceSubmission submission,
            out ShoreLeaveResult? result,
            out string? error)
        {
            result = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.HavenFuelAmount,
                    System.StringComparison.Ordinal))
            {
                error = "No Haven fuel choice is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_pendingHavenPlayerId) || _pendingHavenRequest == null)
            {
                error = "Haven fuel resume state is missing.";
                return false;
            }

            var amount = submission.Amount;
            if (amount == null
                && !string.IsNullOrWhiteSpace(submission.SelectedOptionId)
                && int.TryParse(submission.SelectedOptionId, out var parsed))
                amount = parsed;
            if (amount == null)
            {
                error = "Fuel amount is required (0–4).";
                return false;
            }
            if (amount < 0 || amount > MaxFreeHavenFuel)
            {
                error = $"Fuel amount must be 0–{MaxFreeHavenFuel}.";
                return false;
            }

            var playerId = _pendingHavenPlayerId!;
            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            var player = game.GetPlayer(playerId);
            if (!game.Map.TryGet(player.SectorId, out var sector))
            {
                error = $"Unknown sector '{player.SectorId}'.";
                return false;
            }
            var shoreLeave = _pendingHavenRequest.ShoreLeave;
            _pendingHavenPlayerId = null;
            _pendingHavenRequest = null;
            return FinishHavenBuy(
                game, player, sector, shoreLeave, amount.Value, ownHaven: true, out result, out error);
        }

        private static bool FinishHavenBuy(
            GameState game,
            PlayerState player,
            Sector sector,
            bool shoreLeave,
            int fuel,
            bool ownHaven,
            out ShoreLeaveResult? result,
            out string? error)
        {
            result = null;
            var planet = sector.Planet ?? sector.DisplayName;
            var buyBlock = ActiveAlertRules.BuyBlockReason(game, player, planet);
            if (buyBlock != null)
            {
                error = buyBlock;
                return false;
            }

            var shoreCost = 0;
            if (shoreLeave)
            {
                if (player.Roster.Count == 0)
                {
                    error = "Shore Leave requires crew on the ship.";
                    return false;
                }
                if (!ownHaven)
                    shoreCost = CostFor(player);
            }

            var fuelCost = ownHaven ? 0 : fuel * BuyAction.FuelPrice;
            var total = shoreCost + fuelCost;
            if (player.Cash < total)
            {
                error = $"Need ${total}, have ${player.Cash}.";
                return false;
            }
            if (fuel > 0 && !HoldSpace.TryExplain(player, out error, addFuel: fuel))
                return false;

            if (!game.TryConsumeAction(TurnAction.Buy, out error))
                return false;

            player.Cash -= total;
            player.Fuel += fuel;
            var cleared = 0;
            if (shoreLeave)
                cleared = player.Roster.ClearDisgruntled();

            result = new ShoreLeaveResult(
                player.SectorId,
                planet,
                total,
                cleared,
                fuel);
            return true;
        }

        public static bool HasPlanet(Sector sector) =>
            sector.IsPlanetary || !string.IsNullOrWhiteSpace(sector.Planet);
    }
}
