using System;
using Firefly.Core.State;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Queries the Active Alliance Alert without parsing card text.
    /// Card effects that need a hook call these predicates.
    /// </summary>
    public static class ActiveAlertRules
    {
        public const string PrivilegeSuspension = "Privilege Suspension";
        public const string WhiteSunCommerce = "White Sun Commerce";
        public const string EnhancedInspection = "Enhanced Inspection";
        public const string CriminalActivity = "Criminal Activity";
        public const string RapidResponse = "Rapid Response";
        public const string AllianceAudit = "Alliance Audit";
        public const string CriminalSighting = "Criminal Sighting";
        public const string BackgroundChecks = "Background Checks";
        public const string IncarcerationOrder = "Incarceration Order";
        public const string PersonsOfInterest = "Persons of Interest";

        public static AllianceAlertCard? Active(GameState? game) =>
            game?.AllianceAlertDeck?.Active;

        public static bool IsActive(GameState? game, string idOrName)
        {
            var card = Active(game);
            if (card == null || string.IsNullOrWhiteSpace(idOrName))
                return false;
            return card.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
                || card.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsHarken(string? idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName))
                return false;
            if (ContactNames.EqualsName(idOrName, "Harken"))
                return true;
            return idOrName.IndexOf("harken", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool CountsAsSolidWith(GameState game, PlayerState player, string contactIdOrName)
        {
            if (player == null || !player.IsSolidWith(contactIdOrName))
                return false;
            if (IsHarken(contactIdOrName) && IsActive(game, PrivilegeSuspension))
                return false;
            return true;
        }

        public static int CountedSolidCount(GameState game, PlayerState player)
        {
            if (player == null)
                return 0;
            if (!IsActive(game, PrivilegeSuspension))
                return player.SolidCount;
            var n = 0;
            foreach (var id in player.SolidWith)
            {
                if (!IsHarken(id))
                    n++;
            }
            return n;
        }

        public static bool BlocksBuyAt(GameState game, PlayerState player, string? planet) =>
            BuyBlockReason(game, player, planet) != null;

        public static string? BuyBlockReason(GameState game, PlayerState player, string? planet)
        {
            if (IsActive(game, CriminalSighting)
                && game.AllianceAlertDeck != null
                && game.AllianceAlertDeck.IsParkedOnSupply(planet ?? ""))
                return "Criminal Sighting: cannot Buy at this location until the Alert moves.";
            if (player != null && player.Warrants > 0
                && IsActive(game, WhiteSunCommerce)
                && IsWhiteSunCommercePlanet(planet))
                return "White Sun Commerce: cannot Buy at Osiris or Persephone while you have Warrants.";
            return null;
        }

        public static bool BlocksDealWith(GameState? game, string? contactName)
        {
            if (!IsActive(game, AllianceAudit) || string.IsNullOrWhiteSpace(contactName))
                return false;
            return game!.AllianceAlertDeck != null
                && game.AllianceAlertDeck.IsParkedOnContact(contactName);
        }

        public static void OnJobCompleted(GameState game, string? contactName)
        {
            if (!IsActive(game, AllianceAudit) || string.IsNullOrWhiteSpace(contactName))
                return;
            if (game.AllianceAlertDeck == null)
                return;
            game.AllianceAlertDeck.ParkOnContact(contactName);
        }

        public static void OnWantedCrewHired(GameState game, string? planet)
        {
            if (!IsActive(game, CriminalSighting) || string.IsNullOrWhiteSpace(planet))
                return;
            if (game.AllianceAlertDeck == null)
                return;
            game.AllianceAlertDeck.ParkOnSupply(planet);
        }

        public static bool IsWhiteSunCommercePlanet(string? planet) =>
            !string.IsNullOrWhiteSpace(planet)
            && (planet.Equals("Osiris", StringComparison.OrdinalIgnoreCase)
                || planet.Equals("Persephone", StringComparison.OrdinalIgnoreCase));

        public static bool BlocksSellCargo(GameState? game) =>
            IsActive(game, EnhancedInspection);

        public static int ExtraIllegalMisbehave(GameState game, PlayerState player, JobCard job)
        {
            if (job == null || player == null)
                return 0;
            if (!IsActive(game, CriminalActivity))
                return 0;
            if (player.Warrants <= 0 || job.Legal)
                return 0;
            return 1;
        }

        /// <summary>
        /// Extra sectors a player may add when they move an Alliance ship.
        /// Does not apply to Alliance Cruiser / Alliance Contact Nav snaps.
        /// </summary>
        public static int ExtraAllianceShipMove(GameState? game) =>
            IsActive(game, RapidResponse) ? 1 : 0;

        /// <summary>
        /// Default: only a 1 is captured. Background Checks with warrants:
        /// must roll strictly higher than the warrant count to dodge.
        /// </summary>
        public static bool WantedCrewCaptured(GameState? game, int die, int warrantsAtEncounter)
        {
            if (IsActive(game, BackgroundChecks) && warrantsAtEncounter > 0)
                return die <= warrantsAtEncounter;
            return die == 1;
        }

        public static bool CanUseIncarcerationOrder(GameState? game, PlayerState? player) =>
            IsActive(game, IncarcerationOrder)
            && player != null
            && player.Warrants > 0
            && player.Roster.WantedCount > 0;

        public static int OnJobBotched(GameState? game, PlayerState? player)
        {
            if (!IsActive(game, PersonsOfInterest) || player == null)
                return 0;
            return player.Roster.DisgruntleWhere(_ => true);
        }
    }
}
