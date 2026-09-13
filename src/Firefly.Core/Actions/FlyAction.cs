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
                if (AlertTokenRules.SectorHasAlerts(game.Tokens, step.SectorId, game.UseAlertTokens))
                    game.PendingAlertSectors.Add(step.SectorId);

                // FAQ 4.1 p.14: Alliance Contact before Nav; if Contact Full Stops, do not draw Nav.
                // Legal ships ignore Cruiser/Corvette presence and still draw. Named Alliance Cruiser Nav is separate.
                var stopForContact = false;
                if (truncateOnEncounter && step.Encounter.HasValue)
                {
                    var encounter = step.Encounter.Value;
                    // Director's Cut: Cruiser / Corvette Contact only for Outlaw Ships.
                    // Reaver Cutter cannot be entered (MovementEngine rejects).
                    var allianceToken = encounter == TokenKind.AllianceCruiser
                        || encounter == TokenKind.OperativeCorvette;
                    if (!allianceToken || AlertTokenRules.IsOutlawShip(player))
                    {
                        game.PendingEncounter = encounter;
                        game.PendingEncounterSectorId = step.SectorId;
                        game.PendingEncounterPlayerId = player.Id;
                        // FAQ Cry Baby: entering Cruiser Sector deferred Nav until Contact or Cry Baby.
                        game.PendingEncounterDeferredNav =
                            encounter == TokenKind.AllianceCruiser;
                        stopForContact = true;
                        stopped = true;
                    }
                }

                if (step.DrawsNavCard && !stopForContact)
                    game.PendingNavDraws.Add(new PendingNavDraw(step.SectorId, step.NavRegion));

                if (stopped)
                    break;
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

        /// <summary>
        /// Spend Nav-granted Fly range bonus to enter more Full Burn sectors (no second initiate Fuel).
        /// Fuel Coupling Failure still discards 1 Fuel per sector entered when that flag is set.
        /// </summary>
        public bool TryContinueFullBurn(
            GameState game,
            string playerId,
            IReadOnlyList<string> path,
            out FlyResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (game.FlyRangeBonusThisAction < 1)
            {
                error = "No Fly range bonus to spend.";
                return false;
            }
            if (game.PendingNavDraws.Count > 0 || game.PendingAlertSectors.Count > 0 || game.PendingEncounter.HasValue)
            {
                error = "Resolve pending Nav / Alert / encounter before continuing the Fly.";
                return false;
            }
            if (path == null || path.Count < 2)
            {
                error = "Continue path must include origin and at least one entered sector.";
                return false;
            }
            if (path[0] != player.SectorId)
            {
                error = "Continue path must start in the player's current sector.";
                return false;
            }

            var hops = path.Count - 1;
            if (hops > game.FlyRangeBonusThisAction)
            {
                error = $"Path length {hops} exceeds Fly range bonus {game.FlyRangeBonusThisAction}.";
                return false;
            }

            if (!_movement.TryFullBurn(path, hops, game.Tokens, out var plan, out error) || plan == null)
                return false;

            if (game.DiscardFuelPerExtraSectorThisFly)
            {
                if (player.Fuel < hops)
                {
                    error = "Not enough fuel.";
                    return false;
                }
                player.Fuel -= hops;
            }

            // Already paid Full Burn initiate Fuel; do not charge again.
            var zeroFuelPlan = new MovementPlan(
                plan.Kind,
                plan.FromSectorId,
                plan.ToSectorId,
                plan.Path,
                plan.EnteredSteps,
                fuelCost: 0);

            ApplyContinue(game, player, zeroFuelPlan, out result);
            game.FlyRangeBonusThisAction -= hops;
            return true;
        }

        private static void ApplyContinue(
            GameState game,
            PlayerState player,
            MovementPlan plan,
            out FlyResult result)
        {
            var steps = new List<MovementStep>();
            var path = new List<string> { plan.FromSectorId };
            var stopped = false;

            foreach (var step in plan.EnteredSteps)
            {
                steps.Add(step);
                path.Add(step.SectorId);
                if (AlertTokenRules.SectorHasAlerts(game.Tokens, step.SectorId, game.UseAlertTokens))
                    game.PendingAlertSectors.Add(step.SectorId);

                // FAQ 4.1 p.14: Contact before Nav; Full Stop Contact skips the Nav draw for that Sector.
                var stopForContact = false;
                if (step.Encounter.HasValue)
                {
                    var encounter = step.Encounter.Value;
                    var allianceToken = encounter == TokenKind.AllianceCruiser
                        || encounter == TokenKind.OperativeCorvette;
                    if (!allianceToken || AlertTokenRules.IsOutlawShip(player))
                    {
                        game.PendingEncounter = encounter;
                        game.PendingEncounterSectorId = step.SectorId;
                        game.PendingEncounterPlayerId = player.Id;
                        // FAQ Cry Baby: entering Cruiser Sector deferred Nav until Contact or Cry Baby.
                        game.PendingEncounterDeferredNav =
                            encounter == TokenKind.AllianceCruiser;
                        stopForContact = true;
                        stopped = true;
                    }
                }

                if (step.DrawsNavCard && !stopForContact)
                    game.PendingNavDraws.Add(new PendingNavDraw(step.SectorId, step.NavRegion));

                if (stopped)
                    break;
            }

            var applied = new MovementPlan(
                plan.Kind,
                plan.FromSectorId,
                path[path.Count - 1],
                path,
                steps,
                plan.FuelCost);

            player.SectorId = applied.ToSectorId;
            result = new FlyResult(applied, stopped);
        }
    }
}
