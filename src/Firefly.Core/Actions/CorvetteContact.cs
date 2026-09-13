using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class CorvetteContactChoice
    {
        /// <summary>
        /// Wanted crew removed from play. Required when Wanted crew are present and not protected.
        /// </summary>
        public string? RemoveWantedCrewId { get; set; }

        /// <summary>
        /// Corvette Contact Card: gear / ship upgrades that protect from Wanted Crew Rolls
        /// prevent the Wanted-crew removal.
        /// </summary>
        public bool ProtectedByWantedRollGear { get; set; }
    }

    public sealed class CorvetteContactResult
    {
        public string? WantedRemovedId { get; }
        public bool WantedRemovalPrevented { get; }
        public int FugitivesDiscarded { get; }
        public int FugitivesKeptInStash { get; }

        public CorvetteContactResult(
            string? wantedRemovedId,
            bool wantedRemovalPrevented,
            int fugitivesDiscarded,
            int fugitivesKeptInStash)
        {
            WantedRemovedId = wantedRemovedId;
            WantedRemovalPrevented = wantedRemovalPrevented;
            FugitivesDiscarded = fugitivesDiscarded;
            FugitivesKeptInStash = fugitivesKeptInStash;
        }
    }

    /// <summary>
    /// Operative's Corvette Contact Card (Kalidasa / Director's Cut p.44 image):
    /// Resolve when the Corvette ends its move in an Outlaw Ship's Sector and when an
    /// Outlaw Ship moves into the Corvette's Sector.
    /// — Remove one Wanted Crew from play (gear that ignores Wanted Crew Rolls prevents this).
    /// — Discard all Fugitive Tokens not in your Stash.
    /// — If Flying, Full Stop.
    /// </summary>
    public static class CorvetteContact
    {
        public static bool TryResolve(
            GameState game,
            out CorvetteContactResult? result,
            out string? error,
            CorvetteContactChoice? choice = null)
        {
            result = null;
            error = null;
            if (game.PendingEncounter != TokenKind.OperativeCorvette)
            {
                error = "No Operative's Corvette encounter is pending.";
                return false;
            }

            var player = game.CurrentPlayer;
            var sector = game.PendingEncounterSectorId ?? player.SectorId;
            player.SectorId = sector;
            game.Tokens = game.Tokens.WithOperativeCorvette(sector);

            if (!TryApply(game, player, choice, out result, out error))
                return false;

            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingNavDraws.Clear();
            return true;
        }

        /// <summary>
        /// Apply Contact without requiring PendingEncounter (Corvette Nav / Alert snap onto Outlaw).
        /// </summary>
        public static bool TryApplyImmediate(
            GameState game,
            out CorvetteContactResult? result,
            out string? error,
            CorvetteContactChoice? choice = null) =>
            TryApply(game, game.CurrentPlayer, choice, out result, out error);

        public static bool TryApply(
            GameState game,
            PlayerState player,
            CorvetteContactChoice? choice,
            out CorvetteContactResult? result,
            out string? error)
        {
            result = null;
            error = null;
            if (!AlertTokenRules.IsOutlawShip(player))
            {
                error = "Corvette Contact only applies to Outlaw Ships.";
                return false;
            }

            string? removedId = null;
            var prevented = choice != null && choice.ProtectedByWantedRollGear;
            var wanted = player.Roster.WantedMembers();
            if (wanted.Count > 0 && !prevented)
            {
                CrewMember? target = null;
                var removeId = choice?.RemoveWantedCrewId;
                foreach (var member in wanted)
                {
                    if (string.IsNullOrWhiteSpace(removeId)
                        || member.Id.Equals(removeId, System.StringComparison.OrdinalIgnoreCase)
                        || member.Name.Equals(removeId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        target = member;
                        break;
                    }
                }
                if (target == null)
                {
                    error = "Corvette Contact requires choosing a Wanted crew member to remove from play.";
                    return false;
                }
                player.Roster.Remove(target.Id);
                game.RemovedFromPlay.Add(target.Id);
                removedId = target.Id;
            }

            // Free rearrange: pack as many Fugitive Tokens into Stash as fit; discard the rest.
            // FAQ 4.1 p.14 / Corvette Contact: Fugitive Tokens only — Bound bounty cards stay.
            var stashSlots = System.Math.Max(0, player.StashHold);
            var kept = System.Math.Min(player.Fugitives, stashSlots);
            var discarded = player.Fugitives - kept;
            player.Fugitives = kept;

            result = new CorvetteContactResult(removedId, prevented, discarded, kept);
            return true;
        }

        /// <summary>
        /// Queue Corvette Contact for the current player when they are an Outlaw sharing the Corvette's Sector.
        /// </summary>
        public static bool TryQueueIfOutlaw(GameState game, string sectorId)
        {
            if (game.Tokens.OperativeCorvetteSectorId == null)
                return false;
            if (!string.Equals(
                    game.Tokens.OperativeCorvetteSectorId,
                    sectorId,
                    System.StringComparison.OrdinalIgnoreCase))
                return false;
            if (!AlertTokenRules.IsOutlawShip(game.CurrentPlayer))
                return false;
            if (!string.Equals(game.CurrentPlayer.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase))
                return false;

            game.PendingEncounter = TokenKind.OperativeCorvette;
            game.PendingEncounterSectorId = sectorId;
            return true;
        }
    }
}
