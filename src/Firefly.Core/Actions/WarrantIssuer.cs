using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Shared Warrant Issued hook. GF9 p.16 / Director's Cut p.24 Zero Tolerance:
    /// receiving a Warrant for any reason causes reputation loss with Harken;
    /// may not become Solid with Harken while you have a Warrant (gated in
    /// <see cref="ContactSolidBenefits.BecomeSolid"/>).
    /// </summary>
    public static class WarrantIssuer
    {
        /// <summary>
        /// Issue one Warrant token, then apply Zero Tolerance (lose Harken Solid token if held).
        /// Roberta Make Nice: pass <paramref name="discardRobertaInstead"/> or resume
        /// <see cref="ContactSolidBenefits.TryResumeDiscardOrLoseSolid"/>.
        /// </summary>
        public static bool TryIssue(
            GameState game,
            PlayerState player,
            out string? error,
            SolidRepChoice? solidRep = null,
            bool? discardRobertaInstead = null)
        {
            error = null;
            if (player == null)
            {
                error = "No player.";
                return false;
            }

            player.Warrants++;
            return ContactSolidBenefits.TryApplyZeroTolerance(
                game, player, solidRep, discardRobertaInstead, out error);
        }
    }
}
