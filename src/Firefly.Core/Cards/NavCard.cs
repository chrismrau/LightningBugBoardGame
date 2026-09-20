using System;
using System.Collections.Generic;
using Firefly.Core.Map;

namespace Firefly.Core.Cards
{
    public enum FlightOutcome
    {
        KeepFlying,
        FullStop,
        Evade,
        Conditional
    }

    public sealed class NavOption
    {
        public string? Name { get; }
        /// <summary>
        /// Printed prose — display and durable source of truth. Structured fields are an overlay.
        /// </summary>
        public string Details { get; }
        public FlightOutcome Outcome { get; }
        /// <summary>Optional structured skill test; preferred over parsing <see cref="Details"/>.</summary>
        public MisbehaveSkillCheckSpec? SkillCheck { get; }
        /// <summary>
        /// Optional structured result bands using the shared <see cref="CardEffect"/> vocabulary.
        /// Preferred over regex band selection when present.
        /// </summary>
        public IReadOnlyList<CardEffectBand> Bands { get; }
        /// <summary>
        /// Optional option-level shared effects when there is no skill band
        /// (e.g. Disgruntle Moral with Keep Flying).
        /// </summary>
        public IReadOnlyList<CardEffect> Effects { get; }

        public bool HasStructuredBands => Bands.Count > 0;
        public bool HasStructuredEffects => Effects.Count > 0;

        public NavOption(
            string? name,
            string details,
            FlightOutcome outcome,
            MisbehaveSkillCheckSpec? skillCheck = null,
            IReadOnlyList<CardEffectBand>? bands = null,
            IReadOnlyList<CardEffect>? effects = null)
        {
            Name = name;
            Details = details ?? "";
            Outcome = outcome;
            SkillCheck = skillCheck;
            Bands = bands ?? Array.Empty<CardEffectBand>();
            Effects = effects ?? Array.Empty<CardEffect>();
        }
    }

    public sealed class NavCard
    {
        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public bool IsReshuffle { get; }
        public IReadOnlyList<NavOption> Options { get; }

        public NavCard(string id, string name, string type, bool isReshuffle, IReadOnlyList<NavOption> options)
        {
            Id = id;
            Name = name;
            Type = type;
            IsReshuffle = isReshuffle;
            Options = options ?? Array.Empty<NavOption>();
        }

        public static FlightOutcome ParseOutcome(string? details)
        {
            var text = details ?? "";
            var keep = ContainsIgnoreCase(text, "Keep Flying");
            var stop = ContainsIgnoreCase(text, "Full Stop");
            var evade = ContainsIgnoreCase(text, "Evade");
            if (keep && stop)
                return FlightOutcome.Conditional;
            if (stop)
                return FlightOutcome.FullStop;
            if (evade && !keep)
                return FlightOutcome.Evade;
            return FlightOutcome.KeepFlying;
        }

        private static bool ContainsIgnoreCase(string text, string value) =>
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class DrawnNav
    {
        public NavCard Card { get; }
        public NavRegion Region { get; }
        public string SectorId { get; }

        public DrawnNav(NavCard card, NavRegion region, string sectorId)
        {
            Card = card;
            Region = region;
            SectorId = sectorId;
        }
    }
}
