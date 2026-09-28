using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Xunsu Emergency Ram Jets (Jetwash): discard and use an Action to Initiate a Full Burn.
    /// May be used in addition to a standard Move Action (does not block / is not blocked by Fly).
    /// Supplies.tsv / ShipUpgrades.json / Jetwash rules.
    /// </summary>
    public static class RamJetsAction
    {
        public const string CardId = "ship-upgrade_xunsu-emergency-ram-jets_jetwash";
        public const string CardName = "Xunsu Emergency Ram Jets";

        public static bool HasRamJets(PlayerState player)
        {
            if (player?.ShipUpgrades == null)
                return false;
            foreach (var id in player.ShipUpgrades)
            {
                if (string.Equals(id, CardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, CardName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "Emergency Ram Jets", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool TryFullBurnTo(
            GameState game,
            string playerId,
            string toSectorId,
            MovementEngine movement,
            out FlyResult? result,
            out string? error,
            bool useDecoyNavSat = false,
            bool useFuelCatalyzer = false)
        {
            result = null;
            if (!CanAct(game, playerId, out var player, out error))
                return false;

            var path = movement.Pathfinder.ShortestPath(player.SectorId, toSectorId);
            if (path == null)
            {
                error = $"No path from '{player.SectorId}' to '{toSectorId}'.";
                return false;
            }

            return TryFullBurn(
                game, playerId, path, movement, out result, out error, useDecoyNavSat, useFuelCatalyzer);
        }

        public static bool TryFullBurn(
            GameState game,
            string playerId,
            IReadOnlyList<string> path,
            MovementEngine movement,
            out FlyResult? result,
            out string? error,
            bool useDecoyNavSat = false,
            bool useFuelCatalyzer = false)
        {
            result = null;
            if (!CanAct(game, playerId, out var player, out error))
                return false;

            if (!HasRamJets(player))
            {
                error = "Xunsu Emergency Ram Jets ship upgrade is not installed.";
                return false;
            }

            if (useFuelCatalyzer && !FuelCatalyzerAction.HasCatalyzer(player))
            {
                error = "Modded Fuel Catalyzer ship upgrade is not installed.";
                return false;
            }

            var initiateFuel = player.FullBurnRequiresFuel ? 1 : 0;
            var catalyzerFuel = FuelCatalyzerAction.ExtraFuelToActivate(game, player, useFuelCatalyzer);
            var fuelNeeded = initiateFuel + catalyzerFuel;
            if (player.Fuel < fuelNeeded)
            {
                error = fuelNeeded > 1
                    ? "Not enough fuel for Full Burn with Fuel Catalyzer."
                    : "Not enough fuel for Full Burn.";
                return false;
            }

            var range = FuelCatalyzerAction.DriveRangeForInitiate(game, player, useFuelCatalyzer);
            var reaverEntry = ReaverEntryAllowance.For(game, player);
            if (!movement.TryFullBurn(
                    path,
                    range,
                    game.Tokens,
                    reaverEntry,
                    out var plan,
                    out error) || plan == null)
                return false;

            if (plan.FromSectorId != player.SectorId)
            {
                error = "Full Burn path must start in the player's current sector.";
                return false;
            }

            if (useDecoyNavSat
                && !DecoyNavSatAction.TryDiscardAtMoveStart(game, player, out error))
                return false;

            if (!TryDiscardRamJets(game, player, out error))
                return false;

            if (!game.TryConsumeRamJetsAction(out error))
            {
                ShipUpgradeApply.Install(game, player, CardId);
                return false;
            }

            if (catalyzerFuel > 0)
            {
                player.Fuel -= catalyzerFuel;
                FuelCatalyzerAction.ActivateThisTurn(game);
            }

            FlyAction.ApplyRamJetsBurn(game, player, plan, out result, useDecoyNavSat);
            return true;
        }

        private static bool CanAct(
            GameState game,
            string playerId,
            out PlayerState player,
            out string? error)
        {
            player = game.GetPlayer(playerId);
            error = null;
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }

            if (game.HasPendingEvents)
            {
                error = "Resolve pending Alert Tokens, Nav cards, encounters, Misbehave, or choices before taking another action.";
                return false;
            }

            if (game.TurnComplete)
            {
                error = "This player has already taken both actions this turn.";
                return false;
            }

            return true;
        }

        private static bool TryDiscardRamJets(GameState game, PlayerState player, out string? error)
        {
            if (ShipUpgradeApply.TryRemove(game, player, CardId, out error))
            {
                ReturnToSupplyDiscard(game);
                return true;
            }

            if (ShipUpgradeApply.TryRemove(game, player, CardName, out error))
            {
                ReturnToSupplyDiscard(game);
                return true;
            }

            if (ShipUpgradeApply.TryRemove(game, player, "Emergency Ram Jets", out error))
            {
                ReturnToSupplyDiscard(game);
                return true;
            }

            return false;
        }

        private static void ReturnToSupplyDiscard(GameState game)
        {
            if (game.SupplyDecks == null)
                return;

            SupplyCard card;
            if (game.Supply != null && game.Supply.TryGet(CardId, out var fromCatalog))
                card = fromCatalog;
            else
                card = new SupplyCard(CardId, CardName, 600, SupplyKind.ShipUpgrade);

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
