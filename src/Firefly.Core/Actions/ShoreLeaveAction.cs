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

        public ShoreLeaveResult(string sectorId, string? planet, int cashSpent, int tokensCleared)
        {
            SectorId = sectorId;
            Planet = planet;
            CashSpent = cashSpent;
            TokensCleared = tokensCleared;
        }
    }

    /// <summary>
    /// Shore Leave counts as a Buy action. At a Planet, pay $100 per crew
    /// member (Leader included) and remove every Disgruntled token.
    /// </summary>
    public sealed class ShoreLeaveAction
    {
        public const int CostPerCrew = 100;

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
            var cost = CostFor(player);
            if (player.Roster.Count == 0)
            {
                error = "Shore Leave requires crew on the ship.";
                return false;
            }
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

        public static bool HasPlanet(Sector sector) =>
            sector.IsPlanetary || !string.IsNullOrWhiteSpace(sector.Planet);
    }
}
