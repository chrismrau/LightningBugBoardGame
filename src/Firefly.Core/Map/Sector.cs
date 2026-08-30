using System.Collections.Generic;

namespace Firefly.Core.Map
{
    public sealed class Sector
    {
        public string Id { get; }
        public string Region { get; }
        public string Zone { get; }
        public int Ring { get; }
        public int Index { get; }
        public string? Planet { get; }
        public string? Contact { get; }
        public string? DisplayName { get; }
        public bool HasSupplyDeck { get; }
        public bool IsPlanetary { get; }
        public bool IsRelay { get; }
        public IReadOnlyList<string> Aliases { get; }
        public IReadOnlyList<string> DestinationRegions { get; }

        public Sector(
            string id,
            string region,
            string zone,
            int ring,
            int index,
            string? planet,
            string? contact,
            string? displayName,
            bool hasSupplyDeck,
            bool isPlanetary,
            bool isRelay,
            IReadOnlyList<string>? aliases = null,
            IReadOnlyList<string>? destinationRegions = null)
        {
            Id = id;
            Region = region;
            Zone = zone;
            Ring = ring;
            Index = index;
            Planet = planet;
            Contact = contact;
            DisplayName = displayName;
            HasSupplyDeck = hasSupplyDeck;
            IsPlanetary = isPlanetary;
            IsRelay = isRelay;
            Aliases = aliases ?? new List<string>();
            DestinationRegions = destinationRegions ?? new List<string>();
        }

        public NavRegion NavRegion =>
            Region.Equals("Alliance", System.StringComparison.OrdinalIgnoreCase) ? NavRegion.Alliance
            : Region.Equals("Border", System.StringComparison.OrdinalIgnoreCase) ? NavRegion.Border
            : NavRegion.Rim;
    }

    public enum NavRegion
    {
        Alliance,
        Border,
        Rim
    }
}
