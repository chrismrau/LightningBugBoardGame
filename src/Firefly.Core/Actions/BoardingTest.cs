using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// PBH p.3 / Director's Cut: Boarding Rival Ships.
    /// "The player may choose to use either TECH or NEGOTIATE skill."
    /// FAQ 4.1 p.9: Boarding is a prerequisite — not convertible to Fight (Stitch).
    /// Printed piracy cards: Tech 6 or Negotiate 6 (Skill Test, N dice).
    /// </summary>
    public static class BoardingTest
    {
        public const int DefaultTarget = 6;

        public static bool IsAllowedSkill(Skill skill) =>
            skill == Skill.Tech || skill == Skill.Talk;

        /// <summary>
        /// Resolve a Boarding Test. Fails closed if Fight (or other) is chosen.
        /// </summary>
        public static bool TryResolve(
            PlayerState player,
            Skill skill,
            IRng rng,
            out SkillCheckResult result,
            out string? error,
            int target = DefaultTarget)
        {
            result = null!;
            if (!IsAllowedSkill(skill))
            {
                error = "Boarding Test uses Tech or Negotiate only (PBH p.3).";
                return false;
            }

            if (target < 1)
                target = DefaultTarget;

            var check = new SkillCheck(skill, target);
            return check.TryResolve(player, rng, out result, out error);
        }

        /// <summary>
        /// Parse "Tech 6 or Negotiate 6" style boarding target from job Description.
        /// </summary>
        public static int TargetFromDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return DefaultTarget;
            if (SkillCheck.TryParse(description, out var check))
                return check.Target;
            return DefaultTarget;
        }
    }
}
