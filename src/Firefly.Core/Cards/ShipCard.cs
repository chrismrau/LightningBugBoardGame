namespace Firefly.Core.Cards
{
    public sealed class ShipCard
    {
        public string Id { get; }
        public string Name { get; }
        public string Source { get; }
        public string ShipClass { get; }
        public string MainDrive { get; }
        public int Cost { get; }
        public bool CanUpgradeDrive { get; }
        public int CargoHolds { get; }
        public int Stash { get; }
        public int FuelStash { get; }
        public int MaxCrew { get; }
        public int UpgradeSlots { get; }
        public string? SpecialRules { get; }

        public ShipCard(
            string id,
            string name,
            string source,
            string shipClass,
            string mainDrive,
            int cost,
            bool canUpgradeDrive,
            int cargoHolds,
            int stash,
            int fuelStash,
            int maxCrew,
            int upgradeSlots,
            string? specialRules)
        {
            Id = id;
            Name = name;
            Source = source ?? "";
            ShipClass = shipClass ?? "";
            MainDrive = mainDrive ?? "";
            Cost = cost;
            CanUpgradeDrive = canUpgradeDrive;
            CargoHolds = cargoHolds;
            Stash = stash;
            FuelStash = fuelStash;
            MaxCrew = maxCrew > 0 ? maxCrew : 6;
            UpgradeSlots = upgradeSlots;
            SpecialRules = specialRules;
        }

        public bool IsCoreFirefly =>
            string.Equals(Source, "Core", System.StringComparison.OrdinalIgnoreCase)
            && ShipClass.IndexOf("Firefly", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
