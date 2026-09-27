using System;

namespace Firefly.Core.State
{
    /// <summary>
    /// Official hold packing (GF9 setup p.4 / Buy p.10, Reference Sheet 8.1 Ships):
    /// each Cargo or Stash space holds 1 Cargo, 1 Contraband, 1 Passenger, or 1 Fugitive,
    /// or up to 2 Fuel and/or Parts in any mix. Jetwash and Esmeralda have 6 Fuel-only
    /// stash spaces that each hold exactly 1 Fuel.
    /// Caravan Pods (Esmeralda Coachworks): extra Cargo Hold spaces that may only hold
    /// Passengers or Fugitives — they do not accept Cargo, Contraband, Fuel, or Parts.
    /// Packing is always evaluated as an optimal rearrangement (dump/rearrange is free).
    /// </summary>
    public static class HoldSpace
    {
        public const int FuelOrPartsPerHold = 2;

        public static int GeneralSlots(PlayerState player) =>
            Math.Max(0, player.CargoHold) + Math.Max(0, player.StashHold);

        public static int PassengerOnlySlots(PlayerState player) =>
            Math.Max(0, player.PassengerHold);

        public static int UsedGeneral(PlayerState player) =>
            UsedGeneral(
                player.Fuel,
                player.Parts,
                player.Cargo,
                player.Contraband,
                player.Passengers,
                player.Fugitives,
                player.FuelStash,
                player.PassengerHold);

        public static int UsedGeneral(
            int fuel,
            int parts,
            int cargo,
            int contraband,
            int passengers,
            int fugitives,
            int fuelOnlySlots,
            int passengerOnlySlots = 0)
        {
            fuel = Math.Max(0, fuel);
            parts = Math.Max(0, parts);
            var inFuelOnly = Math.Min(fuel, Math.Max(0, fuelOnlySlots));
            var looseFuel = fuel - inFuelOnly;
            var halfUnits = looseFuel + parts;
            var halfHolds = (halfUnits + FuelOrPartsPerHold - 1) / FuelOrPartsPerHold;
            var people = Math.Max(0, passengers) + Math.Max(0, fugitives);
            var peopleInPassengerOnly = Math.Min(people, Math.Max(0, passengerOnlySlots));
            var peopleInGeneral = people - peopleInPassengerOnly;
            var fullUnits = Math.Max(0, cargo) + Math.Max(0, contraband) + peopleInGeneral;
            return fullUnits + halfHolds;
        }

        public static int FreeGeneral(PlayerState player) =>
            Math.Max(0, GeneralSlots(player) - UsedGeneral(player));

        public static bool Fits(
            PlayerState player,
            int addFuel = 0,
            int addParts = 0,
            int addCargo = 0,
            int addContraband = 0,
            int addPassengers = 0,
            int addFugitives = 0)
        {
            var used = UsedGeneral(
                player.Fuel + addFuel,
                player.Parts + addParts,
                player.Cargo + addCargo,
                player.Contraband + addContraband,
                player.Passengers + addPassengers,
                player.Fugitives + addFugitives,
                player.FuelStash,
                player.PassengerHold);
            return used <= GeneralSlots(player);
        }

        public static bool TryExplain(
            PlayerState player,
            out string? error,
            int addFuel = 0,
            int addParts = 0,
            int addCargo = 0,
            int addContraband = 0,
            int addPassengers = 0,
            int addFugitives = 0)
        {
            if (Fits(player, addFuel, addParts, addCargo, addContraband, addPassengers, addFugitives))
            {
                error = null;
                return true;
            }

            var passengerOnly = PassengerOnlySlots(player);
            var suffix = passengerOnly > 0
                ? $" (+{passengerOnly} passenger/fugitive-only)"
                : "";
            error = $"Not enough cargo/stash space ({UsedGeneral(player)}/{GeneralSlots(player)} holds used{suffix}).";
            return false;
        }
    }
}
