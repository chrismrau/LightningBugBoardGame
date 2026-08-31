using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class JobCatalog
    {
        private readonly Dictionary<string, JobCard> _byId;
        private readonly Dictionary<string, List<JobCard>> _byContact;

        public IReadOnlyDictionary<string, JobCard> Cards => _byId;

        public JobCatalog(IEnumerable<JobCard> cards)
        {
            _byId = new Dictionary<string, JobCard>(StringComparer.Ordinal);
            _byContact = new Dictionary<string, List<JobCard>>(StringComparer.Ordinal);
            foreach (var card in cards)
            {
                _byId[card.Id] = card;
                var key = ContactNames.Normalize(card.ContactName);
                if (!_byContact.TryGetValue(key, out var list))
                {
                    list = new List<JobCard>();
                    _byContact[key] = list;
                }
                list.Add(card);
            }
        }

        public JobCard Get(string id) => _byId[id];

        public bool TryGet(string id, out JobCard card) => _byId.TryGetValue(id, out card!);

        public IReadOnlyList<JobCard> ForContact(string contactName)
        {
            var key = ContactNames.Normalize(contactName);
            return _byContact.TryGetValue(key, out var list) ? list : Array.Empty<JobCard>();
        }

        public static JobCatalog LoadDefault() => LoadFromFile(GameData.JobsPath);

        public static JobCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<JobFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("Jobs.json did not deserialize.");

            var cards = new List<JobCard>();
            foreach (var dto in file.Jobs ?? new List<JobDto>())
            {
                cards.Add(new JobCard(
                    dto.Id,
                    dto.Name ?? dto.Id,
                    dto.Contact ?? "",
                    dto.JobType,
                    dto.Legal,
                    dto.Immoral,
                    dto.Pickup?.Location,
                    dto.Pickup?.Details,
                    dto.Dropoff?.Location,
                    dto.Dropoff?.Details,
                    dto.Pay?.Base,
                    dto.Pay?.Raw,
                    dto.Bonus,
                    dto.Special,
                    dto.Description));
            }

            return new JobCatalog(cards);
        }

        private sealed class JobFile
        {
            public List<JobDto>? Jobs { get; set; }
        }

        private sealed class JobDto
        {
            public string Id { get; set; } = "";
            public string? Name { get; set; }
            public string? Contact { get; set; }
            public string? JobType { get; set; }
            public bool Legal { get; set; }
            public bool Immoral { get; set; }
            public PlaceDto? Pickup { get; set; }
            public PlaceDto? Dropoff { get; set; }
            public PayDto? Pay { get; set; }
            public string? Bonus { get; set; }
            public string? Special { get; set; }
            public string? Description { get; set; }
        }

        private sealed class PlaceDto
        {
            public string? Location { get; set; }
            public string? Details { get; set; }
        }

        private sealed class PayDto
        {
            public int? Base { get; set; }
            public string? Raw { get; set; }
        }
    }
}
