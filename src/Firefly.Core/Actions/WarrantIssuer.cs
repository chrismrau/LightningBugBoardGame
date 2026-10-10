using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Shared Warrant Issued hook. GF9 p.16 / Director's Cut p.24:
    /// <list type="bullet">
    /// <item>
    /// Zero Tolerance — receiving a Warrant for any reason causes reputation loss with Harken;
    /// may not become Solid with Harken while you have a Warrant (gated in
    /// <see cref="ContactSolidBenefits.BecomeSolid"/>).
    /// </item>
    /// <item>
    /// Losing Rep — Warrant while Working a Job for a Contact → lose Solid with that Job's Contact.
    /// Working Goals are not Jobs (Director's Cut p.25); Increased Enforcement after completion
    /// is not "while working."
    /// </item>
    /// </list>
    /// </summary>
    public static class WarrantIssuer
    {
        /// <summary>
        /// Issue one Warrant token, then Zero Tolerance, then Losing Rep when Working a Job.
        /// Roberta Make Nice: pass <paramref name="discardRobertaInstead"/> or resume
        /// <see cref="ContactSolidBenefits.TryResumeDiscardOrLoseSolid"/>.
        /// Pass <paramref name="workingJob"/> when the Job is not on
        /// <see cref="GameState.PendingMisbehave"/> (e.g. Piracy showdown loss).
        /// </summary>
        public static bool TryIssue(
            GameState game,
            PlayerState player,
            out string? error,
            SolidRepChoice? solidRep = null,
            bool? discardRobertaInstead = null,
            JobCard? workingJob = null)
        {
            error = null;
            if (player == null)
            {
                error = "No player.";
                return false;
            }

            player.Warrants++;
            if (!ContactSolidBenefits.TryApplyZeroTolerance(
                    game, player, solidRep, discardRobertaInstead, out error))
                return false;

            workingJob ??= ResolveWorkingJob(game);
            return ContactSolidBenefits.TryApplyLosingRep(
                game, player, workingJob, solidRep, discardRobertaInstead, out error);
        }

        /// <summary>
        /// Job currently being Worked via Misbehave. Null for Goals or when not Working.
        /// </summary>
        public static JobCard? ResolveWorkingJob(GameState game)
        {
            var pending = game?.PendingMisbehave;
            if (pending == null || pending.IsGoalWork || string.IsNullOrWhiteSpace(pending.JobId))
                return null;
            if (game!.Jobs == null || !game.Jobs.TryGet(pending.JobId, out var job))
                return null;
            return job;
        }
    }
}
