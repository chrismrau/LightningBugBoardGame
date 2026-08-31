using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class ScenarioCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Duration { get; }
        public string? Audience { get; }
        public string? WinType { get; }
        public ScenarioCard(string id, string name, string? duration, string? audience, string? winType)
        {
            Id = id; Name = name; Duration = duration; Audience = audience; WinType = winType;
        }
    }

    public sealed class ScenarioCatalog
    {
        private readonly Dictionary<string, ScenarioCard> _byId;
        public IReadOnlyDictionary<string, ScenarioCard> Cards => _byId;
        public ScenarioCatalog(IEnumerable<ScenarioCard> cards)
        {
            _byId = new Dictionary<string, ScenarioCard>(StringComparer.Ordinal);
            foreach (var card in cards) _byId[card.Id] = card;
        }
        public ScenarioCard Get(string id) => _byId[id];
        public static ScenarioCatalog LoadDefault() => LoadFromFile(GameData.ScenarioCardsPath);
        public static ScenarioCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<ScenarioCard>();
            foreach (var card in doc.RootElement.GetProperty("scenarioCards").EnumerateArray())
            {
                string? winType = null;
                if (card.TryGetProperty("win", out var win) && win.TryGetProperty("type", out var type))
                    winType = type.GetString();
                list.Add(new ScenarioCard(
                    card.GetProperty("id").GetString() ?? "",
                    card.GetProperty("name").GetString() ?? "",
                    card.TryGetProperty("duration", out var d) ? d.GetString() : null,
                    card.TryGetProperty("audience", out var a) ? a.GetString() : null,
                    winType));
            }
            return new ScenarioCatalog(list);
        }
    }
}
