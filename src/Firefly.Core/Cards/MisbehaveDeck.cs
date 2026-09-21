using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class MisbehaveDeck
    {
        private readonly List<MisbehaveCard> _draw;
        private readonly List<MisbehaveCard> _discard;
        private readonly IRng _rng;

        public MisbehaveCatalog Catalog { get; }
        public int DrawCount => _draw.Count;
        public int DiscardCount => _discard.Count;

        public MisbehaveDeck(IEnumerable<MisbehaveCard> cards, IRng rng, MisbehaveCatalog? catalog = null)
        {
            _draw = new List<MisbehaveCard>(cards);
            _discard = new List<MisbehaveCard>();
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Catalog = catalog ?? new MisbehaveCatalog(_draw);
            SystemRng.Shuffle(_draw, _rng);
        }

        public static MisbehaveDeck FromCatalog(MisbehaveCatalog catalog, IRng rng) =>
            new MisbehaveDeck(catalog.Cards.Values, rng, catalog);

        public MisbehaveCard Draw()
        {
            if (_draw.Count == 0) ReshuffleDiscardIntoDraw();
            if (_draw.Count == 0) throw new InvalidOperationException("Misbehave deck is empty.");
            var last = _draw.Count - 1;
            var card = _draw[last];
            _draw.RemoveAt(last);
            return card;
        }

        public void ResolveIntoDiscard(MisbehaveCard card)
        {
            _discard.Add(card);
            if (card.IsReshuffle) ReshuffleDiscardIntoDraw();
        }

        public void PlaceOnTop(MisbehaveCard card) => _draw.Add(card);

        /// <summary>Peek the top <paramref name="count"/> cards without removing (draw pile end = top).</summary>
        public IReadOnlyList<MisbehaveCard> PeekTop(int count)
        {
            var list = new List<MisbehaveCard>();
            for (var i = 0; i < count && i < _draw.Count; i++)
                list.Add(_draw[_draw.Count - 1 - i]);
            return list;
        }

        /// <summary>
        /// Replace the top N cards in the given order (first id becomes new top).
        /// Ids must be a permutation of the current top N.
        /// </summary>
        public bool TryReorderTop(IEnumerable<string> orderedIds, out string? error)
        {
            error = null;
            if (orderedIds == null)
            {
                error = "Misbehave reorder requires card ids.";
                return false;
            }
            var idList = new List<string>(orderedIds);
            if (idList.Count == 0)
            {
                error = "Misbehave reorder requires card ids.";
                return false;
            }
            var n = idList.Count;
            if (_draw.Count < n)
            {
                error = "Misbehave deck does not have enough cards.";
                return false;
            }

            var top = new List<MisbehaveCard>();
            for (var i = 0; i < n; i++)
                top.Add(_draw[_draw.Count - 1 - i]);

            var byId = new Dictionary<string, MisbehaveCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in top)
            {
                if (!byId.TryAdd(card.Id, card))
                {
                    error = $"Duplicate Misbehave id '{card.Id}' in top.";
                    return false;
                }
            }

            var rebuilt = new List<MisbehaveCard>();
            foreach (var id in orderedIds)
            {
                if (!byId.TryGetValue(id, out var card))
                {
                    error = $"Misbehave id '{id}' is not in the top {n}.";
                    return false;
                }
                rebuilt.Add(card);
                byId.Remove(card.Id);
            }
            if (byId.Count != 0)
            {
                error = "Misbehave reorder must include every top card exactly once.";
                return false;
            }

            _draw.RemoveRange(_draw.Count - n, n);
            // Place so rebuilt[0] is on top (end of list).
            for (var i = rebuilt.Count - 1; i >= 0; i--)
                _draw.Add(rebuilt[i]);
            return true;
        }

        private void ReshuffleDiscardIntoDraw()
        {
            _draw.AddRange(_discard);
            _discard.Clear();
            SystemRng.Shuffle(_draw, _rng);
        }
    }
}
