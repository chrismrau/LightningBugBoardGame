using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Shared card-effect vocabulary understood by both Nav and Misbehave applicators.
    /// Intersection only — Nav-only / Misbehave-only effects stay in local adapters.
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
        ClearDisgruntled
    }

    /// <summary>
    /// One shared structured effect. <see cref="Count"/> is KillCrew / Load* / TakeCash amount
    /// (TakeCash uses dollars; KillCrew defaults to 1 when Count is 0).
    /// </summary>
    public sealed class CardEffect
    {
        public CardEffectType Type { get; }
        public int Count { get; }

        public CardEffect(CardEffectType type, int count = 0)
        {
            Type = type;
            Count = count;
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
        public int CashGained { get; set; }
        public int MoralDisgruntled { get; set; }
        public int DisgruntledCleared { get; set; }

        public int GoodsLoaded => CargoLoaded + ContrabandLoaded;
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

        public static bool IsSharedTypeName(string raw) => TryParseType(raw, out _);

        public static string Normalize(string raw) =>
            (raw ?? "").Trim().Replace("_", "").Replace("-", "");
    }
}
