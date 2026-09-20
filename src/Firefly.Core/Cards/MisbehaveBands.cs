using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Misbehave-local structured effect types. Do not share Nav's effect vocabulary yet —
    /// PendingChoice will integrate later (core-behaviors plan item 6 / 9).
    /// </summary>
    public enum MisbehaveEffectType
    {
        Proceed,
        Botched,
        WarrantIssued,
        KillCrew,
        KillAllCrew,
        LoadCargo,
        LoadContraband,
        TakeCash,
        Wanted,
        DisgruntleMoral,
        DisgruntleMercs,
        DisgruntleTech,
        ClearDisgruntled,
        LoseSolid,
        DiscardWarrants,
        ReplaceCard
    }

    /// <summary>
    /// One structured overlay effect. Count is used for KillCrew / Load* / TakeCash / DiscardWarrants.
    /// </summary>
    public sealed class MisbehaveEffect
    {
        public MisbehaveEffectType Type { get; }
        public int Count { get; }

        public MisbehaveEffect(MisbehaveEffectType type, int count = 0)
        {
            Type = type;
            Count = count;
        }
    }

    /// <summary>
    /// Optional structured skill result band. Prose <see cref="Text"/> may mirror printed
    /// band wording for display / prose-applicator fallback; <see cref="Effects"/> is the
    /// preferred overlay when present.
    /// </summary>
    public sealed class MisbehaveBand
    {
        public int Min { get; }
        /// <summary>Inclusive max. Null means open-ended (printed N+).</summary>
        public int? Max { get; }
        public string? Text { get; }
        public IReadOnlyList<MisbehaveEffect> Effects { get; }

        public MisbehaveBand(int min, int? max, string? text, IReadOnlyList<MisbehaveEffect>? effects)
        {
            Min = min;
            Max = max;
            Text = text;
            Effects = effects ?? Array.Empty<MisbehaveEffect>();
        }

        public bool Matches(int sum)
        {
            if (sum < Min)
                return false;
            return Max == null || sum <= Max.Value;
        }

        /// <summary>
        /// Director's Cut p.14 / GF9: Skill Tests list results under the target; the rolled
        /// total selects the matching printed band.
        /// </summary>
        public static MisbehaveBand? Pick(IReadOnlyList<MisbehaveBand>? bands, int sum)
        {
            if (bands == null || bands.Count == 0)
                return null;
            MisbehaveBand? picked = null;
            foreach (var band in bands)
            {
                if (band.Matches(sum))
                    picked = band;
            }
            return picked;
        }
    }

    /// <summary>
    /// Optional structured skill test on an option. Prefer this over parsing <c>details</c>
    /// when present. Bribes / Kosherized mirror GF9 p.6 / Director's Cut p.14.
    /// </summary>
    public sealed class MisbehaveSkillCheckSpec
    {
        public Skill Skill { get; }
        public int Target { get; }
        public bool Kosherized { get; }
        public bool BribesAllowed { get; }

        public MisbehaveSkillCheckSpec(Skill skill, int target, bool kosherized = false, bool bribesAllowed = false)
        {
            Skill = skill;
            Target = target;
            Kosherized = kosherized;
            BribesAllowed = bribesAllowed;
        }

        public SkillCheck ToSkillCheck() => new SkillCheck(Skill, Target, Kosherized, BribesAllowed);
    }

    /// <summary>
    /// Card-level skillThresholds from Misbehave.json (fail-band upper bounds). Metadata for
    /// tooling / PR 2 migration; not required for prose resolution.
    /// </summary>
    public sealed class MisbehaveSkillThresholds
    {
        public int? Fight { get; }
        public int? Tech { get; }
        public int? Talk { get; }

        public MisbehaveSkillThresholds(int? fight, int? tech, int? talk)
        {
            Fight = fight;
            Tech = tech;
            Talk = talk;
        }
    }
}
