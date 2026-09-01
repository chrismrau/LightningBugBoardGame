using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public enum SupplyKind
    {
        Gear,
        Crew,
        ShipUpgrade,
        DriveCore
    }

    public sealed class SupplyCard
    {
        public string Id { get; }
        public string Name { get; }
        public int Cost { get; }
        public SupplyKind Kind { get; }
        public IReadOnlyDictionary<string, int> CopiesByPlanet { get; }

        public SupplyCard(
            string id,
            string name,
            int cost,
            SupplyKind kind,
            IReadOnlyDictionary<string, int>? copiesByPlanet = null)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Kind = kind;
            CopiesByPlanet = copiesByPlanet ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public int CopiesAt(string planet)
        {
            if (string.IsNullOrWhiteSpace(planet))
                return 0;
            return CopiesByPlanet.TryGetValue(planet, out var n) ? n : 0;
        }
    }
}
