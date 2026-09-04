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

    public sealed class ShoreLeaveAction
    {
        public const int Cost = 100;

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
            if (!game.CanTakeAction(TurnAction.ShoreLeave, out error))
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
            if (player.Cash < Cost)
            {
                error = $"Need ${Cost} for Shore Leave.";
                return false;
            }

            if (!game.TryConsumeAction(TurnAction.ShoreLeave, out error))
                return false;

            player.Cash -= Cost;
            var cleared = player.Roster.ClearDisgruntled();
            result = new ShoreLeaveResult(player.SectorId, sector.Planet ?? sector.DisplayName, Cost, cleared);
            return true;
        }

        public static bool HasPlanet(Sector sector) =>
            sector.IsPlanetary || !string.IsNullOrWhiteSpace(sector.Planet);
    }
}
