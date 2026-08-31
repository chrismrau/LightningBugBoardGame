using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class DealResult
    {
        public ContactCard Contact { get; }
        public IReadOnlyList<JobCard> Considered { get; }
        public JobCard? Kept { get; }
        public int ContrabandSold { get; }
        public int CargoSold { get; }
        public int CashFromSales { get; }
        public bool WarrantsCleared { get; }

        public DealResult(
            ContactCard contact,
            IReadOnlyList<JobCard> considered,
            JobCard? kept,
            int contrabandSold,
            int cargoSold,
            int cashFromSales,
            bool warrantsCleared)
        {
            Contact = contact;
            Considered = considered;
            Kept = kept;
            ContrabandSold = contrabandSold;
            CargoSold = cargoSold;
            CashFromSales = cashFromSales;
            WarrantsCleared = warrantsCleared;
        }
    }

    /// <summary>
    /// Official Deal action: consider jobs from a Contact in range, optionally keep one,
    /// and sell cargo/contraband at that Contact's printed prices.
    /// </summary>
    public sealed class DealAction
    {
        public const int DefaultConsider = 3;
        public const int PatienceSolidConsider = 4;
        public const int BadgerWarrantClearCost = 1000;

        public bool TryDeal(
            GameState game,
            string playerId,
            string contactName,
            string? keepJobId,
            int sellContraband,
            int sellCargo,
            bool clearWarrants,
            out DealResult? result,
            out string? error)
        {
            result = null;
            var player = game.GetPlayer(playerId);
            if (!ReferenceEquals(player, game.CurrentPlayer))
            {
                error = $"It is not {player.Name}'s turn.";
                return false;
            }
            if (!game.CanTakeAction(TurnAction.Deal, out error))
                return false;
            if (game.Contacts == null || game.Jobs == null || game.ContactDecks == null)
            {
                error = "Contact and job decks are not loaded.";
                return false;
            }
            if (!game.Contacts.TryFindByName(contactName, out var contact))
            {
                error = $"Unknown contact '{contactName}'.";
                return false;
            }
            if (contact.IsHiggins && player.Roster.HasName("Jayne"))
            {
                error = "Higgins will not Deal while Jayne is in the crew.";
                return false;
            }
            if (!CanReachContact(game, player, contact, out error))
                return false;
            if (!game.ContactDecks.TryGet(contact.Name, out var deck))
            {
                error = $"No job deck for {contact.Name}.";
                return false;
            }

            var considerCount = contact.IsPatience && player.IsSolidWith(contact.Id)
                ? PatienceSolidConsider
                : DefaultConsider;
            var considered = deck.DrawConsider(considerCount);

            JobCard? kept = null;
            if (!string.IsNullOrWhiteSpace(keepJobId))
            {
                foreach (var job in considered)
                {
                    if (job.Id == keepJobId)
                    {
                        kept = job;
                        break;
                    }
                }
                if (kept == null)
                {
                    deck.PutOnBottom(considered);
                    error = $"Job '{keepJobId}' was not among the considered cards.";
                    return false;
                }
                if (player.JobHand.Count >= player.JobHandLimit)
                {
                    deck.PutOnBottom(considered);
                    error = $"Job hand is full ({player.JobHandLimit}).";
                    return false;
                }
            }

            if (sellContraband < 0 || sellCargo < 0)
            {
                deck.PutOnBottom(considered);
                error = "Cannot sell a negative quantity.";
                return false;
            }
            if (sellContraband > player.Contraband || sellCargo > player.Cargo)
            {
                deck.PutOnBottom(considered);
                error = "Not enough cargo or contraband to sell.";
                return false;
            }
            if ((sellContraband > 0 && (contact.SellPrices?.Contraband == null)) ||
                (sellCargo > 0 && (contact.SellPrices?.Cargo == null)))
            {
                deck.PutOnBottom(considered);
                error = $"{contact.Name} does not buy that good.";
                return false;
            }

            var cash = 0;
            if (sellContraband > 0)
                cash += sellContraband * contact.SellPrices!.Contraband!.Value;
            if (sellCargo > 0)
                cash += sellCargo * contact.SellPrices!.Cargo!.Value;

            var warrantsCleared = false;
            if (clearWarrants)
            {
                if (!contact.IsBadger || !player.IsSolidWith(contact.Id))
                {
                    deck.PutOnBottom(considered);
                    error = "Only a Solid Deal with Badger can clear warrants.";
                    return false;
                }
                if (player.Cash + cash < BadgerWarrantClearCost)
                {
                    deck.PutOnBottom(considered);
                    error = "Not enough cash to clear warrants with Badger.";
                    return false;
                }
            }

            foreach (var job in considered)
            {
                if (kept != null && job.Id == kept.Id)
                    continue;
                deck.PutOnBottom(job);
            }

            if (kept != null)
                player.JobHand.Add(kept.Id);

            player.Contraband -= sellContraband;
            player.Cargo -= sellCargo;
            player.Cash += cash;

            if (clearWarrants)
            {
                player.Cash -= BadgerWarrantClearCost;
                player.Warrants = 0;
                warrantsCleared = true;
            }

            game.TryConsumeAction(TurnAction.Deal, out _);
            result = new DealResult(contact, considered, kept, sellContraband, sellCargo, cash, warrantsCleared);
            error = null;
            return true;
        }

        public static bool CanReachContact(
            GameState game,
            PlayerState player,
            ContactCard contact,
            out string? error)
        {
            error = null;
            if (contact.IsMrUniverse && player.IsSolidWith(contact.Id))
                return true;

            if (contact.IsHarken)
            {
                if (string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId) ||
                    game.Tokens.AllianceCruiserSectorId != player.SectorId)
                {
                    error = "Harken can only be Dealt with on the Alliance Cruiser.";
                    return false;
                }
                return true;
            }

            if (string.IsNullOrEmpty(contact.Planet) ||
                !game.Map.TryResolveName(contact.Planet, out var sector) ||
                sector.Id != player.SectorId)
            {
                error = $"Must be in {contact.Name}'s sector to Deal.";
                return false;
            }

            return true;
        }
    }
}
