using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Crime &amp; Punishment Alliance Alert deck. Draw pile is face-down;
    /// index 0 is the bottom. At most one card is Active. Audit / Sighting
    /// may park that Active card on a Contact or Supply deck until it moves.
    /// </summary>
    public sealed class AllianceAlertDeck
    {
        private readonly List<AllianceAlertCard> _draw;
        private readonly IRng _rng;

        public AllianceAlertCatalog Catalog { get; }
        public AllianceAlertCard? Active { get; private set; }
        public AllianceAlertPark? ParkedOn { get; private set; }
        public int DrawCount => _draw.Count;
        public bool HasActive => Active != null;
        public bool IsParked => ParkedOn != null && ParkedOn.Kind != AllianceAlertParkKind.None;

        public AllianceAlertDeck(IEnumerable<AllianceAlertCard> cards, IRng rng, AllianceAlertCatalog? catalog = null)
        {
            _draw = new List<AllianceAlertCard>(cards);
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Catalog = catalog ?? new AllianceAlertCatalog(_draw);
            SystemRng.Shuffle(_draw, _rng);
        }

        public static AllianceAlertDeck FromCatalog(AllianceAlertCatalog catalog, IRng rng) =>
            new AllianceAlertDeck(catalog.Cards.Values, rng, catalog);

        /// <summary>
        /// Bury the current Active (lifting it if parked) and reveal the
        /// next card from the top of the draw pile.
        /// </summary>
        public AllianceAlertCard? DrawAndActivate()
        {
            BuryActive();
            if (_draw.Count == 0)
                return null;
            var last = _draw.Count - 1;
            Active = _draw[last];
            _draw.RemoveAt(last);
            return Active;
        }

        public void BuryActive()
        {
            if (Active == null)
                return;
            var card = Active;
            Active = null;
            ParkedOn = null;
            _draw.Insert(0, card);
        }

        public void ParkOnContact(string contactName)
        {
            if (Active == null)
                throw new InvalidOperationException("No Active Alliance Alert to park.");
            if (string.IsNullOrWhiteSpace(contactName))
                throw new ArgumentException("A contact name is required.", nameof(contactName));
            ParkedOn = AllianceAlertPark.Contact(contactName);
        }

        public void ParkOnSupply(string planet)
        {
            if (Active == null)
                throw new InvalidOperationException("No Active Alliance Alert to park.");
            if (string.IsNullOrWhiteSpace(planet))
                throw new ArgumentException("A supply planet is required.", nameof(planet));
            ParkedOn = AllianceAlertPark.Supply(planet);
        }

        public void LiftParked()
        {
            ParkedOn = null;
        }

        public bool IsParkedOnContact(string contactName)
        {
            return ParkedOn != null
                && ParkedOn.Kind == AllianceAlertParkKind.Contact
                && string.Equals(ParkedOn.Target, contactName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsParkedOnSupply(string planet)
        {
            return ParkedOn != null
                && ParkedOn.Kind == AllianceAlertParkKind.Supply
                && string.Equals(ParkedOn.Target, planet, StringComparison.OrdinalIgnoreCase);
        }
    }
}
