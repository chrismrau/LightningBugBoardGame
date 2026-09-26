using System;

namespace Firefly.Core.State
{
    /// <summary>
    /// Director's Cut C&amp;P p.49 I'll Be in my Bunk / No one left: crew Returned to Ship
    /// during Misbehave are unusable for the remainder of that Job (and their Gear).
    /// </summary>
    public static class JobWorkCrew
    {
        public static bool IsReturnedToShip(PlayerState player, string crewId)
        {
            if (player == null || string.IsNullOrWhiteSpace(crewId))
                return false;
            foreach (var job in player.ActiveJobs)
            {
                if (job.IsReturnedToShip(crewId))
                    return true;
            }
            return false;
        }

        public static bool IsUnavailable(PlayerState player, CrewMember member) =>
            member != null && IsReturnedToShip(player, member.Id);

        public static bool HasAnyoneReturnedToShip(PlayerState player)
        {
            if (player == null)
                return false;
            foreach (var job in player.ActiveJobs)
            {
                if (job.ReturnedToShipCrewIds.Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Crew still Working the Job (not Returned to Ship). Killed crew are already off roster.
        /// </summary>
        public static int AvailableCount(PlayerState player)
        {
            if (player == null)
                return 0;
            var n = 0;
            foreach (var member in player.Roster.Members)
            {
                if (!IsUnavailable(player, member))
                    n++;
            }
            return n;
        }

        public static ActiveJob? FindJobTrackingReturns(PlayerState player, string? jobId)
        {
            if (player == null || string.IsNullOrWhiteSpace(jobId))
                return null;
            return player.FindActive(jobId);
        }

        public static void ReturnToShip(PlayerState player, string jobId, string crewId)
        {
            var active = player.FindActive(jobId)
                ?? throw new InvalidOperationException($"No Active Job '{jobId}' for Return to Ship.");
            active.ReturnToShip(crewId);
        }
    }
}
