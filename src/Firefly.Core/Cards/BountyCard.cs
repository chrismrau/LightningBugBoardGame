namespace Firefly.Core.Cards
{
    public enum BountyKind
    {
        Wanted,
        CortexAlert
    }

    public sealed class BountyCard
    {
        public string Id { get; }
        public string Name { get; }
        public BountyKind Kind { get; }
        public bool Immoral { get; }
        public string? PickupPlanet { get; }
        public string? DropoffPlanet { get; }
        public int BotchKill { get; }
        public int Pay { get; }
        public string? Description { get; }

        public bool IsCortex => Kind == BountyKind.CortexAlert;

        public BountyCard(
            string id,
            string name,
            BountyKind kind,
            bool immoral,
            string? pickupPlanet,
            string? dropoffPlanet,
            int botchKill,
            int pay,
            string? description)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Immoral = immoral;
            PickupPlanet = pickupPlanet;
            DropoffPlanet = dropoffPlanet;
            BotchKill = botchKill < 0 ? 0 : botchKill;
            Pay = pay;
            Description = description;
        }

        public bool MatchesCrewName(string crewName)
        {
            if (string.IsNullOrWhiteSpace(crewName))
                return false;
            if (string.Equals(Name, crewName, System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (!IsCortex)
                return false;
            if (Name.Equals("Enforcers", System.StringComparison.OrdinalIgnoreCase))
                return crewName.Equals("Enforcer", System.StringComparison.OrdinalIgnoreCase);
            if (Name.Equals("Scrappers", System.StringComparison.OrdinalIgnoreCase))
                return crewName.Equals("Scrapper", System.StringComparison.OrdinalIgnoreCase);
            if (Name.Equals("Bandits", System.StringComparison.OrdinalIgnoreCase))
                return crewName.Equals("Bandit", System.StringComparison.OrdinalIgnoreCase);
            return false;
        }
    }
}
