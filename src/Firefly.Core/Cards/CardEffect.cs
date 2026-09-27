using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Shared card-effect vocabulary understood by both Nav and Misbehave applicators.
    /// Core intersection (PR #37) plus promoted leftovers (ClearDisgruntledMoral, LoadParts,
    /// LoadGoods, LoadUpTo). Nav-only locals use <see cref="NavLocalEffectType"/> (S5);
    /// Misbehave-only effects stay in <see cref="MisbehaveLocalEffectType"/>.
    /// JSON type names are camelCase of these identifiers (e.g. <c>killCrew</c>).
    /// </summary>
    public enum CardEffectType
    {
        WarrantIssued,
        KillCrew,
        LoadCargo,
        LoadContraband,
        TakeCash,
        DisgruntleMoral,
        ClearDisgruntled,
        /// <summary>Clear Disgruntled from Moral crew only (printed "Moral Crew").</summary>
        ClearDisgruntledMoral,
        /// <summary>Load / Take N Parts into the hold (count = N).</summary>
        LoadParts,
        /// <summary>
        /// Load N Goods (Blue Sun: Fuel/Parts/Cargo/Contraband mix). Count = N; mix via
        /// <see cref="Actions.CardEffectContext"/> Goods fields / PendingChoice GoodsMix.
        /// </summary>
        LoadGoods,
        /// <summary>
        /// Load up to N of one typed good. Count = max N; <see cref="CardEffect.Kind"/> required.
        /// Chosen amount via <see cref="Actions.CardEffectContext.LoadAmount"/>
        /// (null = max that fits, capped at Count).
        /// </summary>
        LoadUpTo
    }

    /// <summary>
    /// Typed hold good for <see cref="CardEffectType.LoadUpTo"/>
    /// (Cargo / Contraband / Parts / Fuel).
    /// </summary>
    public enum CardLoadKind
    {
        Cargo,
        Contraband,
        Parts,
        Fuel
    }

    /// <summary>
    /// One shared structured effect. <see cref="Count"/> is KillCrew / Load* / TakeCash amount
    /// (TakeCash uses dollars; KillCrew defaults to 1 when Count is 0).
    /// <see cref="Kind"/> is required for <see cref="CardEffectType.LoadUpTo"/>.
    /// </summary>
    public sealed class CardEffect
    {
        public CardEffectType Type { get; }
        public int Count { get; }
        public CardLoadKind? Kind { get; }

        public CardEffect(CardEffectType type, int count = 0, CardLoadKind? kind = null)
        {
            Type = type;
            Count = count;
            Kind = kind;
        }
    }

    /// <summary>
    /// Which resolver invoked the shared applicator — thin context for PendingChoice resume
    /// and any source-specific validation (e.g. Nav hold packing on Load).
    /// </summary>
    public enum CardEffectSource
    {
        Nav,
        Misbehave
    }

    /// <summary>Aggregated deltas from applying a list of <see cref="CardEffect"/>.</summary>
    public sealed class CardEffectApplyResult
    {
        public int WarrantsIssued { get; set; }
        public int CrewKilled { get; set; }
        public int CargoLoaded { get; set; }
        public int ContrabandLoaded { get; set; }
        public int PartsLoaded { get; set; }
        public int FuelLoaded { get; set; }
        public int CashGained { get; set; }
        public int MoralDisgruntled { get; set; }
        public int DisgruntledCleared { get; set; }

        /// <summary>All hold goods deltas (Cargo + Contraband + Parts + Fuel).</summary>
        public int GoodsLoaded => CargoLoaded + ContrabandLoaded + PartsLoaded + FuelLoaded;
    }

    /// <summary>
    /// Optional structured skill result band using the shared effect vocabulary.
    /// Used by Nav overlays; Misbehave bands may mix shared + local via <see cref="MisbehaveEffect"/>.
    /// </summary>
    public sealed class CardEffectBand
    {
        public int Min { get; }
        /// <summary>Inclusive max. Null means open-ended (printed N+).</summary>
        public int? Max { get; }
        public string? Text { get; }
        public IReadOnlyList<CardEffect> Effects { get; }

        public CardEffectBand(int min, int? max, string? text, IReadOnlyList<CardEffect>? effects)
        {
            Min = min;
            Max = max;
            Text = text;
            Effects = effects ?? Array.Empty<CardEffect>();
        }

        public bool Matches(int sum)
        {
            if (sum < Min)
                return false;
            return Max == null || sum <= Max.Value;
        }

        public static CardEffectBand? Pick(IReadOnlyList<CardEffectBand>? bands, int sum)
        {
            if (bands == null || bands.Count == 0)
                return null;
            CardEffectBand? picked = null;
            foreach (var band in bands)
            {
                if (band.Matches(sum))
                    picked = band;
            }
            return picked;
        }
    }

    /// <summary>Parse shared effect type names from JSON (camelCase / aliases).</summary>
    public static class CardEffectParsing
    {
        public static bool TryParseType(string raw, out CardEffectType type)
        {
            var key = Normalize(raw);
            foreach (CardEffectType candidate in Enum.GetValues(typeof(CardEffectType)))
            {
                if (candidate.ToString().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    type = candidate;
                    return true;
                }
            }
            if (key.Equals("warrant", StringComparison.OrdinalIgnoreCase))
            {
                type = CardEffectType.WarrantIssued;
                return true;
            }
            type = default;
            return false;
        }

        public static bool TryParseLoadKind(string? raw, out CardLoadKind kind)
        {
            var key = Normalize(raw ?? "");
            if (key.Equals("Cargo", StringComparison.OrdinalIgnoreCase))
            {
                kind = CardLoadKind.Cargo;
                return true;
            }
            if (key.Equals("Contraband", StringComparison.OrdinalIgnoreCase))
            {
                kind = CardLoadKind.Contraband;
                return true;
            }
            if (key.Equals("Part", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Parts", StringComparison.OrdinalIgnoreCase))
            {
                kind = CardLoadKind.Parts;
                return true;
            }
            if (key.Equals("Fuel", StringComparison.OrdinalIgnoreCase))
            {
                kind = CardLoadKind.Fuel;
                return true;
            }
            kind = default;
            return false;
        }

        /// <summary>
        /// Map exact typed Load* shared types to a <see cref="CardLoadKind"/> for up-to promotion.
        /// </summary>
        public static bool TryLoadKindFromExactType(CardEffectType type, out CardLoadKind kind)
        {
            switch (type)
            {
                case CardEffectType.LoadCargo:
                    kind = CardLoadKind.Cargo;
                    return true;
                case CardEffectType.LoadContraband:
                    kind = CardLoadKind.Contraband;
                    return true;
                case CardEffectType.LoadParts:
                    kind = CardLoadKind.Parts;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        public static bool IsSharedTypeName(string raw) => TryParseType(raw, out _);

        public static string Normalize(string raw) =>
            (raw ?? "").Trim().Replace("_", "").Replace("-", "");
    }
}
