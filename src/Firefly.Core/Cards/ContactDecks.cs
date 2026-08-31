using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class ContactDeck
    {
        private readonly List<JobCard> _draw = new List<JobCard>();

        public string ContactName { get; }
        public int Count => _draw.Count;

        public ContactDeck(string contactName, IEnumerable<JobCard> jobs, IRng rng)
        {
            ContactName = contactName;
            _draw.AddRange(jobs);
            SystemRng.Shuffle(_draw, rng);
        }

        public IReadOnlyList<JobCard> DrawConsider(int count)
        {
            var taken = new List<JobCard>();
            var n = Math.Min(count, _draw.Count);
            for (var i = 0; i < n; i++)
            {
                taken.Add(_draw[0]);
                _draw.RemoveAt(0);
            }
            return taken;
        }

        public void PutOnBottom(JobCard job) => _draw.Add(job);

        public void PutOnBottom(IEnumerable<JobCard> jobs)
        {
            foreach (var job in jobs)
                PutOnBottom(job);
        }
    }

    public sealed class ContactDecks
    {
        private readonly Dictionary<string, ContactDeck> _decks =
            new Dictionary<string, ContactDeck>(StringComparer.Ordinal);

        public ContactDecks(JobCatalog jobs, IRng rng)
        {
            var grouped = new Dictionary<string, List<JobCard>>(StringComparer.Ordinal);
            foreach (var card in jobs.Cards.Values)
            {
                var key = ContactNames.Normalize(card.ContactName);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<JobCard>();
                    grouped[key] = list;
                }
                list.Add(card);
            }

            foreach (var kv in grouped)
                _decks[kv.Key] = new ContactDeck(kv.Value[0].ContactName, kv.Value, rng);
        }

        public bool TryGet(string contactName, out ContactDeck deck) =>
            _decks.TryGetValue(ContactNames.Normalize(contactName), out deck!);
    }
}
