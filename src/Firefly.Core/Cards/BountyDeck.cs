using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Pirates &amp; Bounty Hunters: the Bounty deck and the 3-card
    /// 'Verse's Most Wanted List.
    /// </summary>
    public sealed class BountyDeck
    {
        public const int WantedCount = 3;

        private readonly List<BountyCard> _draw;
        private readonly List<BountyCard> _faceUp;
        private readonly List<BountyCard> _outOfGame;
        private readonly IRng _rng;

        public BountyCatalog Catalog { get; }
        public IReadOnlyList<BountyCard> FaceUp => _faceUp;
        public int DrawCount => _draw.Count;
        public int OutOfGameCount => _outOfGame.Count;

        public BountyDeck(IEnumerable<BountyCard> cards, IRng rng, BountyCatalog? catalog = null)
        {
            _draw = new List<BountyCard>(cards);
            _faceUp = new List<BountyCard>();
            _outOfGame = new List<BountyCard>();
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Catalog = catalog ?? new BountyCatalog(_draw);
            SystemRng.Shuffle(_draw, _rng);
            RefillWanted();
        }

        public static BountyDeck FromCatalog(BountyCatalog catalog, IRng rng) =>
            new BountyDeck(catalog.Cards.Values, rng, catalog);

        public BountyCard? FindWanted(string idOrName)
        {
            foreach (var card in _faceUp)
            {
                if (string.Equals(card.Id, idOrName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(card.Name, idOrName, StringComparison.OrdinalIgnoreCase))
                    return card;
            }
            return null;
        }

        public bool TryClaimWanted(string idOrName, out BountyCard card)
        {
            card = null!;
            for (var i = 0; i < _faceUp.Count; i++)
            {
                var face = _faceUp[i];
                if (string.Equals(face.Id, idOrName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(face.Name, idOrName, StringComparison.OrdinalIgnoreCase))
                {
                    card = face;
                    _faceUp.RemoveAt(i);
                    RefillWanted();
                    return true;
                }
            }
            return false;
        }

        public void ReturnToBottom(BountyCard card)
        {
            if (card != null)
                _draw.Insert(0, card);
            RefillWanted();
        }

        public void RemoveFromGame(BountyCard card)
        {
            if (card == null)
                return;
            _faceUp.Remove(card);
            _draw.Remove(card);
            _outOfGame.Add(card);
            RefillWanted();
        }

        /// <summary>
        /// Alliance Cruiser Nav: return the 3 face-up bounties to the bottom
        /// and reveal 3 new ones. Cull cards whose target is out of play.
        /// </summary>
        public void CycleWantedList(ISet<string>? removedNames = null)
        {
            while (_faceUp.Count > 0)
            {
                var card = _faceUp[0];
                _faceUp.RemoveAt(0);
                _draw.Insert(0, card);
            }
            RefillWanted();
            CullRemoved(removedNames);
        }

        public void CullRemoved(ISet<string>? removedNames)
        {
            if (removedNames == null || removedNames.Count == 0)
                return;
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = _faceUp.Count - 1; i >= 0; i--)
                {
                    var card = _faceUp[i];
                    if (card.IsCortex)
                        continue;
                    if (!removedNames.Contains(card.Name))
                        continue;
                    _faceUp.RemoveAt(i);
                    _outOfGame.Add(card);
                    changed = true;
                }
                RefillWanted();
            }
        }

        public void RefillWanted()
        {
            while (_faceUp.Count < WantedCount && _draw.Count > 0)
            {
                var last = _draw.Count - 1;
                _faceUp.Add(_draw[last]);
                _draw.RemoveAt(last);
            }
        }
    }
}
