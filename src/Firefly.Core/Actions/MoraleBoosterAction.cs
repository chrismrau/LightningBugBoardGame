using System;
using System.Collections.Generic;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Morale Booster (Emma / Helen / Lucy) and Love Bot: spend a Crew Action to remove
    /// Disgruntled from one legal crew. FAQ 4.1 p.8 may — target always suspends when unset.
    /// </summary>
    public sealed class MoraleBoosterRequest
    {
        /// <summary>
        /// Target crew id. Null/empty → <see cref="PendingChoiceKinds.MoraleBoosterTarget"/>.
        /// </summary>
        public string? TargetCrewId { get; set; }
    }

    public sealed class MoraleBoosterResult
    {
        public string TargetCrewId { get; }
        public string TargetName { get; }

        public MoraleBoosterResult(string targetCrewId, string targetName)
        {
            TargetCrewId = targetCrewId;
            TargetName = targetName;
        }
    }

    public sealed class MoraleBoosterAction
    {
        private string? _pendingPlayerId;

        public bool TryClearDisgruntled(
            GameState game,
            string playerId,
            MoraleBoosterRequest? request,
            out MoraleBoosterResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (!AbilityDispatcher.CanClearDisgruntledAction(game, player))
            {
                error = "No Morale Booster or Love Bot ability is available.";
                return false;
            }
            if (!game.CanTakeAction(TurnAction.Crew, out error))
                return false;

            var legal = AbilityDispatcher.LegalMoraleBoosterTargets(game, player);
            if (legal.Count == 0)
            {
                error = "No Disgruntled crew can be cleared.";
                return false;
            }

            var targetId = request?.TargetCrewId;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                var pending = new PendingChoice(
                    playerId,
                    PendingChoiceKinds.MoraleBoosterTarget,
                    options: legal,
                    prompt: "Choose a Disgruntled crew to clear.");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                _pendingPlayerId = playerId;
                error = "Choose which Disgruntled crew to clear.";
                return false;
            }

            return Finish(game, player, targetId!, legal, out result, out error);
        }

        public bool TryResume(
            GameState game,
            ChoiceSubmission submission,
            out MoraleBoosterResult? result,
            out string? error)
        {
            result = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.MoraleBoosterTarget,
                    StringComparison.Ordinal))
            {
                error = "No Morale Booster target choice is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_pendingPlayerId))
            {
                error = "Morale Booster resume state is missing.";
                return false;
            }

            var playerId = _pendingPlayerId!;
            var targetId = submission.SelectedOptionId ?? submission.Value;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                error = "A target crew id is required.";
                return false;
            }

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            var player = game.GetPlayer(playerId);
            var legal = AbilityDispatcher.LegalMoraleBoosterTargets(game, player);
            _pendingPlayerId = null;
            return Finish(game, player, targetId!, legal, out result, out error);
        }

        private static bool Finish(
            GameState game,
            PlayerState player,
            string targetId,
            IReadOnlyList<string> legal,
            out MoraleBoosterResult? result,
            out string? error)
        {
            result = null;
            var allowed = false;
            foreach (var id in legal)
            {
                if (string.Equals(id, targetId, StringComparison.Ordinal))
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed)
            {
                error = $"'{targetId}' is not a legal Morale Booster target.";
                return false;
            }

            var member = player.Roster.Find(targetId);
            if (member == null || !member.Disgruntled)
            {
                error = "Target crew must be Disgruntled.";
                return false;
            }

            if (!game.TryConsumeAction(TurnAction.Crew, out error))
                return false;

            member.Disgruntled = false;
            result = new MoraleBoosterResult(member.Id, member.Name);
            return true;
        }
    }
}
