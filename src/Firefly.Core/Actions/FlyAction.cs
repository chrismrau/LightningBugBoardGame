using System;
using System.Collections.Generic;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.Map;
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
    /// Goods discarded for Full Mess Deck mid-Fly.
    /// </summary>
    public enum FullMessDiscardKind
    {
        Cargo,
        Contraband
    }

    /// <summary>
    /// Official Fly action: Mosey or Full Burn. Consumes the player's one action for the turn.
    /// Full Burn spends 1 fuel (unless the drive does not require it) and queues a Nav draw
    /// for each sector actually entered. Movement stops on the first Cruiser or Cutter entered.
    /// Also: Dobson Mole Cruiser move; Full Mess Deck / Long-Range Scanner mid-Fly mays.
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

            if (!_movement.TryFullBurn(path, player.GetEffectiveDriveRange(game), game.Tokens, out var plan, out error) || plan == null)
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

        /// <summary>
        /// Dobson Mole: In Alliance Space, move Alliance Cruiser to your Sector as a Fly Action.
        /// Supplies.tsv / PBH. Queues Cruiser Contact for Outlaws in that Sector (FAQ 4.1 p.14).
        /// </summary>
        public bool TryMoveCruiserWithDobson(
            GameState game,
            string playerId,
            out string? error,
            string? safeHarborRedirectSectorId = null)
        {
            error = null;
            if (!CanAct(game, playerId, out var player, out error))
                return false;

            if (!AbilityDispatcher.HasMoveCruiserAsFly(player))
            {
                error = "Dobson Mole ability is required to move the Cruiser as a Fly Action.";
                return false;
            }

            if (!IsAllianceSpace(game, player.SectorId))
            {
                error = "Dobson may only move the Cruiser while in Alliance Space.";
                return false;
            }

            if (HavenRules.NeedsSafeHarborRedirect(game, player.SectorId, safeHarborRedirectSectorId))
            {
                var options = HavenRules.EligibleSafeHarborRedirects(game, player.SectorId);
                var pending = new PendingChoice(
                    player.Id,
                    PendingChoiceKinds.SectorDestination,
                    contextId: SectorDestinationContexts.SafeHarbor(player.SectorId),
                    options: options,
                    prompt: "Safe Harbor: choose an adjacent Sector for the Alliance Cruiser.");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                error = pending.Prompt;
                return false;
            }

            if (!HavenRules.TryPlaceAllianceCruiser(
                    game,
                    player.SectorId,
                    safeHarborRedirectSectorId,
                    out error))
                return false;

            game.ClearPendingEvents();
            game.TryConsumeAction(TurnAction.Fly, out _);
            var cruiserSector = game.Tokens.AllianceCruiserSectorId ?? player.SectorId;
            AllianceCruiserContact.QueueForOutlawsInSector(game, cruiserSector);
            return true;
        }

        /// <summary>
        /// Resume Dobson after Safe Harbor redirect PendingChoice.
        /// </summary>
        public bool TryResumeDobsonSafeHarbor(
            GameState game,
            ChoiceSubmission submission,
            out string? error)
        {
            error = null;
            if (game.PendingChoice == null
                || game.PendingChoice.Kind != PendingChoiceKinds.SectorDestination
                || !SectorDestinationContexts.TryParseSafeHarbor(
                    game.PendingChoice.ContextId, out _))
            {
                error = "No Dobson Safe Harbor choice is pending.";
                return false;
            }

            var playerId = game.PendingChoice.PlayerId;
            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            var redirect = submission.SelectedOptionId ?? submission.Value;
            return TryMoveCruiserWithDobson(
                game,
                playerId,
                out error,
                safeHarborRedirectSectorId: redirect);
        }

        /// <summary>
        /// Full Mess Deck: during a Fly Action, discard 1 Cargo or Contraband to clear all Disgruntled.
        /// Esmeralda rules: in addition to the Fly Action, not instead.
        /// </summary>
        public bool TryFullMessDeck(
            GameState game,
            string playerId,
            FullMessDiscardKind discard,
            out int cleared,
            out string? error)
        {
            cleared = 0;
            error = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (!game.ActionWasUsed(TurnAction.Fly))
            {
                error = "Full Mess Deck may only be used during a Fly Action.";
                return false;
            }
            if (game.PendingChoice != null)
            {
                error = "Resolve the pending choice before using Full Mess Deck.";
                return false;
            }
            if (!AbilityDispatcher.HasDiscardGoodsClearDisgruntled(game, player, AbilityContext.Flying))
            {
                error = "Full Mess Deck is not installed.";
                return false;
            }

            if (discard == FullMessDiscardKind.Cargo)
            {
                if (player.Cargo < 1)
                {
                    error = "No Cargo to discard.";
                    return false;
                }
                player.Cargo--;
            }
            else
            {
                if (player.Contraband < 1)
                {
                    error = "No Contraband to discard.";
                    return false;
                }
                player.Contraband--;
            }

            cleared = player.Roster.ClearDisgruntled();
            return true;
        }

        /// <summary>
        /// Long-Range Scanner Array: during a Fly Action, resolve Alert Tokens in an adjacent Sector.
        /// Blue Sun: may do this at any time during Fly; does not interrupt; multiple times allowed.
        /// </summary>
        public bool TryLongRangeScanner(
            GameState game,
            string playerId,
            string adjacentSectorId,
            IRng rng,
            out AlertResolution? resolution,
            out string? error,
            AlertResolveChoice? choice = null)
        {
            resolution = null;
            error = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (!game.ActionWasUsed(TurnAction.Fly))
            {
                error = "Long-Range Scanner may only be used during a Fly Action.";
                return false;
            }
            if (game.PendingChoice != null)
            {
                error = "Resolve the pending choice before using Long-Range Scanner.";
                return false;
            }
            if (!AbilityDispatcher.HasResolveAdjacentAlertTokens(game, player, AbilityContext.Flying))
            {
                error = "Long-Range Scanner Array is not installed.";
                return false;
            }
            if (!game.UseAlertTokens)
            {
                error = "Alert Tokens are not in use.";
                return false;
            }
            if (!AreAdjacent(game, player.SectorId, adjacentSectorId))
            {
                error = "Long-Range Scanner target must be an adjacent Sector.";
                return false;
            }

            return AlertTokenResolver.TryResolveSector(
                game, adjacentSectorId, rng, choice, out resolution, out error);
        }

        /// <summary>
        /// Resume Emissions Recycler after two Big Black Nav cards: take 1 Fuel or decline.
        /// </summary>
        public static bool TryResumeEmissionsFuel(
            GameState game,
            ChoiceSubmission submission,
            out bool tookFuel,
            out string? error)
        {
            tookFuel = false;
            error = null;
            if (game.PendingChoice == null
                || game.PendingChoice.Kind != PendingChoiceKinds.EmissionsFuel)
            {
                error = "No Emissions Recycler fuel choice is pending.";
                return false;
            }

            if (!game.TrySubmitChoice(game.PendingChoice.PlayerId, submission, out _, out error))
                return false;

            var player = game.CurrentPlayer;
            game.ConsecutiveBigBlackNavThisFly = 0;

            var take = string.Equals(
                submission.SelectedOptionId,
                EmissionsFuelOptions.TakeFuel,
                StringComparison.Ordinal);
            if (!take)
                return true;

            if (game.EmissionsFuelTakenThisFly)
            {
                error = "Emissions Recycler fuel already taken this Fly Action.";
                return false;
            }
            if (!HoldSpace.Fits(player, addFuel: 1))
            {
                error = "No hold space for Fuel.";
                return false;
            }

            player.Fuel++;
            game.EmissionsFuelTakenThisFly = true;
            tookFuel = true;
            return true;
        }

        private static bool IsAllianceSpace(GameState game, string sectorId)
        {
            if (!game.Map.TryGet(sectorId, out var sector))
                return false;
            return sector.NavRegion == NavRegion.Alliance;
        }

        private static bool AreAdjacent(GameState game, string fromSectorId, string toSectorId)
        {
            foreach (var n in game.Map.Neighbors(fromSectorId))
            {
                if (string.Equals(n, toSectorId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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

            // Consume Fly before queuing Alert/Nav pending — those are part of this Fly Action
            // (Blue Sun: resolve Alert Tokens during Fly; must not block TryConsumeAction).
            if (!game.TryConsumeAction(TurnAction.Fly, out _))
            {
                // Should not fail after ClearPendingEvents; leave state unchanged if it does.
            }

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
