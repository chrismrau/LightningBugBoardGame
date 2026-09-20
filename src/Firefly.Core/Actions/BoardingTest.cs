using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// PBH p.3 / Director's Cut: Boarding Rival Ships.
    /// "The player may choose to use either TECH or NEGOTIATE skill."
    /// FAQ 4.1 p.9: Boarding is a prerequisite — not convertible to Fight (Stitch).
    /// Printed piracy cards: Tech 6 or Negotiate 6 (Skill Test, N dice).
    /// Cortland: Negotiate Boarding is a Negotiate Test (not a Showdown) — Bribes apply.
    /// </summary>
    public static class BoardingTest
    {
        public const int DefaultTarget = 6;

        public static bool IsAllowedSkill(Skill skill) =>
            skill == Skill.Tech || skill == Skill.Talk;

        /// <summary>
        /// Resolve a Boarding Test. Fails closed if Fight (or other) is chosen.
        /// Applies Cortland <c>bribesOnAnyNegotiate</c> on Talk. Callers must suspend
        /// via <see cref="SkillCheck.NeedsBribeChoice"/> before resolving when needed.
        /// </summary>
        public static bool TryResolve(
            PlayerState player,
            Skill skill,
            IRng rng,
            out SkillCheckResult result,
            out string? error,
            int target = DefaultTarget,
            SkillCheckChoice? choice = null)
        {
            result = null!;
            if (!IsAllowedSkill(skill))
            {
                error = "Boarding Test uses Tech or Negotiate only (PBH p.3).";
                return false;
            }

            if (target < 1)
                target = DefaultTarget;

            var check = SkillCheck.WithAbilityBribes(new SkillCheck(skill, target), player);
            return check.TryResolve(player, rng, out result, out error, choice);
        }

        /// <summary>
        /// Build the Boarding SkillCheck with Cortland Bribes enablement (no roll yet).
        /// </summary>
        public static SkillCheck BuildCheck(PlayerState player, Skill skill, int target = DefaultTarget)
        {
            if (target < 1)
                target = DefaultTarget;
            return SkillCheck.WithAbilityBribes(new SkillCheck(skill, target), player);
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
