using Firefly.Core.State;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// PBH / Director's Cut p.35: Lawmen stay onboard during Illegal Jobs.
    /// "Lawmen will never work an Illegal Job. If you are working an Illegal Job,
    /// all Lawmen stay onboard ship."
    /// </summary>
    public static class LawmanRules
    {
        public static bool IsLawman(CrewMember member) =>
            member != null && member.Card.HasProfession("Lawman");

        /// <summary>
        /// True when this crew stays onboard (does not contribute skills/tags) for the Job.
        /// </summary>
        public static bool StaysOnboardForJob(CrewMember member, JobCard? job) =>
            job != null && !job.Legal && IsLawman(member);

        public static int CrewSkillForJob(PlayerState player, JobCard? job, Skill skill)
        {
            var total = 0;
            foreach (var member in player.Roster.Members)
            {
                if (StaysOnboardForJob(member, job))
                    continue;
                total += skill switch
                {
                    Skill.Fight => member.Card.Fight,
                    Skill.Tech => member.Card.Tech,
                    _ => member.Card.Talk
                };
            }
            return total;
        }

        public static bool HasProfessionForJob(PlayerState player, JobCard? job, string profession)
        {
            foreach (var member in player.Roster.Members)
            {
                if (StaysOnboardForJob(member, job))
                    continue;
                if (member.Card.HasProfession(profession))
                    return true;
            }
            return false;
        }

        public static bool HasKeywordForJob(PlayerState player, JobCard? job, string keyword)
        {
            foreach (var member in player.Roster.Members)
            {
                if (StaysOnboardForJob(member, job))
                    continue;
                foreach (var k in member.Card.Keywords)
                {
                    if (string.Equals(k, keyword, System.StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
    }
}
