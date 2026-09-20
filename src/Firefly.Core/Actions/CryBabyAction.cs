using System;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Ship upgrade "Cry Baby": discard while sharing a Sector with the Alliance Cruiser to ignore
    /// its effects and move the Cruiser 1 Sector within Alliance Space.
    /// FAQ 4.1 p.14: Outlaw enters Cruiser Sector, deploys Cry Baby → Cruiser moves → Nav resolves
    /// as normal (Contact skipped). Multi-ship FAQ: deploying Cry Baby saves everyone in that Sector.
    /// </summary>
    public static class CryBabyAction
    {
        public const string CardId = "ship-upgrade_cry-baby";
        public const string CardName = "Cry Baby";

        public static bool TryDeploy(
            GameState game,
            string playerId,
            string cruiserToSectorId,
            out string? error)
        {
            error = null;
            PlayerState? player = null;
            foreach (var p in game.Players)
            {
                if (string.Equals(p.Id, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    player = p;
                    break;
                }
            }

            if (player == null)
            {
                error = $"Unknown player '{playerId}'.";
                return false;
            }

            if (!TryRemoveCryBaby(player, out error))
                return false;

            var cruiserSector = game.Tokens.AllianceCruiserSectorId;
            if (string.IsNullOrEmpty(cruiserSector))
            {
                error = "Alliance Cruiser is not on the board.";
                RestoreCryBaby(player);
                return false;
            }

            if (!string.Equals(player.SectorId, cruiserSector, StringComparison.OrdinalIgnoreCase))
            {
                error = "Cry Baby requires your ship to be in the Alliance Cruiser's Sector.";
                RestoreCryBaby(player);
                return false;
            }

            if (string.IsNullOrWhiteSpace(cruiserToSectorId))
            {
                error = "Cry Baby requires a destination Sector for the Alliance Cruiser.";
                RestoreCryBaby(player);
                return false;
            }

            if (!IsAllianceSpace(game, cruiserToSectorId))
            {
                error = "Cry Baby must move the Cruiser within Alliance Space.";
                RestoreCryBaby(player);
                return false;
            }

            // AllianceAlert.tsv Rapid Response: player may move an Alliance Ship one extra Sector.
            var maxSectors = 1 + ActiveAlertRules.ExtraAllianceShipMove(game);
            var path = new Pathfinder(game.Map).ShortestPath(cruiserSector!, cruiserToSectorId);
            if (path == null)
            {
                error = "No path for Cry Baby Cruiser move.";
                RestoreCryBaby(player);
                return false;
            }

            var distance = path.Count - 1;
            if (distance < 1 || distance > maxSectors)
            {
                error = maxSectors == 1
                    ? "Cry Baby must move the Cruiser 1 Sector."
                    : $"Cry Baby must move the Cruiser 1 to {maxSectors} Sectors.";
                RestoreCryBaby(player);
                return false;
            }

            // FAQ: Contact is skipped for everyone who would have resolved it in this Sector.
            var restoreNav = game.PendingEncounterDeferredNav
                && game.PendingEncounter == TokenKind.AllianceCruiser;
            var navSector = player.SectorId;
            AllianceCruiserContact.ClearAll(game);

            if (restoreNav && game.Map.TryGet(navSector, out var sector))
            {
                game.PendingNavDraws.Insert(0, new PendingNavDraw(navSector, sector.NavRegion));
            }

            game.Tokens = game.Tokens.WithAllianceCruiser(cruiserToSectorId);
            ReturnCryBabyToSupplyDiscard(game);

            // Cruiser's new Sector: Outlaws there resolve Contact (same as any Cruiser move).
            AllianceCruiserContact.QueueForOutlawsInSector(game, cruiserToSectorId);
            return true;
        }

        private static bool TryRemoveCryBaby(PlayerState player, out string? error)
        {
            error = null;
            for (var i = 0; i < player.ShipUpgrades.Count; i++)
            {
                if (string.Equals(player.ShipUpgrades[i], CardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(player.ShipUpgrades[i], CardName, StringComparison.OrdinalIgnoreCase))
                {
                    player.ShipUpgrades.RemoveAt(i);
                    return true;
                }
            }

            error = "Cry Baby ship upgrade is not installed.";
            return false;
        }

        private static void RestoreCryBaby(PlayerState player) =>
            player.ShipUpgrades.Add(CardId);

        private static void ReturnCryBabyToSupplyDiscard(GameState game)
        {
            if (game.SupplyDecks == null)
                return;

            SupplyCard card;
            if (game.Supply != null && game.Supply.TryGet(CardId, out var fromCatalog))
                card = fromCatalog;
            else
                card = new SupplyCard(CardId, CardName, 800, SupplyKind.ShipUpgrade);

            SupplyMarket? market = null;
            foreach (var candidate in game.SupplyDecks.Markets)
            {
                market = candidate;
                break;
            }

            market?.Discard.Add(card);
        }

        private static bool IsAllianceSpace(GameState game, string sectorId)
        {
            if (!game.Map.TryGet(sectorId, out var sector))
                return false;
            return sector.NavRegion == NavRegion.Alliance;
        }
    }
}
