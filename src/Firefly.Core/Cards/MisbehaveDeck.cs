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

        private void ReshuffleDiscardIntoDraw()
        {
            _draw.AddRange(_discard);
            _discard.Clear();
            SystemRng.Shuffle(_draw, _rng);
        }
    }
}
