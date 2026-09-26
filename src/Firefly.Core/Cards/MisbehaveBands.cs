using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Misbehave-only structured effects (outcome markers, Wanted, Solid loss, etc.).
    /// Shared Kill / Warrant / Load / TakeCash / DisgruntleMoral / ClearDisgruntled live on
    /// <see cref="CardEffectType"/> — do not duplicate them here.
    /// </summary>
    public enum MisbehaveLocalEffectType
    {
        Proceed,
        Botched,
        KillAllCrew,
        Wanted,
        DisgruntleMercs,
        DisgruntleTech,
        DisgruntleAllCrew,
        LoseSolid,
        DiscardWarrants,
        ReplaceCard,
        /// <summary>FIRST-step carry: next Fight Test is Kosherized (C&amp;P Secure Perimeter).</summary>
        NextFightKosherized,
        /// <summary>FIRST-step carry: +N Negotiate to next Test (count = N).</summary>
        NextTalkBonus,
        /// <summary>
        /// Take/Load N Goods (count = N). Blue Sun: Cargo, Contraband, Fuel, Parts — mix via
        /// <see cref="State.PendingChoiceKinds.GoodsMix"/>.
        /// </summary>
        LoadGoods,
        /// <summary>
        /// Optional exclusive discard: up to N Warrants <em>or</em> up to N Wanted Tokens
        /// (count = N). Proceed with zero discards is legal.
        /// </summary>
        MayDiscardWarrantsOrWanted,
        /// <summary>Discard N Cargo from the hold (count = N; default 1). Fails if Cargo &lt; N.</summary>
        DiscardCargo
    }

    /// <summary>
    /// Printed skill-check target modifiers (e.g. Alliance Patrol
    /// "Negotiate 8 - 1 for each of your Warrants"). Applied as a roll bonus against
    /// printed absolute bands so "8+ Proceed" stays correct when the TN is reduced.
    /// </summary>
    public enum MisbehaveTargetModifierType
    {
        MinusPerWarrant
    }

    /// <summary>
    /// One structured target modifier on a skill check.
    /// <see cref="Amount"/> is the per-unit delta (Alliance Patrol prints 1).
    /// </summary>
    public sealed class MisbehaveTargetModifier
    {
        public MisbehaveTargetModifierType Type { get; }
        public int Amount { get; }

        public MisbehaveTargetModifier(MisbehaveTargetModifierType type, int amount)
        {
            Type = type;
            Amount = amount;
        }
    }

    /// <summary>
    /// Printed "+N Skill with TAG" gear/profession bonus on a structured skill check
    /// (e.g. "+ 2 Tech with HACKING RIG"). Applied when <see cref="MisbehaveResolver.HasTag"/> matches.
    /// </summary>
    public sealed class MisbehaveSkillBonus
    {
        public int Amount { get; }
        public string Tag { get; }

        public MisbehaveSkillBonus(int amount, string tag)
        {
            Amount = amount;
            Tag = tag ?? "";
        }
    }

    /// <summary>
    /// One Misbehave structured overlay effect: either a shared <see cref="CardEffect"/> or a
    /// Misbehave-local type. Count on local effects is used for DiscardWarrants.
    /// </summary>
    public sealed class MisbehaveEffect
    {
        public CardEffect? Shared { get; }
        public MisbehaveLocalEffectType? Local { get; }
        public int Count { get; }

        public bool IsShared => Shared != null;
        public bool IsLocal => Local != null;

        private MisbehaveEffect(CardEffect? shared, MisbehaveLocalEffectType? local, int count)
        {
            Shared = shared;
            Local = local;
            Count = shared?.Count ?? count;
        }

        public static MisbehaveEffect Of(CardEffectType type, int count = 0) =>
            new MisbehaveEffect(new CardEffect(type, count), null, count);

        public static MisbehaveEffect Of(CardEffect shared) =>
            new MisbehaveEffect(shared ?? throw new ArgumentNullException(nameof(shared)), null, shared.Count);

        public static MisbehaveEffect Of(MisbehaveLocalEffectType type, int count = 0) =>
            new MisbehaveEffect(null, type, count);

        /// <summary>True when this effect is the given shared type.</summary>
        public bool Is(CardEffectType type) => Shared != null && Shared.Type == type;

        /// <summary>True when this effect is the given local type.</summary>
        public bool Is(MisbehaveLocalEffectType type) => Local == type;
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
        /// <summary>Optional structured "+N with TAG" bonuses; preferred over parsing details.</summary>
        public IReadOnlyList<MisbehaveSkillBonus> Bonuses { get; }
        /// <summary>
        /// Optional printed TN modifiers (e.g. minusPerWarrant). Applied as a roll bonus
        /// so absolute printed bands remain correct.
        /// </summary>
        public IReadOnlyList<MisbehaveTargetModifier> TargetModifiers { get; }

        public MisbehaveSkillCheckSpec(
            Skill skill,
            int target,
            bool kosherized = false,
            bool bribesAllowed = false,
            IReadOnlyList<MisbehaveSkillBonus>? bonuses = null,
            IReadOnlyList<MisbehaveTargetModifier>? targetModifiers = null)
        {
            Skill = skill;
            Target = target;
            Kosherized = kosherized;
            BribesAllowed = bribesAllowed;
            Bonuses = bonuses ?? Array.Empty<MisbehaveSkillBonus>();
            TargetModifiers = targetModifiers ?? Array.Empty<MisbehaveTargetModifier>();
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
