using Firefly.Core.Abilities;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>Hotseat Deal / Buy / Shore Leave chrome modes (P6).</summary>
public enum EconomyUiMode
{
    None,
    DealPick,
    DealConsider,
    BuyPick,
    BetterOfferPick
}

public sealed record DealContactOption(
    string Name,
    string Id,
    string? Planet,
    bool AtLocation,
    bool Reachable,
    string? BlockReason,
    string? ArtUrl,
    int ConsiderLimit,
    int DiscardCount,
    int? SellContrabandPrice,
    int? SellCargoPrice,
    bool CanClearWarrants);

public sealed record DealDiscardJobOption(
    string Id,
    string Name,
    bool Legal,
    bool Immoral,
    string? PayLabel);

public sealed record DealConsideredJobView(
    string Id,
    string Name,
    bool Legal,
    bool Immoral,
    string? PayLabel);

public sealed record DealChromeView(
    EconomyUiMode Mode,
    string? SelectedContact,
    IReadOnlyList<DealContactOption> Contacts,
    IReadOnlyList<DealDiscardJobOption> DiscardJobs,
    int ConsiderLimit,
    int MaxKeep,
    int JobHandCount,
    int JobHandLimit,
    int Cargo,
    int Contraband,
    int Parts,
    int Cash,
    int Warrants,
    bool SolidWithSelected,
    int? SellContrabandPrice,
    int? SellCargoPrice,
    bool CanClearWarrants,
    IReadOnlyList<DealConsideredJobView> Considered,
    string? StatusHint);

public sealed record BuyFaceUpCardView(
    string Id,
    string Name,
    SupplyKind Kind,
    int PrintedCost,
    int EffectiveCost,
    string? PortraitUrl);

public sealed record BuyChromeView(
    EconomyUiMode Mode,
    string? Planet,
    bool AtSupplyPlanet,
    string? BlockReason,
    int Cash,
    int Fuel,
    int Parts,
    int FuelPrice,
    int PartsPrice,
    int MaxBuyCards,
    IReadOnlyList<BuyFaceUpCardView> FaceUp,
    string? StatusHint);

public sealed record BetterOfferTargetView(
    string RivalPlayerId,
    string RivalName,
    string CrewId,
    string CrewName,
    int Cost);

public sealed record BetterOfferChromeView(
    EconomyUiMode Mode,
    IReadOnlyList<BetterOfferTargetView> Targets,
    string? StatusHint);

public sealed record DealPickSubmission(
    string ContactName,
    int ConsiderCount,
    IReadOnlyList<string> TakeFromDiscard,
    int SellContraband,
    int SellCargo,
    bool ClearWarrants);

public sealed record DealKeepSubmission(IReadOnlyList<string> KeepJobIds);

public sealed record BuyPickSubmission(
    int Fuel,
    int Parts,
    IReadOnlyList<string> SupplyCardIds);

/// <summary>Builds Deal / Buy / Better Offer chrome from kernel state — no invented prices.</summary>
public static class EconomyChromeBuilder
{
    public static DealChromeView BuildDeal(
        GameState game,
        string? selectedContact,
        IReadOnlyList<DealConsideredJobView>? considered = null)
    {
        var player = game.CurrentPlayer;
        var contacts = ListDealContacts(game, player);
        DealContactOption? selected = null;
        if (!string.IsNullOrWhiteSpace(selectedContact))
        {
            foreach (var c in contacts)
            {
                if (string.Equals(c.Name, selectedContact, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Id, selectedContact, StringComparison.OrdinalIgnoreCase))
                {
                    selected = c;
                    break;
                }
            }
        }

        selected ??= contacts.FirstOrDefault(c => c.AtLocation && c.Reachable)
                     ?? contacts.FirstOrDefault(c => c.Reachable);

        var discard = Array.Empty<DealDiscardJobOption>();
        var considerLimit = DealActionDefaults.BaseConsider;
        var solid = false;
        int? sellContra = null;
        int? sellCargo = null;
        var canClear = false;
        if (selected != null
            && game.Contacts != null
            && game.Contacts.TryFindByName(selected.Name, out var contactCard))
        {
            considerLimit = selected.ConsiderLimit;
            solid = ContactSolidBenefits.CountsAsSolidWith(game, player, contactCard);
            sellContra = contactCard.SellPrices?.Contraband;
            sellCargo = contactCard.SellPrices?.Cargo;
            canClear = contactCard.IsBadger && solid;
            if (game.ContactDecks != null
                && game.ContactDecks.TryGet(selected.Name, out var deck))
            {
                discard = deck.DiscardPile
                    .Select(j => new DealDiscardJobOption(
                        j.Id,
                        j.Name,
                        j.Legal,
                        j.Immoral,
                        PayLabel(j)))
                    .ToArray();
            }
        }

        var mode = considered is { Count: > 0 }
            ? EconomyUiMode.DealConsider
            : EconomyUiMode.DealPick;

        return new DealChromeView(
            mode,
            selected?.Name,
            contacts,
            discard,
            considerLimit,
            player.Deal.MaxKeepFromConsider,
            player.JobHand.Count,
            player.JobHandLimit,
            player.Cargo,
            player.Contraband,
            player.Parts,
            player.Cash,
            player.Warrants,
            solid,
            sellContra,
            sellCargo,
            canClear,
            considered ?? Array.Empty<DealConsideredJobView>(),
            mode == EconomyUiMode.DealConsider
                ? $"Pick up to {player.Deal.MaxKeepFromConsider} job(s) to keep (rest return to the bottom)."
                : "Deal with a Contact — consider jobs and/or take from the public discard.");
    }

    public static BuyChromeView BuildBuy(GameState game)
    {
        var player = game.CurrentPlayer;
        if (!game.Map.TryGet(player.SectorId, out var sector))
        {
            return new BuyChromeView(
                EconomyUiMode.BuyPick,
                null,
                false,
                "Unknown sector.",
                player.Cash,
                player.Fuel,
                player.Parts,
                BuyAction.FuelPrice,
                BuyAction.PartsPrice,
                BuyActionDefaults.MaxBuyCards,
                Array.Empty<BuyFaceUpCardView>(),
                null);
        }

        var planet = BuyAction.ShopPlanet(sector);
        SupplyMarket? market = null;
        var atSupply = !string.IsNullOrWhiteSpace(planet)
            && game.SupplyDecks != null
            && game.SupplyDecks.TryGet(planet!, out market)
            && sector.HasSupplyDeck;

        string? block = null;
        if (!atSupply)
            block = "Must be at a Supply planet to Buy.";
        else if (planet != null)
            block = ActiveAlertRules.BuyBlockReason(game, player, planet);

        var maxCards = AbilityDispatcher.BuySupplyCardsUpTo(game, player);
        if (maxCards <= 0)
            maxCards = BuyActionDefaults.MaxBuyCards;

        var faceUp = Array.Empty<BuyFaceUpCardView>();
        if (market != null)
        {
            faceUp = market.FaceUp
                .Select(c => new BuyFaceUpCardView(
                    c.Id,
                    c.Name,
                    c.Kind,
                    c.Cost,
                    EffectiveBuyCost(game, player, c),
                    PortraitForSupply(c)))
                .ToArray();
        }

        return new BuyChromeView(
            EconomyUiMode.BuyPick,
            planet,
            atSupply && block == null,
            block,
            player.Cash,
            player.Fuel,
            player.Parts,
            BuyAction.FuelPrice,
            BuyAction.PartsPrice,
            maxCards,
            faceUp,
            atSupply && block == null
                ? $"Buy at {planet} — Fuel ${BuyAction.FuelPrice}, Parts ${BuyAction.PartsPrice}, up to {maxCards} Supply card(s)."
                : block);
    }

    public static BetterOfferChromeView BuildBetterOffer(GameState game)
    {
        var targets = new List<BetterOfferTargetView>();
        var player = game.CurrentPlayer;
        foreach (var rival in game.Players)
        {
            if (string.Equals(rival.Id, player.Id, StringComparison.Ordinal))
                continue;
            if (rival.SectorId != player.SectorId)
                continue;
            foreach (var member in rival.Roster.Members)
            {
                if (member.IsLeader || !member.Disgruntled)
                    continue;
                var cost = Math.Max(0, member.Card.Cost);
                if (player.Cash < cost)
                    continue;
                if (player.Roster.Count >= player.Roster.MaxCrew)
                    continue;
                targets.Add(new BetterOfferTargetView(
                    rival.Id,
                    rival.Name,
                    member.Id,
                    member.Name,
                    cost));
            }
        }

        return new BetterOfferChromeView(
            EconomyUiMode.BetterOfferPick,
            targets,
            targets.Count == 0
                ? "No Disgruntled rival crew in this sector."
                : "Pay hiring cost to the bank — not an Action (FAQ 4.1).");
    }

    public static int ShoreLeaveCost(GameState game)
    {
        var player = game.CurrentPlayer;
        if (!game.Map.TryGet(player.SectorId, out var sector))
            return ShoreLeaveAction.CostFor(player);

        var free = (game.Scenario != null
                && game.Scenario.FriendsInLowPlaces
                && HavenRules.IsOwnHaven(player, player.SectorId))
            || (AbilityDispatcher.HasFreeShoreLeaveAtSupply(player) && sector.HasSupplyDeck);
        return free ? 0 : ShoreLeaveAction.CostFor(player);
    }

    /// <summary>
    /// Shore Leave legality beyond Buy action gate — mirrors <see cref="ShoreLeaveAction.TryShoreLeave"/> gates.
    /// </summary>
    public static (bool Ok, string Reason) ShoreLeaveLegality(GameState game, bool navFaceUpBlocking = false)
    {
        if (navFaceUpBlocking)
            return (false, "Resolve the face-up Nav card before taking another action.");
        if (!game.CanTakeAction(TurnAction.Buy, out var error))
            return (false, error ?? "Buy action not available.");

        var player = game.CurrentPlayer;
        if (!game.Map.TryGet(player.SectorId, out var sector))
            return (false, $"Unknown sector '{player.SectorId}'.");

        var anySector = AbilityDispatcher.HasShoreLeaveAnySector(game, player);
        if (!anySector && !ShoreLeaveAction.HasPlanet(sector))
            return (false, "Shore Leave requires a Planet in this sector.");

        var planet = sector.Planet ?? sector.DisplayName;
        var buyBlock = ActiveAlertRules.BuyBlockReason(game, player, planet);
        if (buyBlock != null)
            return (false, buyBlock);

        if (player.Roster.Count == 0)
            return (false, "Shore Leave requires crew on the ship.");

        var cost = ShoreLeaveCost(game);
        if (player.Cash < cost)
            return (false, $"Need ${cost} for Shore Leave ({player.Roster.Count} crew).");

        return (true, "");
    }

    private static IReadOnlyList<DealContactOption> ListDealContacts(GameState game, PlayerState player)
    {
        if (game.Contacts == null)
            return Array.Empty<DealContactOption>();

        var list = new List<DealContactOption>();
        foreach (var contact in game.Contacts.Cards.Values
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var at = DealAction.IsAtContact(game, player, contact);
            var reachable = DealAction.CanReachContact(game, player, contact, at, out var reachError);
            var block = reachError;
            if (contact.IsHiggins && player.Roster.HasName("Jayne"))
            {
                reachable = false;
                block = "Higgins will not Deal while Jayne is in the crew.";
            }
            else if (ActiveAlertRules.BlocksDealWith(game, contact.Name))
            {
                reachable = false;
                block = $"Alliance Audit: cannot Deal with {contact.Name} until the Alert moves.";
            }

            var discardCount = 0;
            if (game.ContactDecks != null && game.ContactDecks.TryGet(contact.Name, out var deck))
                discardCount = deck.DiscardCount;

            list.Add(new DealContactOption(
                contact.Name,
                contact.Id,
                contact.Planet,
                at,
                reachable,
                reachable ? null : block,
                CardArtUrls.Contact(contact.Id),
                DealAction.ConsiderLimit(player, contact, remote: !at),
                discardCount,
                contact.SellPrices?.Contraband,
                contact.SellPrices?.Cargo,
                contact.IsBadger && ContactSolidBenefits.CountsAsSolidWith(game, player, contact)));
        }

        // Prefer local contacts first in the picker.
        return list
            .OrderByDescending(c => c.AtLocation)
            .ThenByDescending(c => c.Reachable)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int EffectiveBuyCost(GameState game, PlayerState player, SupplyCard card)
    {
        if (card.Kind == SupplyKind.Crew && AbilityDispatcher.HasFreeHireCrew(player))
            return 0;
        return BuyAction.CardBuyCost(game, player, card);
    }

    private static string? PortraitForSupply(SupplyCard card) => card.Kind switch
    {
        SupplyKind.Crew => CardArtUrls.Crew(card.Id),
        SupplyKind.Gear => CardArtUrls.Gear(card.Id),
        SupplyKind.DriveCore => CardArtUrls.DriveCore(card.Id),
        SupplyKind.ShipUpgrade => CardArtUrls.ShipUpgrade(card.Id),
        _ => null
    };

    private static string? PayLabel(JobCard job) =>
        job.PayBase != null
            ? $"${job.PayBase:N0}"
            : (string.IsNullOrWhiteSpace(job.PayRaw) ? null : job.PayRaw);
}
