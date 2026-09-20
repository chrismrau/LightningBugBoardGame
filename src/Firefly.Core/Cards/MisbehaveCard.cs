using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class MisbehaveOption
    {
        public string Name { get; }
        /// <summary>
        /// Printed prose — display and durable source of truth. Structured fields are an overlay.
        /// </summary>
        public string Details { get; }
        /// <summary>Optional structured skill test; preferred over parsing <see cref="Details"/>.</summary>
        public MisbehaveSkillCheckSpec? SkillCheck { get; }
        /// <summary>Optional structured result bands; preferred over regex band selection.</summary>
        public IReadOnlyList<MisbehaveBand> Bands { get; }
        /// <summary>
        /// Optional option-level effects when there is no skill band (e.g. Requires + Proceed).
        /// Ignored when a structured band supplies effects after a skill roll.
        /// </summary>
        public IReadOnlyList<MisbehaveEffect> Effects { get; }

        public bool HasStructuredBands => Bands.Count > 0;
        public bool HasStructuredEffects => Effects.Count > 0;

        public MisbehaveOption(
            string name,
            string details,
            MisbehaveSkillCheckSpec? skillCheck = null,
            IReadOnlyList<MisbehaveBand>? bands = null,
            IReadOnlyList<MisbehaveEffect>? effects = null)
        {
            Name = name ?? "";
            Details = details ?? "";
            SkillCheck = skillCheck;
            Bands = bands ?? Array.Empty<MisbehaveBand>();
            Effects = effects ?? Array.Empty<MisbehaveEffect>();
        }
    }

    public sealed class MisbehaveCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Suit { get; }
        public string? Ace { get; }
        public string? Keyword { get; }
        public bool IsReshuffle { get; }
        public IReadOnlyList<MisbehaveOption> Options { get; }
        /// <summary>Optional card-level skillThresholds from JSON (not used for prose resolution).</summary>
        public MisbehaveSkillThresholds? SkillThresholds { get; }
        /// <summary>Optional card-level other flag string from JSON (e.g. Bribes, Crew of 5).</summary>
        public string? Other { get; }
        /// <summary>Explicit or derived from <see cref="Other"/> — Bribes skill-test flag.</summary>
        public bool Bribes { get; }
        /// <summary>Explicit or derived from <see cref="Other"/> — Kosherized skill-test flag.</summary>
        public bool Kosherized { get; }

        public MisbehaveCard(
            string id,
            string name,
            string? suit,
            string? ace,
            string? keyword,
            bool isReshuffle,
            IReadOnlyList<MisbehaveOption> options,
            MisbehaveSkillThresholds? skillThresholds = null,
            string? other = null,
            bool? bribes = null,
            bool? kosherized = null)
        {
            Id = id;
            Name = name;
            Suit = suit;
            Ace = ace;
            Keyword = keyword;
            IsReshuffle = isReshuffle;
            Options = options ?? new List<MisbehaveOption>();
            SkillThresholds = skillThresholds;
            Other = other;
            Bribes = bribes ?? ContainsFlag(other, "Bribes");
            Kosherized = kosherized ?? ContainsFlag(other, "Kosherized");
        }

        private static bool ContainsFlag(string? other, string flag) =>
            !string.IsNullOrEmpty(other)
            && other.IndexOf(flag, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
