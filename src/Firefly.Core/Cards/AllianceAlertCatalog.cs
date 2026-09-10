using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class AllianceAlertCatalog
    {
        private readonly Dictionary<string, AllianceAlertCard> _byId;
        private readonly Dictionary<string, AllianceAlertCard> _byName;

        public IReadOnlyDictionary<string, AllianceAlertCard> Cards => _byId;

        public AllianceAlertCatalog(IEnumerable<AllianceAlertCard> cards)
        {
            _byId = new Dictionary<string, AllianceAlertCard>(StringComparer.Ordinal);
            _byName = new Dictionary<string, AllianceAlertCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
            {
                _byId[card.Id] = card;
                _byName[card.Name] = card;
            }
        }

        public AllianceAlertCard Get(string id) => _byId[id];

        public bool TryGet(string id, out AllianceAlertCard card) => _byId.TryGetValue(id, out card!);

        public bool TryResolve(string idOrName, out AllianceAlertCard card)
        {
            if (TryGet(idOrName, out card))
                return true;
            return _byName.TryGetValue(idOrName, out card!);
        }

        public static AllianceAlertCatalog LoadDefault() => LoadFromFile(GameData.AllianceAlertsPath);

        public static AllianceAlertCatalog LoadFromFile(string path)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<AllianceAlertFile>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException("AllianceAlerts.json did not deserialize.");

            var cards = new List<AllianceAlertCard>();
            foreach (var dto in file.AllianceAlerts ?? new List<AllianceAlertDto>())
            {
                cards.Add(new AllianceAlertCard(
                    dto.Id ?? Slug(dto.Name),
                    dto.Name ?? dto.Id ?? "alert",
                    dto.Source,
                    dto.SourceLabel,
                    dto.Detail,
                    dto.SystemWide,
                    dto.SystemWideText));
            }
            return new AllianceAlertCatalog(cards);
        }

        private static string Slug(string? name) =>
            "alert_" + (name ?? "unknown").Trim().ToLowerInvariant().Replace(' ', '-');

        private sealed class AllianceAlertFile
        {
            public List<AllianceAlertDto>? AllianceAlerts { get; set; }
        }

        private sealed class AllianceAlertDto
        {
            public string? Id { get; set; }
            public string? Source { get; set; }
            public string? SourceLabel { get; set; }
            public string? Name { get; set; }
            public string? Detail { get; set; }
            public bool SystemWide { get; set; }
            public string? SystemWideText { get; set; }
        }
    }
}
