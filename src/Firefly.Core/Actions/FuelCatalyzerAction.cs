using System;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    /// <summary>
    /// Modded Fuel Catalyzer (Blue Sun): when initiating a Full Burn, spend 1 additional
    /// Fuel to add +2 to Drive Core Max Range this turn. Supplies.tsv / ShipUpgrades.json.
    /// </summary>
    public static class FuelCatalyzerAction
    {
        public const string CardId = "ship-upgrade_modded-fuel-catalyzer_bluesun";
        public const string CardName = "Modded Fuel Catalyzer";
        public const int RangeBonus = 2;
        public const int ExtraFuelCost = 1;

        public static bool HasCatalyzer(PlayerState player)
        {
            if (player?.ShipUpgrades == null)
                return false;
            foreach (var id in player.ShipUpgrades)
            {
                if (string.Equals(id, CardId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, CardName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Range already granted this turn after a prior Catalyzer spend.
        /// </summary>
        public static int ActiveRangeBonus(GameState game) =>
            game?.CatalyzerRangeBonusThisTurn ?? 0;

        /// <summary>
        /// Full Burn initiate range including an optional Catalyzer activation this call.
        /// </summary>
        public static int DriveRangeForInitiate(
            GameState game,
            PlayerState player,
            bool useCatalyzer)
        {
            var range = player.GetEffectiveDriveRange(game) + ActiveRangeBonus(game);
            if (useCatalyzer && ActiveRangeBonus(game) == 0 && HasCatalyzer(player))
                range += RangeBonus;
            return range < 1 ? 1 : range;
        }

        /// <summary>
        /// Extra Fuel required to activate Catalyzer on this initiate (0 if already active
        /// this turn, not installed, or caller declined).
        /// </summary>
        public static int ExtraFuelToActivate(GameState game, PlayerState player, bool useCatalyzer)
        {
            if (!useCatalyzer || !HasCatalyzer(player) || ActiveRangeBonus(game) > 0)
                return 0;
            return ExtraFuelCost;
        }

        /// <summary>Mark +2 Max Range for the rest of the turn after a successful activate spend.</summary>
        public static void ActivateThisTurn(GameState game)
        {
            game.CatalyzerRangeBonusThisTurn = RangeBonus;
        }
    }
}
