using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class SupplyMarket
    {
        public const int FaceUpCount = 3;

        public string Planet { get; }
        public IList<SupplyCard> Deck { get; }
        public IList<SupplyCard> FaceUp { get; }
        public IList<SupplyCard> Discard { get; }

        public SupplyMarket(string planet, IEnumerable<SupplyCard>? deck = null)
        {
            Planet = planet;
            Deck = new List<SupplyCard>(deck ?? Array.Empty<SupplyCard>());
            FaceUp = new List<SupplyCard>();
            Discard = new List<SupplyCard>();
        }

        public void Shuffle(IRng rng) => SystemRng.Shuffle(Deck, rng);

        public void ShuffleAndDeal(IRng rng)
        {
            Shuffle(rng);
            Refill();
        }

        /// <summary>
        /// Priming the Pump (GF9 / Director's Cut): reveal the top <paramref name="count"/>
        /// cards into this planet's discard pile.
        /// </summary>
        public void PrimeToDiscard(int count)
        {
            for (var i = 0; i < count && Deck.Count > 0; i++)
            {
                var next = Deck[0];
                Deck.RemoveAt(0);
                Discard.Add(next);
            }
        }

        /// <summary>
        /// Draws the top card of the draw pile (not face-up). Used by Blitz Strip Mining.
        /// </summary>
        public bool TryDraw(out SupplyCard card)
        {
            card = null!;
            if (Deck.Count == 0)
                return false;
            card = Deck[0];
            Deck.RemoveAt(0);
            return true;
        }

        public bool TryTake(string cardId, out SupplyCard card)
        {
            card = null!;
            for (var i = 0; i < FaceUp.Count; i++)
            {
                if (FaceUp[i].Id == cardId)
                {
                    card = FaceUp[i];
                    FaceUp.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool TryTakeFromDiscard(string cardId, out SupplyCard card)
        {
            card = null!;
            for (var i = 0; i < Discard.Count; i++)
            {
                if (string.Equals(Discard[i].Id, cardId, StringComparison.OrdinalIgnoreCase))
                {
                    card = Discard[i];
                    Discard.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool TryFindInDiscard(string cardId, out SupplyCard card)
        {
            card = null!;
            foreach (var existing in Discard)
            {
                if (string.Equals(existing.Id, cardId, StringComparison.OrdinalIgnoreCase))
                {
                    card = existing;
                    return true;
                }
            }
            return false;
        }

        public void Refill()
        {
            while (FaceUp.Count < FaceUpCount && Deck.Count > 0)
            {
                var next = Deck[0];
                Deck.RemoveAt(0);
                FaceUp.Add(next);
            }
        }
    }

    public sealed class SupplyDecks
    {
        private readonly Dictionary<string, SupplyMarket> _byPlanet;

        public SupplyDecks(IEnumerable<SupplyMarket> markets)
        {
            _byPlanet = new Dictionary<string, SupplyMarket>(StringComparer.OrdinalIgnoreCase);
            foreach (var market in markets)
                _byPlanet[market.Planet] = market;
        }

        public IReadOnlyCollection<string> Planets => _byPlanet.Keys;

        public IReadOnlyCollection<SupplyMarket> Markets => _byPlanet.Values;

        public bool TryGet(string planet, out SupplyMarket market) =>
            _byPlanet.TryGetValue(planet, out market!);

        /// <summary>
        /// Locates a Supply card sitting in any planet's discard pile (Nav / Salvage grabs).
        /// </summary>
        public bool TryFindInAnyDiscard(string cardId, out SupplyMarket market, out SupplyCard card)
        {
            market = null!;
            card = null!;
            if (string.IsNullOrWhiteSpace(cardId))
                return false;
            foreach (var candidate in _byPlanet.Values)
            {
                if (candidate.TryFindInDiscard(cardId, out card))
                {
                    market = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pulls every crew copy of this name out of every market (deck, face-up, discard)
        /// and refills the row. Used when that person is seated as a Leader.
        /// </summary>
        public int RemoveCrewNamed(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return 0;
            var removed = 0;
            foreach (var market in _byPlanet.Values)
            {
                removed += Strip(market.Deck, name);
                removed += Strip(market.FaceUp, name);
                removed += Strip(market.Discard, name);
                market.Refill();
            }
            return removed;
        }

        private static int Strip(IList<SupplyCard> pile, string name)
        {
            var n = 0;
            for (var i = pile.Count - 1; i >= 0; i--)
            {
                var card = pile[i];
                if (card.Kind == SupplyKind.Crew &&
                    string.Equals(card.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    pile.RemoveAt(i);
                    n++;
                }
            }
            return n;
        }

        public static SupplyDecks FromCatalog(SupplyCatalog catalog, IRng rng)
        {
            var grouped = new Dictionary<string, List<SupplyCard>>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in catalog.Cards.Values)
            {
                foreach (var kv in card.CopiesByPlanet)
                {
                    if (!grouped.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<SupplyCard>();
                        grouped[kv.Key] = list;
                    }
                    for (var i = 0; i < kv.Value; i++)
                        list.Add(card);
                }
            }

            var markets = new List<SupplyMarket>();
            foreach (var kv in grouped)
            {
                var market = new SupplyMarket(kv.Key, kv.Value);
                // Shuffle only — Priming the Pump + FaceUp refill happen in GameSetup
                // so Strip Mining (The Blitz) can draft from the draw pile first.
                market.Shuffle(rng);
                markets.Add(market);
            }
            return new SupplyDecks(markets);
        }
    }
}
