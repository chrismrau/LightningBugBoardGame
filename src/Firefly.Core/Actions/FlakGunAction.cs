using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Hull-Mounted Flak Gun: when sharing a Sector with a Reaver Cutter, discard to ignore
    /// that Cutter's effects and move it 1 Sector within Rim or Border Space.
    /// Supplies.tsv / ShipUpgrades.json. Card text only — no Cry Baby FAQ analogies.
    /// </summary>
    public static class FlakGunAction
    {
        public const string CardId = "ship-upgrade_hull-mounted-flak-gun_bluesun";
        public const string CardName = "Hull-Mounted Flak Gun";

        public static bool TryDeploy(
            GameState game,
            string playerId,
            string? cutterToSectorId,
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

            if (!HasFlak(player))
            {
                error = "Hull-Mounted Flak Gun ship upgrade is not installed.";
                return false;
            }

            if (!TryFindCutterAt(player.SectorId, game.Tokens, out var cutterIndex, out var cutterSector))
            {
                error = "Hull-Mounted Flak Gun requires your ship to be in a Reaver Cutter's Sector.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cutterToSectorId))
            {
                var options = EligibleDestinations(game, cutterSector!);
                var pending = new PendingChoice(
                    player.Id,
                    PendingChoiceKinds.SectorDestination,
                    contextId: SectorDestinationContexts.FlakGun,
                    options: options.Count > 0 ? options : null,
                    prompt: "Flak Gun: choose a Rim or Border Sector for the Reaver Cutter (1 Sector).");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                error = pending.Prompt;
                return false;
            }

            return TryApplyDeploy(game, player, cutterIndex, cutterSector!, cutterToSectorId!, out error);
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.SectorDestination"/> for Flak Gun.
        /// </summary>
        public static bool TryResumeDestination(
            GameState game,
            ChoiceSubmission submission,
            out string? error)
        {
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.SectorDestination,
                    StringComparison.Ordinal)
                || !string.Equals(
                    game.PendingChoice.ContextId,
                    SectorDestinationContexts.FlakGun,
                    StringComparison.Ordinal))
            {
                error = "No Flak Gun destination choice is pending.";
                return false;
            }

            var playerId = game.PendingChoice.PlayerId;
            var sector = submission.Value ?? submission.SelectedOptionId;
            if (string.IsNullOrWhiteSpace(sector))
            {
                error = "Flak Gun requires a destination Sector for the Reaver Cutter.";
                return false;
            }

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            return TryDeploy(game, playerId, sector, out error);
        }

        private static bool TryApplyDeploy(
            GameState game,
            PlayerState player,
            int cutterIndex,
            string cutterSector,
            string cutterToSectorId,
            out string? error)
        {
            if (!TryRemoveFlak(game, player, out error))
                return false;

            if (!IsRimOrBorder(game, cutterToSectorId))
            {
                error = "Flak Gun must move the Reaver Cutter within Rim or Border Space.";
                RestoreFlak(game, player);
                return false;
            }

            var path = new Pathfinder(game.Map).ShortestPath(cutterSector, cutterToSectorId);
            if (path == null)
            {
                error = "No path for Flak Gun Reaver Cutter move.";
                RestoreFlak(game, player);
                return false;
            }

            var distance = path.Count - 1;
            if (distance != 1)
            {
                error = "Flak Gun must move the Reaver Cutter 1 Sector.";
                RestoreFlak(game, player);
                return false;
            }

            if (!game.Tokens.TryMoveReaverCutter(
                    cutterToSectorId,
                    out var updated,
                    out error,
                    cutterIndex,
                    leaveReaverAlertToken: game.UseAlertTokens))
            {
                RestoreFlak(game, player);
                return false;
            }

            // Card text: ignore that Cutter's effects for the deployer.
            if (game.PendingEncounter == TokenKind.ReaverCutter
                && (game.PendingEncounterPlayerId == null
                    || string.Equals(
                        game.PendingEncounterPlayerId,
                        player.Id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                game.PendingEncounter = null;
                game.PendingEncounterSectorId = null;
                game.PendingEncounterPlayerId = null;
            }

            game.Tokens = updated;
            ReturnFlakToSupplyDiscard(game);
            return true;
        }

        private static IReadOnlyList<string> EligibleDestinations(GameState game, string fromSectorId)
        {
            var result = new List<string>();
            foreach (var neighbor in game.Map.Neighbors(fromSectorId))
            {
                if (!IsRimOrBorder(game, neighbor))
                    continue;
                // Blue Sun: only one Reaver per Sector.
                if (game.Tokens.EncounterAt(neighbor) == TokenKind.ReaverCutter)
                    continue;
                result.Add(neighbor);
            }
            return result;
        }

        private static bool TryFindCutterAt(
            string sectorId,
            MapTokens tokens,
            out int cutterIndex,
            out string? cutterSector)
        {
            cutterIndex = -1;
            cutterSector = null;
            for (var i = 0; i < tokens.ReaverCutterSectorIds.Count; i++)
            {
                if (string.Equals(
                        tokens.ReaverCutterSectorIds[i],
                        sectorId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    cutterIndex = i;
                    cutterSector = tokens.ReaverCutterSectorIds[i];
                    return true;
                }
            }
            return false;
        }

        private static bool HasFlak(PlayerState player)
        {
            foreach (var id in player.ShipUpgrades)
            {
                if (string.Equals(id, CardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, CardName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool TryRemoveFlak(GameState game, PlayerState player, out string? error)
        {
            if (ShipUpgradeApply.TryRemove(game, player, CardId, out error))
                return true;
            return ShipUpgradeApply.TryRemove(game, player, CardName, out error);
        }

        private static void RestoreFlak(GameState game, PlayerState player) =>
            ShipUpgradeApply.Install(game, player, CardId);

        private static void ReturnFlakToSupplyDiscard(GameState game)
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

        private static bool IsRimOrBorder(GameState game, string sectorId)
        {
            if (!game.Map.TryGet(sectorId, out var sector))
                return false;
            return sector.NavRegion == NavRegion.Rim || sector.NavRegion == NavRegion.Border;
        }
    }
}
