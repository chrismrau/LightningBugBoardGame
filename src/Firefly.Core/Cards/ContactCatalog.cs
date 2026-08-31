using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class ContactCatalog
    {
        private readonly Dictionary<string, ContactCard> _byId;
        private readonly Dictionary<string, ContactCard> _byName;

        public IReadOnlyDictionary<string, ContactCard> Cards => _byId;

        public ContactCatalog(IEnumerable<ContactCard> cards)
        {
            _byId = new Dictionary<string, ContactCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, ContactCard>(StringComparer.Ordinal);
            foreach (var card in cards)
            {
                _byId[card.Id] = card;
                _byName[ContactNames.Normalize(card.Name)] = card;
            }
        }

        public ContactCard Get(string id) => _byId[id];

        public bool TryGet(string id, out ContactCard card) => _byId.TryGetValue(id, out card!);

        public bool TryFindByName(string name, out ContactCard card)
        {
            card = null!;
            var key = ContactNames.Normalize(name);
            if (_byName.TryGetValue(key, out card!))
                return true;
            if (_byId.TryGetValue(name, out card!))
                return true;
            return false;
        }

        public static ContactCatalog LoadDefault() => LoadFromFile(GameData.ContactsPath);

        public static ContactCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<ContactFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Contacts.json did not deserialize.");

            var cards = new List<ContactCard>();
            foreach (var dto in file.Contacts ?? new List<ContactDto>())
            {
                cards.Add(new ContactCard(
                    dto.Id,
                    dto.Name ?? dto.Id,
                    dto.Location?.Planet,
                    dto.Location?.System,
                    dto.SolidAbility?.Name,
                    dto.SolidAbility?.Text,
                    ToPrices(dto.BuyPrices),
                    ToPrices(dto.SellPrices),
                    dto.HandSizeBonus,
                    dto.ActiveJobsBonus));
            }

            return new ContactCatalog(cards);
        }

        private static ContactPrices? ToPrices(PriceDto? dto)
        {
            if (dto == null)
                return null;
            if (dto.Contraband == null && dto.Cargo == null && dto.Fuel == null)
                return null;
            return new ContactPrices(dto.Contraband, dto.Cargo, dto.Fuel);
        }

        private sealed class ContactFile { public List<ContactDto>? Contacts { get; set; } }
        private sealed class ContactDto
        {
            public string Id { get; set; } = "";
            public string? Name { get; set; }
            public LocDto? Location { get; set; }
            public AbilityDto? SolidAbility { get; set; }
            public PriceDto? BuyPrices { get; set; }
            public PriceDto? SellPrices { get; set; }
            public int? HandSizeBonus { get; set; }
            public int? ActiveJobsBonus { get; set; }
        }
        private sealed class LocDto { public string? Planet { get; set; } public string? System { get; set; } }
        private sealed class AbilityDto { public string? Name { get; set; } public string? Text { get; set; } }
        private sealed class PriceDto { public int? Contraband { get; set; } public int? Cargo { get; set; } public int? Fuel { get; set; } }
    }
}
