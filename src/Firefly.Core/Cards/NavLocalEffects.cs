using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Nav-only structured effects (fuel lose, seize unprotected Goods, etc.).
    /// Shared Kill / Warrant / Load / TakeCash / DisgruntleMoral / Clear* / LoadUpTo
    /// live on <see cref="CardEffectType"/> — do not duplicate them here.
    /// Plan S5 scaffold: migrate high-churn locals; prose adapters remain for the rest.
    /// </summary>
    public enum NavLocalEffectType
    {
        /// <summary>Lose / Discard N Fuel (count = N).</summary>
        LoseFuel,
        /// <summary>
        /// N Goods not in Stash are seized (count = N). Composition via
        /// <see cref="Actions.NavResolveChoice"/> SeizeGoods* fields / auto-pick.
        /// </summary>
        SeizeGoodsNotInStash
    }

    /// <summary>
    /// One Nav structured overlay effect: either a shared <see cref="CardEffect"/> or a
    /// Nav-local type. Mirrors <see cref="MisbehaveEffect"/>.
    /// </summary>
    public sealed class NavEffect
    {
        public CardEffect? Shared { get; }
        public NavLocalEffectType? Local { get; }
        public int Count { get; }

        public bool IsShared => Shared != null;
        public bool IsLocal => Local != null;

        /// <summary>Shared type when <see cref="IsShared"/>; else default.</summary>
        public CardEffectType Type => Shared?.Type ?? default;

        /// <summary>Shared LoadUpTo kind when present.</summary>
        public CardLoadKind? Kind => Shared?.Kind;

        private NavEffect(CardEffect? shared, NavLocalEffectType? local, int count)
        {
            Shared = shared;
            Local = local;
            Count = shared?.Count ?? count;
        }

        public static NavEffect Of(CardEffectType type, int count = 0) =>
            new NavEffect(new CardEffect(type, count), null, count);

        public static NavEffect Of(CardEffect shared) =>
            new NavEffect(shared ?? throw new ArgumentNullException(nameof(shared)), null, shared.Count);

        public static NavEffect Of(NavLocalEffectType type, int count = 0) =>
            new NavEffect(null, type, count);

        public bool Is(CardEffectType type) => Shared != null && Shared.Type == type;

        public bool Is(NavLocalEffectType type) => Local == type;
    }

    /// <summary>
    /// Structured Nav skill-band overlay using mixed shared + Nav-local effects.
    /// </summary>
    public sealed class NavBand
    {
        public int Min { get; }
        /// <summary>Inclusive max. Null means open-ended (printed N+).</summary>
        public int? Max { get; }
        public string? Text { get; }
        public IReadOnlyList<NavEffect> Effects { get; }

        public NavBand(int min, int? max, string? text, IReadOnlyList<NavEffect>? effects)
        {
            Min = min;
            Max = max;
            Text = text;
            Effects = effects ?? Array.Empty<NavEffect>();
        }

        public bool Matches(int sum)
        {
            if (sum < Min)
                return false;
            return Max == null || sum <= Max.Value;
        }

        public static NavBand? Pick(IReadOnlyList<NavBand>? bands, int sum)
        {
            if (bands == null || bands.Count == 0)
                return null;
            NavBand? picked = null;
            foreach (var band in bands)
            {
                if (band.Matches(sum))
                    picked = band;
            }
            return picked;
        }
    }
}
