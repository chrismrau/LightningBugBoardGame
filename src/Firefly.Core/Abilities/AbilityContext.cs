namespace Firefly.Core.Abilities
{
    /// <summary>
    /// Thin context for ability evaluation (Job vs Goal; Work gear lock).
    /// Optional <c>may</c> abilities use PendingChoice at their trigger sites.
    /// </summary>
    public sealed class AbilityContext
    {
        /// <summary>
        /// GF9 / Director's Cut: "special abilities that apply during Jobs do not apply while Working Goals."
        /// Goal Work is not implemented — callers leave this false until a Goal Work path exists.
        /// </summary>
        public bool IsWorkingGoal { get; set; }

        /// <summary>True while resolving a Work Action / Misbehave site (gear switch locked; onboard unused).</summary>
        public bool IsWorkingJob { get; set; }

        /// <summary>True while resolving a Fly / Nav skill test (Wash's Lucky Dinosaurs).</summary>
        public bool IsFlying { get; set; }

        /// <summary>True while resolving Misbehave (Jayne's Cunning Hat).</summary>
        public bool IsMisbehaving { get; set; }

        public static AbilityContext None { get; } = new AbilityContext();

        public static AbilityContext WorkingJob { get; } = new AbilityContext { IsWorkingJob = true };

        public static AbilityContext Flying { get; } = new AbilityContext { IsFlying = true };

        public static AbilityContext Misbehaving { get; } =
            new AbilityContext { IsWorkingJob = true, IsMisbehaving = true };
    }
}
