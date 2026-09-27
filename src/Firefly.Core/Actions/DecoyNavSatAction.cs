using System;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Decoy Nav Sat Cluster (Jetwash): discard at the beginning of a Move (Fly) Action to
    /// treat Nav Cards that would move a Reaver or Alliance Ship as "The Big Black" instead.
    /// Supplies.tsv / ShipUpgrades.json card text.
    /// Alliance Ship = Alliance Cruiser-type Nav and Operative's Corvette (same reading as
    /// Rapid Response / AllianceAlert.tsv ExtraAllianceShipMove).
    /// </summary>
    public static class DecoyNavSatAction
    {
        public const string CardId = "ship-upgrade_decoy-nav-sat-cluster_jetwash";
        public const string CardName = "Decoy Nav Sat Cluster";

        public static bool HasDecoy(PlayerState player)
        {
            foreach (var id in player.ShipUpgrades)
            {
                if (string.Equals(id, CardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, CardName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Discard the upgrade at Move start. Caller sets
        /// <see cref="GameState.DecoyNavSatActiveThisFly"/> after clearing Fly-scoped state.
        /// </summary>
        public static bool TryDiscardAtMoveStart(GameState game, PlayerState player, out string? error)
        {
            error = null;
            if (!HasDecoy(player))
            {
                error = "Decoy Nav Sat Cluster ship upgrade is not installed.";
                return false;
            }

            if (!ShipUpgradeApply.TryRemove(game, player, CardId, out error)
                && !ShipUpgradeApply.TryRemove(game, player, CardName, out error))
                return false;

            ReturnToSupplyDiscard(game);
            return true;
        }

        /// <summary>
        /// Nav Cards that would normally move a Reaver Cutter, Alliance Cruiser, or Corvette.
        /// </summary>
        public static bool IsReaverOrAllianceShipMover(NavCard card)
        {
            var type = card.Type ?? "";
            return type.Equals("Alliance Cruiser", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Operative's Corvette", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Reaver Cutter", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReturnToSupplyDiscard(GameState game)
        {
            if (game.SupplyDecks == null)
                return;

            SupplyCard card;
            if (game.Supply != null && game.Supply.TryGet(CardId, out var fromCatalog))
                card = fromCatalog;
            else
                card = new SupplyCard(CardId, CardName, 400, SupplyKind.ShipUpgrade);

            SupplyMarket? market = null;
            foreach (var candidate in game.SupplyDecks.Markets)
            {
                market = candidate;
                break;
            }

            market?.Discard.Add(card);
        }
    }
}
