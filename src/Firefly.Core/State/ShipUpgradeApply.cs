using System;
using Firefly.Core.Abilities;
using Firefly.Core.Cards;

namespace Firefly.Core.State
{
    /// <summary>
    /// Applies installed ship-upgrade structural bonuses to hold / Max Crew.
    /// Director's Cut p.19: Ship Upgrades can boost Full Burn Range, provide extra storage,
    /// increase reliability and add other useful abilities. Full Burn range stays query-time
    /// via <see cref="AbilityDispatcher.ShipUpgradeFullBurnRangeBonus"/>; holds and Max Crew
    /// are refreshed onto <see cref="PlayerState"/> whenever the upgrade list changes.
    /// </summary>
    public static class ShipUpgradeApply
    {
        /// <summary>
        /// Recompute CargoHold / StashHold / PassengerHold / MaxCrew from the ship card plus
        /// typed upgrade abilities. Call after installing or removing a Ship Upgrade.
        /// </summary>
        public static void Refresh(GameState game, PlayerState player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            var baseCargo = 8;
            var baseStash = 4;
            var baseCrew = 6;
            if (game?.Ships != null
                && !string.IsNullOrWhiteSpace(player.ShipId)
                && game.Ships.TryResolve(player.ShipId, out var ship))
            {
                baseCargo = ship.CargoHolds;
                baseStash = ship.Stash;
                baseCrew = ship.MaxCrew;
                player.FuelStash = ship.FuelStash;
                player.UpgradeSlots = ship.UpgradeSlots;
            }

            player.CargoHold = baseCargo + AbilityDispatcher.ShipUpgradeAmount(
                game, player, AbilityTypes.ExtraCargoHold);
            player.StashHold = baseStash + AbilityDispatcher.ShipUpgradeAmount(
                game, player, AbilityTypes.ExtraStashHold);
            player.PassengerHold = AbilityDispatcher.ShipUpgradeAmount(
                game, player, AbilityTypes.PassengerFugitiveHold);
            player.Roster.MaxCrew = baseCrew + AbilityDispatcher.ShipUpgradeAmount(
                game, player, AbilityTypes.MaxCrewBonus);
        }

        /// <summary>
        /// Install an upgrade id and refresh structural bonuses.
        /// </summary>
        public static void Install(GameState game, PlayerState player, string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return;
            if (!player.ShipUpgrades.Contains(upgradeId))
                player.ShipUpgrades.Add(upgradeId);
            Refresh(game, player);
        }

        /// <summary>
        /// Remove one installed upgrade and refresh. Esmeralda Coachworks: if Caravan Pods
        /// (or any Max Crew bonus) leaves the roster over capacity, discard excess non-Leader crew.
        /// </summary>
        public static bool TryRemove(
            GameState game,
            PlayerState player,
            string upgradeId,
            out string? error)
        {
            error = null;
            var removed = false;
            for (var i = 0; i < player.ShipUpgrades.Count; i++)
            {
                if (string.Equals(player.ShipUpgrades[i], upgradeId, StringComparison.OrdinalIgnoreCase))
                {
                    player.ShipUpgrades.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
            if (!removed)
            {
                error = $"Ship upgrade '{upgradeId}' is not installed.";
                return false;
            }

            Refresh(game, player);
            TrimRosterToMaxCrew(game, player);
            return true;
        }

        /// <summary>
        /// Esmeralda: "If this extra capacity is in use and you remove the Caravan Pods
        /// from your ship, you must also discard a Crew at the same time."
        /// </summary>
        public static void TrimRosterToMaxCrew(GameState game, PlayerState player)
        {
            while (player.Roster.Count > player.Roster.MaxCrew)
            {
                CrewMember? victim = null;
                foreach (var member in player.Roster.Members)
                {
                    if (member.IsLeader)
                        continue;
                    victim = member;
                    break;
                }
                if (victim == null)
                    break;
                if (!player.Roster.TryDismiss(victim.Id, out _))
                    break;
                game?.RemovedFromPlay.Add(victim.Id);
            }
        }
    }
}
