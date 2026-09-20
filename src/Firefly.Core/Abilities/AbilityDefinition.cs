using System;
using System.Collections.Generic;

namespace Firefly.Core.Abilities
{
    /// <summary>
    /// One typed ability effect from card JSON/TSV. English description is display-only.
    /// </summary>
    public sealed class AbilityDefinition
    {
        public string Type { get; }
        /// <summary>
        /// FAQ 4.1 p.8: mandatory unless the printed ability says "may".
        /// Optional (<c>mandatory: false</c>) abilities suspend via PendingChoice when their trigger fires.
        /// </summary>
        public bool Mandatory { get; }
        public int Amount { get; }
        /// <summary>Fight / Tech / Talk for <see cref="AbilityTypes.SkillAddend"/>.</summary>
        public string? Skill { get; }
        /// <summary>Target crew name for subject-scoped abilities (e.g. River Tam).</summary>
        public string? Subject { get; }
        /// <summary>
        /// GF9 / Director's Cut: Job-only abilities do not apply while Working Goals.
        /// Kernel has no Goal Work path yet — flag is reserved for that hook.
        /// </summary>
        public bool JobOnly { get; }

        public AbilityDefinition(
            string type,
            bool mandatory = true,
            int amount = 0,
            string? skill = null,
            string? subject = null,
            bool jobOnly = false)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Mandatory = mandatory;
            Amount = amount;
            Skill = skill;
            Subject = subject;
            JobOnly = jobOnly;
        }

        public bool MatchesType(string type) =>
            string.Equals(Type, type, StringComparison.OrdinalIgnoreCase);

        public static IReadOnlyList<AbilityDefinition> Empty { get; } =
            Array.Empty<AbilityDefinition>();
    }
}
