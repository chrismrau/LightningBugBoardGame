namespace Firefly.Core.Cards
{
    public sealed class ContactPrices
    {
        public int? Contraband { get; }
        public int? Cargo { get; }
        public int? Fuel { get; }

        public ContactPrices(int? contraband, int? cargo, int? fuel)
        {
            Contraband = contraband;
            Cargo = cargo;
            Fuel = fuel;
        }
    }

    public sealed class ContactCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Planet { get; }
        public string? System { get; }
        public string? SolidAbilityName { get; }
        public string? SolidAbilityText { get; }
        public ContactPrices? BuyPrices { get; }
        public ContactPrices? SellPrices { get; }
        public int? HandSizeBonus { get; }
        public int? ActiveJobsBonus { get; }
        public bool IsHarken { get; }
        public bool IsMrUniverse { get; }
        public bool IsPatience { get; }
        public bool IsHiggins { get; }
        public bool IsBadger { get; }
        public bool IsAmnon { get; }
        public bool IsFanty { get; }
        public bool IsHarrow { get; }
        public bool IsNiska { get; }

        public ContactCard(
            string id,
            string name,
            string? planet,
            string? system,
            string? solidAbilityName,
            string? solidAbilityText,
            ContactPrices? buyPrices,
            ContactPrices? sellPrices,
            int? handSizeBonus,
            int? activeJobsBonus)
        {
            Id = id;
            Name = name;
            Planet = planet;
            System = system;
            SolidAbilityName = solidAbilityName;
            SolidAbilityText = solidAbilityText;
            BuyPrices = buyPrices;
            SellPrices = sellPrices;
            HandSizeBonus = handSizeBonus;
            ActiveJobsBonus = activeJobsBonus;
            IsHarken = ContactNames.EqualsName(name, "Harken");
            IsMrUniverse = ContactNames.EqualsName(name, "Mr. Universe")
                || ContactNames.EqualsName(name, "Mr Universe");
            IsPatience = ContactNames.EqualsName(name, "Patience");
            IsHiggins = ContactNames.EqualsName(name, "Magistrate Higgins")
                || ContactNames.EqualsName(name, "Higgins");
            IsBadger = ContactNames.EqualsName(name, "Badger");
            IsAmnon = ContactNames.EqualsName(name, "Amnon Duul")
                || ContactNames.EqualsName(name, "Amnon");
            IsFanty = ContactNames.EqualsName(name, "Fanty & Mingo")
                || ContactNames.EqualsName(name, "Fanty and Mingo")
                || ContactNames.EqualsName(name, "Fanty");
            IsHarrow = ContactNames.EqualsName(name, "Lord Harrow")
                || ContactNames.EqualsName(name, "Harrow");
            IsNiska = ContactNames.EqualsName(name, "Niska");
        }
    }
}
