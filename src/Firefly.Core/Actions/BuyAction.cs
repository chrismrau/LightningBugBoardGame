using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class BuyRequest
    {
        public int Fuel { get; set; }
        public int Parts { get; set; }
        public IList<string> SupplyCardIds { get; set; } = new List<string>();
    }

    public sealed class BuyResult
    {
        public string Planet { get; }
        public int FuelBought { get; }
        public int PartsBought { get; }
        public int CashSpent { get; }
        public IReadOnlyList<SupplyCard> CardsBought { get; }

        public BuyResult(string planet, int fuelBought, int partsBought, int cashSpent, IReadOnlyList<SupplyCard> cardsBought)
        {
            Planet = planet;
            FuelBought = fuelBought;
            PartsBought = partsBought;
            CashSpent = cashSpent;
            CardsBought = cardsBought;
        }
    }

    /// <summary>
    /// Buy at a Supply planet. One action may purchase any mix of:
    /// Fuel ($100), Parts ($300), and any of the 3 face-up Supply cards
    /// (Gear, Crew, Ship Upgrades, Drive Cores). Bought cards are replaced
    /// from that planet's Supply deck.
    /// </summary>
    public sealed class BuyAction
    {
        public const int FuelPrice = 100;
        public const int PartsPrice = 300;

        public bool TryBuy(
            GameState game,
            string playerId,
            BuyRequest request,
            out BuyResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (!game.CanTakeAction(TurnAction.Buy, out error))
                return false;
            if (request == null)
            {
                error = "A buy request is required.";
                return false;
            }
            if (request.Fuel < 0 || request.Parts < 0)
            {
                error = "Cannot buy a negative amount.";
                return false;
            }

            if (!game.Map.TryGet(player.SectorId, out var sector))
            {
                error = $"Unknown sector '{player.SectorId}'.";
                return false;
            }

            var planet = ShopPlanet(sector);
            if (string.IsNullOrWhiteSpace(planet) || game.SupplyDecks == null || !game.SupplyDecks.TryGet(planet, out var market))
            {
                error = "Must be at a Supply planet to Buy.";
                return false;
            }

            var cardIds = request.SupplyCardIds ?? new List<string>();
            if (request.Fuel == 0 && request.Parts == 0 && cardIds.Count == 0)
            {
                error = "Buy must purchase at least one item.";
                return false;
            }

            var seen = new HashSet<string>();
            var wanted = new List<SupplyCard>();
            foreach (var id in cardIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "A Supply card id is required.";
                    return false;
                }
                if (!seen.Add(id))
                {
                    error = $"Cannot buy '{id}' twice in the same action.";
                    return false;
                }

                SupplyCard? onTable = null;
                foreach (var face in market.FaceUp)
                {
                    if (face.Id == id)
                    {
                        onTable = face;
                        break;
                    }
                }
                if (onTable == null)
                {
                    error = $"'{id}' is not for sale at {planet}.";
                    return false;
                }
                wanted.Add(onTable);
            }

            var cost = request.Fuel * FuelPrice + request.Parts * PartsPrice;
            foreach (var card in wanted)
                cost += card.Cost;
            if (player.Cash < cost)
            {
                error = $"Need ${cost}, have ${player.Cash}.";
                return false;
            }
            if (!HoldSpace.TryExplain(player, out error, addFuel: request.Fuel, addParts: request.Parts))
                return false;

            foreach (var card in wanted)
            {
                if (card.Kind == SupplyKind.Crew && !CanHire(game, player, card, out error))
                    return false;
                if (card.Kind == SupplyKind.DriveCore && !CanInstallDrive(game, player, card, out error))
                    return false;
            }

            player.Cash -= cost;
            player.Fuel += request.Fuel;
            player.Parts += request.Parts;

            var bought = new List<SupplyCard>();
            foreach (var card in wanted)
            {
                if (!market.TryTake(card.Id, out var taken))
                {
                    error = $"'{card.Id}' is not for sale at {planet}.";
                    return false;
                }
                if (!GiveCard(game, player, taken, out error))
                    return false;
                bought.Add(taken);
            }
            market.Refill();

            if (!game.TryConsumeAction(TurnAction.Buy, out error))
                return false;

            result = new BuyResult(planet, request.Fuel, request.Parts, cost, bought);
            return true;
        }

        public static string? ShopPlanet(Sector sector)
        {
            if (!string.IsNullOrWhiteSpace(sector.Planet))
                return sector.Planet;
            return sector.DisplayName;
        }

        private static bool CanHire(GameState game, PlayerState player, SupplyCard card, out string? error)
        {
            if (game.Crew == null || !game.Crew.TryGet(card.Id, out _))
            {
                error = $"Crew card '{card.Id}' is not in the catalog.";
                return false;
            }
            if (player.Roster.Count >= player.Roster.MaxCrew)
            {
                error = $"Roster is full ({player.Roster.MaxCrew}).";
                return false;
            }
            error = null;
            return true;
        }

        private static bool CanInstallDrive(GameState game, PlayerState player, SupplyCard card, out string? error)
        {
            var cores = game.DriveCores ?? DriveCoreCatalog.LoadDefault();
            if (!cores.TryResolve(card.Id, out _) && cores.FindByName(card.Name) == null)
            {
                error = $"Drive Core '{card.Id}' is not in the catalog.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(player.DriveCoreId)
                && cores.TryResolve(player.DriveCoreId, out var current)
                && current.Locked)
            {
                error = $"{current.Name} cannot be replaced.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool InstallDrive(GameState game, PlayerState player, SupplyCard card, out string? error)
        {
            var cores = game.DriveCores ?? DriveCoreCatalog.LoadDefault();
            if (!cores.TryResolve(card.Id, out var core))
                core = cores.FindByName(card.Name)!;
            if (core == null)
            {
                error = $"Drive Core '{card.Id}' is not in the catalog.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(player.DriveCoreId)
                && cores.TryResolve(player.DriveCoreId, out var current)
                && current.Locked)
            {
                error = $"{current.Name} cannot be replaced.";
                return false;
            }
            player.ApplyDriveCore(core);
            error = null;
            return true;
        }

        private static bool GiveCard(GameState game, PlayerState player, SupplyCard card, out string? error)
        {
            error = null;
            switch (card.Kind)
            {
                case SupplyKind.Crew:
                    if (game.Crew == null || !game.Crew.TryGet(card.Id, out var crew))
                    {
                        error = $"Crew card '{card.Id}' is not in the catalog.";
                        return false;
                    }
                    if (!player.Roster.TryHire(crew, out error))
                        return false;
                    DeceptiveCrew.AfterHired(game, crew.Name);
                    return true;
                case SupplyKind.Gear:
                    player.Gear.Add(card.Id);
                    return true;
                case SupplyKind.ShipUpgrade:
                    player.ShipUpgrades.Add(card.Id);
                    return true;
                case SupplyKind.DriveCore:
                    return InstallDrive(game, player, card, out error);
                default:
                    error = $"Unknown supply kind '{card.Kind}'.";
                    return false;
            }
        }
    }
}
