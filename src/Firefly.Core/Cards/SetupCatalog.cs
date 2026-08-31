using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class SetupCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Audience { get; }
        public string? TimeModifier { get; }
        public int? StartingCash { get; }
        public int? StartingFuel { get; }
        public int? StartingParts { get; }

        public SetupCard(string id, string name, string? audience, string? timeModifier, int? cash, int? fuel, int? parts)
        {
            Id = id; Name = name; Audience = audience; TimeModifier = timeModifier;
            StartingCash = cash; StartingFuel = fuel; StartingParts = parts;
        }
    }

    public sealed class SetupCatalog
    {
        private readonly Dictionary<string, SetupCard> _byId;
        public IReadOnlyDictionary<string, SetupCard> Cards => _byId;
        public SetupCatalog(IEnumerable<SetupCard> cards)
        {
            _byId = new Dictionary<string, SetupCard>(StringComparer.Ordinal);
            foreach (var card in cards) _byId[card.Id] = card;
        }
        public SetupCard Get(string id) => _byId[id];
        public static SetupCatalog LoadDefault() => LoadFromFile(GameData.SetupCardsPath);
        public static SetupCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<SetupCard>();
            foreach (var card in doc.RootElement.GetProperty("setupCards").EnumerateArray())
            {
                int? cash = null, fuel = null, parts = null;
                if (card.TryGetProperty("startingSupplies", out var supplies))
                {
                    if (supplies.TryGetProperty("cash", out var c)) cash = c.GetInt32();
                    if (supplies.TryGetProperty("fuel", out var f)) fuel = f.GetInt32();
                    if (supplies.TryGetProperty("parts", out var p)) parts = p.GetInt32();
                }
                list.Add(new SetupCard(
                    card.GetProperty("id").GetString() ?? "",
                    card.GetProperty("name").GetString() ?? "",
                    card.TryGetProperty("audience", out var a) ? a.GetString() : null,
                    card.TryGetProperty("timeModifier", out var t) ? t.GetString() : null,
                    cash, fuel, parts));
            }
            return new SetupCatalog(list);
        }
    }
}
