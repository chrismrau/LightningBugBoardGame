using System.Collections.Generic;
using Firefly.Core.Abilities;
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
        /// <summary>FAQ 4.1 Amnon Travel Hub: load as part of Deal when Solid.</summary>
        public int LoadPassengers { get; set; }
        public int LoadFugitives { get; set; }
        /// <summary>Kalidasa: buy Contraband from Fanty when Solid. Blue Sun: buy Cargo from Harrow when Solid.</summary>
        public int BuyContraband { get; set; }
        public int BuyCargo { get; set; }
        /// <summary>
        /// FAQ 4.1: when Solid with Harken, buy Fuel for $100 each while Dealing with Harken.
        /// </summary>
        public int BuyFuel { get; set; }
        /// <summary>
        /// Universal Encyclopedia: reorder top Misbehave ids (first = new top). Empty = skip.
        /// </summary>
        public IList<string> MisbehaveReorderIds { get; set; } = new List<string>();
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
        public int PassengersLoaded { get; }
        public int FugitivesLoaded { get; }
        public int ContrabandBought { get; }
        public int CargoBought { get; }
        public int FuelBought { get; }
        public int CashSpentBuying { get; }

        public DealResult(
            ContactCard contact,
            bool considered,
            IReadOnlyList<JobCard> drawn,
            IReadOnlyList<JobCard> keptFromConsider,
            IReadOnlyList<JobCard> takenFromDiscard,
            int contrabandSold,
            int cargoSold,
            int cashFromSales,
            bool warrantsCleared,
            int passengersLoaded = 0,
            int fugitivesLoaded = 0,
            int contrabandBought = 0,
            int cargoBought = 0,
            int cashSpentBuying = 0,
            int fuelBought = 0)
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
            PassengersLoaded = passengersLoaded;
            FugitivesLoaded = fugitivesLoaded;
            ContrabandBought = contrabandBought;
            CargoBought = cargoBought;
            CashSpentBuying = cashSpentBuying;
            FuelBought = fuelBought;
        }
    }

    /// <summary>
    /// Deal with a Contact. Discard piles are public at all times and taking from
    /// discard is not Considering. Considering starts only when at least one card
    /// is drawn from that Contact's facedown deck. Default: consider up to 3, keep 0-2.
    /// </summary>
    public sealed class DealAction
    {
        public bool TryDeal(
            GameState game,
            string playerId,
            DealRequest request,
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
            if (ActiveAlertRules.BlocksDealWith(game, contact.Name))
            {
                error = $"Alliance Audit: cannot Deal with {contact.Name} until the Alert moves.";
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
            if (remote && (request.SellContraband > 0 || request.SellCargo > 0 || request.ClearWarrants
                || request.LoadPassengers > 0 || request.LoadFugitives > 0
                || request.BuyContraband > 0 || request.BuyCargo > 0 || request.BuyFuel > 0))
            {
                error = "Selling, buying goods, Amnon loading, and Badger's warrant wipe require being in the Contact's sector.";
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

            var reorder = request.MisbehaveReorderIds;
            if (reorder != null && reorder.Count > 0)
            {
                var reorderMax = player.Deal.ReorderMisbehaveTop;
                if (reorderMax <= 0)
                    reorderMax = AbilityDispatcher.DealReorderMisbehaveAmount(game, player);
                if (reorderMax <= 0)
                {
                    error = "Universal Encyclopedia is required to reorder Misbehave cards.";
                    return false;
                }
                if (game.Misbehave == null)
                {
                    error = "Misbehave deck is not loaded.";
                    return false;
                }
                var peeked = game.Misbehave.PeekTop(reorderMax);
                if (reorder.Count != peeked.Count)
                {
                    error = $"Misbehave reorder expects {peeked.Count} card id(s).";
                    return false;
                }
                if (!game.Misbehave.TryReorderTop(reorder, out error))
                    return false;
            }

            var drawn = considering
                ? deck.DrawConsider(request.ConsiderCount)
                : (IReadOnlyList<JobCard>)new List<JobCard>();

            var kept = new List<JobCard>();
            foreach (var id in keepIds)
            {
                JobCard? match = null;
                foreach (var job in drawn)
                {
                    if (job.Id == id)
                    {
                        match = job;
                        break;
                    }
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
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = $"Job '{id}' is not in {contact.Name}'s discard pile.";
                    return false;
                }
                fromDiscard.Add(job);
            }

            var incoming = kept.Count + fromDiscard.Count;
            if (player.JobHand.Count + incoming > player.JobHandLimit)
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = $"Job hand is full ({player.JobHandLimit}).";
                return false;
            }

            if (request.SellContraband < 0 || request.SellCargo < 0)
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Cannot sell a negative quantity.";
                return false;
            }
            if (request.SellCargo > 0 && ActiveAlertRules.BlocksSellCargo(game))
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Enhanced Inspection: you may not Sell Cargo to Contacts.";
                return false;
            }
            if (request.SellContraband > player.Contraband || request.SellCargo > player.Cargo)
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Not enough cargo or contraband to sell.";
                return false;
            }
            if ((request.SellContraband > 0 && contact.SellPrices?.Contraband == null) ||
                (request.SellCargo > 0 && contact.SellPrices?.Cargo == null))
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = $"{contact.Name} does not buy that good.";
                return false;
            }

            var cash = 0;
            if (request.SellContraband > 0)
                cash += request.SellContraband * contact.SellPrices!.Contraband!.Value;
            if (request.SellCargo > 0)
                cash += request.SellCargo * contact.SellPrices!.Cargo!.Value;

            var warrantsCleared = false;
            if (request.ClearWarrants)
            {
                if (!contact.IsBadger || !ContactSolidBenefits.CountsAsSolidWith(game, player, contact))
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Only a Solid Deal with Badger can clear warrants.";
                    return false;
                }
                if (player.Cash + cash < DealActionDefaults.BadgerWarrantClearCost)
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Not enough cash to clear warrants with Badger.";
                    return false;
                }
            }

            if (request.LoadPassengers < 0 || request.LoadFugitives < 0
                || request.BuyContraband < 0 || request.BuyCargo < 0 || request.BuyFuel < 0)
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Cannot load or buy a negative quantity.";
                return false;
            }

            // FAQ 4.1 p.6: Solid Amnon — load Passengers/Fugitives as part of Deal.
            if (request.LoadPassengers > 0 || request.LoadFugitives > 0)
            {
                if (!contact.IsAmnon || !ContactSolidBenefits.CountsAsSolidWith(game, player, contact))
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Only a Solid Deal with Amnon Duul can load Passengers and Fugitives.";
                    return false;
                }
            }

            var buyCost = 0;
            if (request.BuyContraband > 0)
            {
                if (!contact.IsFanty || !ContactSolidBenefits.CountsAsSolidWith(game, player, contact))
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Only a Solid Deal with Fanty & Mingo can buy Contraband.";
                    return false;
                }
                buyCost += request.BuyContraband * ContactSolidBenefits.FantyBuyContrabandPrice;
            }
            if (request.BuyCargo > 0)
            {
                if (!contact.IsHarrow || !ContactSolidBenefits.CountsAsSolidWith(game, player, contact))
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Only a Solid Deal with Lord Harrow can buy Cargo.";
                    return false;
                }
                buyCost += request.BuyCargo * ContactSolidBenefits.HarrowBuyCargoPrice;
            }
            // FAQ 4.1: "When you're Solid with Harken, the Alliance Cruiser becomes a
            // refueling station. You may purchase as much Fuel as you'd like from Harken
            // for $100 each, when Dealing with Harken." Price from Contacts.json buyPrices.
            if (request.BuyFuel > 0)
            {
                if (!contact.IsHarken || !ContactSolidBenefits.CountsAsSolidWith(game, player, contact))
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Only a Solid Deal with Harken can buy Fuel.";
                    return false;
                }
                if (contact.BuyPrices?.Fuel == null)
                {
                    foreach (var taken in fromDiscard)
                        deck.MoveToDiscard(taken);
                    deck.PutOnBottom(drawn);
                    error = "Harken has no printed Fuel buy price.";
                    return false;
                }
                buyCost += request.BuyFuel * contact.BuyPrices.Fuel.Value;
            }
            if ((request.LoadPassengers > 0 || request.LoadFugitives > 0
                    || request.BuyContraband > 0 || request.BuyCargo > 0 || request.BuyFuel > 0)
                && !HoldSpace.TryExplain(
                    player,
                    out var buyHoldError,
                    addFuel: request.BuyFuel,
                    addCargo: request.BuyCargo,
                    addContraband: request.BuyContraband,
                    addPassengers: request.LoadPassengers,
                    addFugitives: request.LoadFugitives))
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = buyHoldError;
                return false;
            }
            if (buyCost > 0 && player.Cash + cash - (request.ClearWarrants ? DealActionDefaults.BadgerWarrantClearCost : 0) < buyCost)
            {
                foreach (var taken in fromDiscard)
                    deck.MoveToDiscard(taken);
                deck.PutOnBottom(drawn);
                error = "Not enough cash to buy goods from this Contact.";
                return false;
            }

            foreach (var job in drawn)
            {
                if (ContainsId(kept, job.Id))
                    continue;
                deck.PutOnBottom(job);
            }

            foreach (var job in kept)
                player.JobHand.Add(job.Id);
            foreach (var job in fromDiscard)
                player.JobHand.Add(job.Id);

            player.Contraband -= request.SellContraband;
            player.Cargo -= request.SellCargo;
            player.Cash += cash;

            if (request.ClearWarrants)
            {
                player.Cash -= DealActionDefaults.BadgerWarrantClearCost;
                player.Warrants = 0;
                warrantsCleared = true;
            }

            player.Passengers += request.LoadPassengers;
            player.Fugitives += request.LoadFugitives;
            player.Contraband += request.BuyContraband;
            player.Cargo += request.BuyCargo;
            player.Fuel += request.BuyFuel;
            player.Cash -= buyCost;

            game.TryConsumeAction(TurnAction.Deal, out _);
            result = new DealResult(
                contact,
                considering,
                drawn,
                kept,
                fromDiscard,
                request.SellContraband,
                request.SellCargo,
                cash,
                warrantsCleared,
                request.LoadPassengers,
                request.LoadFugitives,
                request.BuyContraband,
                request.BuyCargo,
                buyCost,
                request.BuyFuel);
            error = null;
            return true;
        }

        public static int ConsiderLimit(PlayerState player, ContactCard contact, bool remote)
        {
            // Cortex Uplink remote consider is overridden when Solid with the Contact
            // (Mr. Universe any-sector Deal still uses full Solid consider limits).
            if (remote && player.Deal.ConsiderTopCardFromAnyContact && !player.IsSolidWith(contact.Id))
                return DealActionDefaults.CortexUplinkConsider;

            var limit = DealActionDefaults.BaseConsider;
            if (contact.IsPatience && player.IsSolidWith(contact.Id))
                limit = DealActionDefaults.PatienceSolidConsider;
            if (player.Deal.ConsiderUpTo.HasValue && player.Deal.ConsiderUpTo.Value > limit)
                limit = player.Deal.ConsiderUpTo.Value;
            limit += player.Deal.ExtraConsider;
            if (limit < 0)
                limit = 0;
            return limit;
        }

        public static bool CanReachContact(
            GameState game,
            PlayerState player,
            ContactCard contact,
            bool atLocation,
            out string? error)
        {
            error = null;
            if (atLocation)
                return true;
            if (contact.IsMrUniverse && player.IsSolidWith(contact.Id))
                return true;
            // Cortex Uplink (modifier or live gear query).
            if (player.Deal.ConsiderTopCardFromAnyContact
                || AbilityDispatcher.HasConsiderTopAnyContact(game, player))
                return true;
            // Universal Encyclopedia.
            if (player.Deal.ReorderMisbehaveTop > 0
                || AbilityDispatcher.HasDealReorderMisbehave(game, player))
                return true;
            // Fess: remote Deal only with the named Contact (Higgins).
            var named = player.Deal.NamedRemoteContact
                ?? AbilityDispatcher.FindDealWithNamedContact(player);
            if (!string.IsNullOrWhiteSpace(named) && ContactMatchesName(contact, named))
                return true;
            // Legacy remote Deal without a named-contact restriction.
            if (player.Deal.CanDealFromAnySector && string.IsNullOrWhiteSpace(named))
                return true;

            error = contact.IsHarken
                ? "Harken can only be Dealt with on the Alliance Cruiser."
                : $"Must be in {contact.Name}'s sector to Deal.";
            return false;
        }

        private static bool ContactMatchesName(ContactCard contact, string name)
        {
            if (contact.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (contact.Id.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("Higgins", System.StringComparison.OrdinalIgnoreCase) && contact.IsHiggins)
                return true;
            return contact.Id.IndexOf(name.Replace(" ", "-"), System.StringComparison.OrdinalIgnoreCase) >= 0;
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
                if (job.Id == id)
                    return true;
            }
            return false;
        }
    }
}
