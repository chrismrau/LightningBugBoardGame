using System;
using System.Collections.Generic;
using Firefly.Core.Map;

namespace Firefly.Core.Cards
{
    public sealed class NavDeck
    {
        private readonly List<NavCard> _draw;
        private readonly List<NavCard> _discard;
        private readonly IRng _rng;

        public int DrawCount => _draw.Count;
        public int DiscardCount => _discard.Count;

        public NavDeck(IEnumerable<NavCard> cards, IRng rng)
        {
            _draw = new List<NavCard>(cards);
            _discard = new List<NavCard>();
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            SystemRng.Shuffle(_draw, _rng);
        }

        public NavCard Draw()
        {
            if (_draw.Count == 0)
                ReshuffleDiscardIntoDraw();
            if (_draw.Count == 0)
                throw new InvalidOperationException("Nav deck is empty.");

            var last = _draw.Count - 1;
            var card = _draw[last];
            _draw.RemoveAt(last);
            return card;
        }

        public void ResolveIntoDiscard(NavCard card)
        {
            _discard.Add(card);
            if (card.IsReshuffle)
                ReshuffleDiscardIntoDraw();
        }

        public void PlaceOnTop(NavCard card) => _draw.Add(card);

        private void ReshuffleDiscardIntoDraw()
        {
            _draw.AddRange(_discard);
            _discard.Clear();
            SystemRng.Shuffle(_draw, _rng);
        }
    }

    public sealed class NavDecks
    {
        public NavDeck Alliance { get; }
        public NavDeck Border { get; }
        public NavDeck Rim { get; }
        public NavCatalog Catalog { get; }

        public NavDecks(NavDeck alliance, NavDeck border, NavDeck rim, NavCatalog catalog)
        {
            Alliance = alliance;
            Border = border;
            Rim = rim;
            Catalog = catalog;
        }

        public NavDeck For(NavRegion region) =>
            region == NavRegion.Alliance ? Alliance
            : region == NavRegion.Border ? Border
            : Rim;
    }
}
