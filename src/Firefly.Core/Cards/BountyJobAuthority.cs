using System;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// PBH Bounty Cards are a separate deck (<see cref="Bounties.json"/>).
    /// Duplicate rows in Jobs.json must not drive Contact Decks or Work-as-Job.
    /// </summary>
    public static class BountyJobAuthority
    {
        /// <summary>
        /// True when this Jobs.json row is the same printed bounty as a
        /// <see cref="BountyCard"/> (Wanted / Cortex Alert). Play those via
        /// <c>BountyAction</c> and the Most Wanted List, not Contact Deal/Work.
        /// </summary>
        public static bool IsCoveredByBountyDeck(JobCard job, BountyCatalog? bounties)
        {
            if (job == null || bounties == null)
                return false;
            var name = FugitiveNameFromJobName(job.Name);
            return !string.IsNullOrEmpty(name) && bounties.TryResolve(name, out _);
        }

        /// <summary>
        /// "Wanted: Jayne" → Jayne; "Cortex Alert: Bandits" → Bandits.
        /// </summary>
        public static string FugitiveNameFromJobName(string? jobName)
        {
            if (string.IsNullOrWhiteSpace(jobName))
                return string.Empty;
            var name = jobName.Trim();
            const string wanted = "Wanted:";
            const string cortex = "Cortex Alert:";
            if (name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                return name.Substring(wanted.Length).Trim();
            if (name.StartsWith(cortex, StringComparison.OrdinalIgnoreCase))
                return name.Substring(cortex.Length).Trim();
            return name;
        }
    }
}
