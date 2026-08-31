using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class DealRequest
    {
        public string ContactName { get; set; } = "";
        public int ConsiderCount { get; set; }
        public IList<string> KeepFromConsidered { get; set; } = new List<string>();
        public IList<string> TakeFromDiscard { get; set; } = new List<string>();
        public int SellContraband { get; set; }
        public int SellCargo { get; set; }
        public bool ClearWarrants { get; set; }
    }

    public sealed class DealResult
    {
        public ContactCard Contact { get; }
        public bool Considered { get; }
        public IReadOnlyList<JobCard> Drawn { get; }
        public IReadOnlyList<JobCard> KeptFromConsider { get; }
        public IReadOnlyList<JobCard> TakenFromDiscard { get; }
        public int ContrabandSold { get; }
        public int CargoSold { get; }
        public int CashFromSales { get; }
        public bool WarrantsCleared { get; }

        public DealResult(
            ContactCard contact,
            bool considered,
            IReadOnlyList<JobCard> drawn,
            IReadOnlyList<JobCard> keptFromConsider,
            IReadOnlyList<JobCard> takenFromDiscard,
            int contrabandSold,
            int cargoSold,
            int cashFromSales,
            bool warrantsCleared)
        {
            Contact = contact;
            Considered = considered;
            Drawn = drawn;
            KeptFromConsider = keptFromConsider;
            TakenFromDiscard = takenFromDiscard;
            ContrabandSold = contrabandSold;
            CargoSold = cargoSold;
            CashFromSales = cashFromSales;
            WarrantsCleared = warrantsCleared;
        }
    }

    public sealed class DealAction
    {
        public bool TryDeal(GameState game, string playerId, DealRequest request, out DealResult? result, out string? error)
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
            if (request == null || string.IsNullOrWhiteSpace(request.ContactName))
            {
                error = "A contact is required.";
                return false;
            }
            if (!game.Contacts.TryFindByName(request.ContactName, out var contact))
            {
                error = $"Unknown contact '{request.ContactName}'.";
                return false;
            }
            if (contact.IsHiggins && player.Roster.HasName("Jayne"))
            {
                error = "Higgins will not Deal while Jayne is in the crew.";
                return false;
            }

            var atLocation = IsAtContact(game, player, contact);
            if (!CanReachContact(game, player, contact, atLocation, out error))
                return false;
            if (!game.ContactDecks.TryGet(contact.Name, out var deck))
            {
                error = $"No job deck for {contact.Name}.";
                return false;
            }

            var remote = !atLocation;
            if (remote && (request.SellContraband > 0 || request.SellCargo > 0 || request.ClearWarrants))
            {
                error = "Selling and Badger's warrant wipe require being in the Contact's sector.";
                return false;
            }

            var limit = ConsiderLimit(player, contact, remote);
            if (request.ConsiderCount < 0)
            {
                error = "Consider count cannot be negative.";
                return false;
            }
            if (request.ConsiderCount > limit)
            {
                error = $"May consider at most {limit} job(s) with this Contact.";
                return false;
            }

            var considering = request.ConsiderCount > 0;
            var keepIds = request.KeepFromConsidered ?? new List<string>();
            var discardIds = request.TakeFromDiscard ?? new List<string>();
            var maxKeep = player.Deal.MaxKeepFromConsider;

            if (!considering && keepIds.Count > 0)
            {
                error = "Cannot keep considered jobs unless cards were drawn.";
                return false;
            }
            if (keepIds.Count > maxKeep)
            {
                error = $"May take at most {maxKeep} jobs from those considered.";
                return false;
            }

            var drawn = considering ? deck.DrawConsider(request.ConsiderCount) : (IReadOnlyList<JobCard>)new List<JobCard>();
            var kept = new List<JobCard>();
            foreach (var id in keepIds)
            {
                JobCard? match = null;
                foreach (var job in drawn)
                {
                    if (job.Id == id) { match = job; break; }
                }
                if (match == null)
                {
                    deck.PutOnBottom(drawn);
                    error = $"Job '{id}' was not among the considered cards.";
                    return false;
                }
                if (ContainsId(kept, id))
                {
                    deck.PutOnBottom(drawn);
                    error = "Cannot keep the same considered job twice.";
                    return false;
                }
                kept.Add(match);
            }

            var fromDiscard = new List<JobCard>();
            foreach (var id in discardIds)
            {
                if (!deck.TryTakeFromDiscard(id, out var job))
                {
                    foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = $"Job '{id}' is not in {contact.Name}'s discard pile.";
                    return false;
                }
                fromDiscard.Add(job);
            }

            if (player.JobHand.Count + kept.Count + fromDiscard.Count > player.JobHandLimit)
            {
                foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = $"Job hand is full ({player.JobHandLimit}).";
                return false;
            }

            if (request.SellContraband < 0 || request.SellCargo < 0)
            {
                foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Cannot sell a negative quantity.";
                return false;
            }
            if (request.SellContraband > player.Contraband || request.SellCargo > player.Cargo)
            {
                foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Not enough cargo or contraband to sell.";
                return false;
            }
            if ((request.SellContraband > 0 && contact.SellPrices?.Contraband == null) ||
                (request.SellCargo > 0 && contact.SellPrices?.Cargo == null))
            {
                foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = $"{contact.Name} does not buy that good.";
                return false;
            }

            var cash = 0;
            if (request.SellContraband > 0)
                cash += request.SellContraband * contact.SellPrices!.Contraband!.Value;
            if (request.SellCargo > 0)
                cash += request.SellCargo * contact.SellPrices!.Cargo!.Value;

            if (request.ClearWarrants)
            {
                if (!contact.IsBadger || !player.IsSolidWith(contact.Id))
                {
                    foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Only a Solid Deal with Badger can clear warrants.";
                    return false;
                }
                if (player.Cash + cash < DealActionDefaults.BadgerWarrantClearCost)
                {
                    foreach (var taken in fromDiscard) deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Not enough cash to clear warrants with Badger.";
                    return false;
                }
            }

            foreach (var job in drawn)
            {
                if (!ContainsId(kept, job.Id))
                    deck.PutOnBottom(job);
            }
            foreach (var job in kept) player.JobHand.Add(job.Id);
            foreach (var job in fromDiscard) player.JobHand.Add(job.Id);
            player.Contraband -= request.SellContraband;
            player.Cargo -= request.SellCargo;
            player.Cash += cash;
            var warrantsCleared = false;
            if (request.ClearWarrants)
            {
                player.Cash -= DealActionDefaults.BadgerWarrantClearCost;
                player.Warrants = 0;
                warrantsCleared = true;
            }

            game.TryConsumeAction(TurnAction.Deal, out _);
            result = new DealResult(contact, considering, drawn, kept, fromDiscard, request.SellContraband, request.SellCargo, cash, warrantsCleared);
            error = null;
            return true;
        }

        public static int ConsiderLimit(PlayerState player, ContactCard contact, bool remote)
        {
            if (remote && player.Deal.ConsiderTopCardFromAnyContact && !player.IsSolidWith(contact.Id))
                return DealActionDefaults.CortexUplinkConsider;

            var limit = DealActionDefaults.BaseConsider;
            if (contact.IsPatience && player.IsSolidWith(contact.Id))
                limit = DealActionDefaults.PatienceSolidConsider;
            if (player.Deal.ConsiderUpTo.HasValue && player.Deal.ConsiderUpTo.Value > limit)
                limit = player.Deal.ConsiderUpTo.Value;
            limit += player.Deal.ExtraConsider;
            return limit < 0 ? 0 : limit;
        }

        public static bool CanReachContact(GameState game, PlayerState player, ContactCard contact, bool atLocation, out string? error)
        {
            error = null;
            if (atLocation) return true;
            if (contact.IsMrUniverse && player.IsSolidWith(contact.Id)) return true;
            if (player.Deal.CanDealFromAnySector || player.Deal.ConsiderTopCardFromAnyContact) return true;
            error = contact.IsHarken
                ? "Harken can only be Dealt with on the Alliance Cruiser."
                : $"Must be in {contact.Name}'s sector to Deal.";
            return false;
        }

        public static bool IsAtContact(GameState game, PlayerState player, ContactCard contact)
        {
            if (contact.IsHarken)
                return !string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId)
                    && game.Tokens.AllianceCruiserSectorId == player.SectorId;
            return !string.IsNullOrEmpty(contact.Planet)
                && game.Map.TryResolveName(contact.Planet, out var sector)
                && sector.Id == player.SectorId;
        }

        private static bool ContainsId(List<JobCard> jobs, string id)
        {
            foreach (var job in jobs)
            {
                if (job.Id == id) return true;
            }
            return false;
        }
    }
}
