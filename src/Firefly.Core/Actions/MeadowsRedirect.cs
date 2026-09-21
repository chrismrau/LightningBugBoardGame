using System;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Meadows Hero Worship helpers for Apprehend / Alliance seize (Kill N uses CrewKill).
    /// Supplies.tsv: "Any time a Crew is Killed, Apprehended, or Seized by the Alliance,
    /// you may Kill Meadows instead."
    /// </summary>
    public static class MeadowsRedirect
    {
        public static bool NeedsChoice(PlayerState owner, bool? acceptAlready) =>
            acceptAlready == null && AbilityDispatcher.FindMeadowsRedirect(owner) != null;

        public static bool TrySuspend(
            GameState game,
            PlayerState owner,
            string contextId,
            out string? error,
            string? prompt = null)
        {
            var pending = new PendingChoice(
                owner.Id,
                PendingChoiceKinds.MeadowsRedirect,
                contextId: contextId,
                options: new[] { MeadowsRedirectOptions.KillMeadows, MeadowsRedirectOptions.Decline },
                prompt: prompt ?? "Kill Meadows instead?");
            return game.TrySetPendingChoice(pending, out error);
        }

        public static bool TryParseAccept(ChoiceSubmission submission, out bool accept, out string? error)
        {
            accept = false;
            error = null;
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }
            if (submission.Accepted != null)
            {
                accept = submission.Accepted.Value;
                return true;
            }
            if (string.Equals(
                    submission.SelectedOptionId,
                    MeadowsRedirectOptions.KillMeadows,
                    StringComparison.Ordinal))
            {
                accept = true;
                return true;
            }
            if (string.Equals(
                    submission.SelectedOptionId,
                    MeadowsRedirectOptions.Decline,
                    StringComparison.Ordinal))
            {
                accept = false;
                return true;
            }
            error = "Kill Meadows or decline.";
            return false;
        }

        /// <summary>
        /// Kill Meadows (Medic / Med Bay apply) and leave the original threatened crew alone.
        /// </summary>
        public static KillResult KillMeadowsInstead(
            GameState game,
            PlayerState owner,
            IRng rng,
            KillChoice? killChoice = null)
        {
            var meadows = AbilityDispatcher.FindMeadowsRedirect(owner);
            if (meadows == null)
                return KillResult.None;
            var member = owner.Roster.Find(meadows.Id);
            if (member == null)
                return KillResult.None;
            // Scripted KillUpTo-style: auto-decline undecided Med Bay on this single kill.
            var choice = killChoice ?? new KillChoice { AcceptMedicReroll = false, AcceptMeadowsRedirect = false, MeadowsOfferedThisBatch = true };
            choice.AcceptMeadowsRedirect = false;
            choice.MeadowsOfferedThisBatch = true;
            if (choice.AcceptMedicReroll == null)
                choice.AcceptMedicReroll = false;
            return CrewKill.Apply(game, owner, member, rng, choice);
        }
    }
}
