using System.Collections.Generic;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class FlyResult
    {
        public MovementPlan Plan { get; }
        public bool StoppedForEncounter { get; }

        public FlyResult(MovementPlan plan, bool stoppedForEncounter)
        {
            Plan = plan;
            StoppedForEncounter = stoppedForEncounter;
        }
    }

    /// <summary>
    /// Official Fly action: Mosey or Full Burn. Consumes the player's one action for the turn.
    /// Full Burn spends 1 fuel (unless the drive does not require it) and queues a Nav draw
    /// for each sector actually entered. Movement stops on the first Cruiser or Cutter entered.
    /// </summary>
    public sealed class FlyAction
    {
        private readonly MovementEngine _movement;

        public FlyAction(MovementEngine movement)
        {
            _movement = movement;
        }

        public bool TryMosey(GameState game, string playerId, string toSectorId, out FlyResult? result, out string? error)
        {
            result = null;
            if (!CanAct(game, playerId, out var player, out error))
                return false;

            if (!_movement.TryMosey(player.SectorId, toSectorId, game.Tokens, out var plan, out error) || plan == null)
                return false;

            Apply(game, player, plan, truncateOnEncounter: true, out result);
            return true;
        }

        public bool TryFullBurn(
            GameState game,
            string playerId,
            IReadOnlyList<string> path,
            out FlyResult? result,
            out string? error)
        {
            result = null;
            if (!CanAct(game, playerId, out var player, out error))
                return false;

            if (player.FullBurnRequiresFuel && player.Fuel < 1)
            {
                error = "Not enough fuel for Full Burn.";
                return false;
            }

            if (!_movement.TryFullBurn(path, player.EffectiveDriveRange, game.Tokens, out var plan, out error) || plan == null)
                return false;

            if (plan.FromSectorId != player.SectorId)
            {
                error = "Full Burn path must start in the player's current sector.";
                return false;
            }

            Apply(game, player, plan, truncateOnEncounter: true, out result);
            return true;
        }

        public bool TryFullBurnTo(
            GameState game,
            string playerId,
            string toSectorId,
            out FlyResult? result,
            out string? error)
        {
            result = null;
            if (!CanAct(game, playerId, out var player, out error))
                return false;

            var path = _movement.Pathfinder.ShortestPath(player.SectorId, toSectorId);
            if (path == null)
            {
                error = $"No path from '{player.SectorId}' to '{toSectorId}'.";
                return false;
            }

            return TryFullBurn(game, playerId, path, out result, out error);
        }

        private static bool CanAct(GameState game, string playerId, out PlayerState player, out string? error)
        {
            player = game.GetPlayer(playerId);
            error = null;
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            return game.CanTakeAction(TurnAction.Fly, out error);
        }

        private static void Apply(
            GameState game,
            PlayerState player,
            MovementPlan plan,
            bool truncateOnEncounter,
            out FlyResult result)
        {
            game.ClearPendingEvents();

            var steps = new List<MovementStep>();
            var path = new List<string> { plan.FromSectorId };
            var stopped = false;

            foreach (var step in plan.EnteredSteps)
            {
                steps.Add(step);
                path.Add(step.SectorId);
                if (step.DrawsNavCard)
                    game.PendingNavDraws.Add(new PendingNavDraw(step.SectorId, step.NavRegion));

                if (truncateOnEncounter && step.Encounter.HasValue)
                {
                    game.PendingEncounter = step.Encounter;
                    game.PendingEncounterSectorId = step.SectorId;
                    stopped = true;
                    break;
                }
            }

            var applied = new MovementPlan(
                plan.Kind,
                plan.FromSectorId,
                path[path.Count - 1],
                path,
                steps,
                plan.FuelCost);

            if (applied.Kind == MovementKind.FullBurn &&
                applied.FuelCost > 0 &&
                player.FullBurnRequiresFuel)
            {
                player.Fuel -= applied.FuelCost;
            }

            player.SectorId = applied.ToSectorId;
            game.TryConsumeAction(TurnAction.Fly, out _);
            result = new FlyResult(applied, stopped);
        }
    }
}
