namespace Firefly.Core.Abilities
{
    /// <summary>
    /// Thin context for ability evaluation. Full PendingChoice comes later.
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

        public static AbilityContext None { get; } = new AbilityContext();

        public static AbilityContext WorkingJob { get; } = new AbilityContext { IsWorkingJob = true };
    }
}
