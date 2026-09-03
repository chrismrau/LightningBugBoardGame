using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class GearEntry
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> Keywords { get; }
        public GearEntry(string id, string name, IReadOnlyList<string> keywords)
        {
            Id = id; Name = name; Keywords = keywords;
        }
    }

    public sealed class GearIndex
    {
        private readonly Dictionary<string, GearEntry> _byId;
        public IReadOnlyDictionary<string, GearEntry> Items => _byId;
        public GearIndex(IEnumerable<GearEntry> items)
        {
            _byId = new Dictionary<string, GearEntry>(StringComparer.Ordinal);
            foreach (var item in items) _byId[item.Id] = item;
        }
        public bool TryGet(string id, out GearEntry entry) => _byId.TryGetValue(id, out entry!);

        public static GearIndex LoadDefault()
        {
            var path = GameData.GearPath;
            var items = new List<GearEntry>();
            if (!File.Exists(path)) return new GearIndex(items);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("gear", out var array)) return new GearIndex(items);
            foreach (var node in array.EnumerateArray())
            {
                var id = node.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
                var name = node.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                var keywords = new List<string>();
                if (node.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in kw.EnumerateArray())
                    {
                        var text = item.GetString();
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var cut = text.IndexOf(':');
                        keywords.Add(cut > 0 ? text.Substring(0, cut) : text);
                    }
                }
                items.Add(new GearEntry(id, name, keywords));
            }
            return new GearIndex(items);
        }
    }
}
