using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>
/// Inspect-only discard browser (P5). Supply + Contact piles are face-up by rules;
/// browsing never consumes a turn action. Deep-link via <see cref="DiscardBrowseTargets"/>
/// so P6 Buy/Deal can open the same piles.
/// </summary>
public sealed class DiscardBrowserView
{
    public required IReadOnlyList<SupplyDiscardPileView> SupplyPiles { get; init; }
    public required IReadOnlyList<ContactDiscardPileView> ContactPiles { get; init; }
    public required IReadOnlyList<NavDiscardPileView> NavPiles { get; init; }
    public required int SupplyCardCount { get; init; }
    public required int ContactCardCount { get; init; }
    public required int NavCardCount { get; init; }
    public int TotalCount => SupplyCardCount + ContactCardCount + NavCardCount;
}

public enum DiscardBrowseSection
{
    Supply,
    Contacts,
    Nav
}

/// <summary>Stable deep-link keys for discard piles (P5 inspect / P6 Buy·Deal).</summary>
public static class DiscardBrowseTargets
{
    public const string PrefixSupply = "supply:";
    public const string PrefixContact = "contact:";
    public const string PrefixNav = "nav:";

    public static string Supply(string planet) => PrefixSupply + planet;
    public static string Contact(string contactNameOrId) => PrefixContact + contactNameOrId;
    public static string Nav(string regionLabel) => PrefixNav + regionLabel;

    public static bool TryParse(
        string? target,
        out DiscardBrowseSection section,
        out string pileKey)
    {
        section = DiscardBrowseSection.Supply;
        pileKey = "";
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (target.StartsWith(PrefixSupply, StringComparison.OrdinalIgnoreCase))
        {
            section = DiscardBrowseSection.Supply;
            pileKey = target[PrefixSupply.Length..].Trim();
            return pileKey.Length > 0;
        }

        if (target.StartsWith(PrefixContact, StringComparison.OrdinalIgnoreCase))
        {
            section = DiscardBrowseSection.Contacts;
            pileKey = target[PrefixContact.Length..].Trim();
            return pileKey.Length > 0;
        }

        if (target.StartsWith(PrefixNav, StringComparison.OrdinalIgnoreCase))
        {
            section = DiscardBrowseSection.Nav;
            pileKey = target[PrefixNav.Length..].Trim();
            return pileKey.Length > 0;
        }

        return false;
    }
}

public sealed record SupplyDiscardCardView(
    string Id,
    string Name,
    SupplyKind Kind,
    int Cost,
    string? PortraitUrl);

public sealed record SupplyDiscardPileView(
    string Planet,
    string TargetKey,
    IReadOnlyList<SupplyDiscardCardView> Cards)
{
    public int Count => Cards.Count;
}

public sealed record ContactDiscardJobView(
    string Id,
    string Name,
    bool Legal,
    bool Immoral,
    string? PayLabel,
    string? JobType);

public sealed record ContactDiscardPileView(
    string ContactId,
    string ContactName,
    string TargetKey,
    string? ContactArtUrl,
    IReadOnlyList<ContactDiscardJobView> Jobs)
{
    public int Count => Jobs.Count;
}

public sealed record NavDiscardCardView(
    string Id,
    string Name,
    string Type,
    bool IsReshuffle);

public sealed record NavDiscardPileView(
    string RegionLabel,
    string TargetKey,
    IReadOnlyList<NavDiscardCardView> Cards)
{
    public int Count => Cards.Count;
}

public static class DiscardBrowserBuilder
{
    public static DiscardBrowserView From(GameState game)
    {
        var supply = BuildSupply(game);
        var contacts = BuildContacts(game);
        var nav = BuildNav(game);
        return new DiscardBrowserView
        {
            SupplyPiles = supply,
            ContactPiles = contacts,
            NavPiles = nav,
            SupplyCardCount = supply.Sum(p => p.Count),
            ContactCardCount = contacts.Sum(p => p.Count),
            NavCardCount = nav.Sum(p => p.Count)
        };
    }

    private static IReadOnlyList<SupplyDiscardPileView> BuildSupply(GameState game)
    {
        if (game.SupplyDecks == null)
            return Array.Empty<SupplyDiscardPileView>();

        return game.SupplyDecks.Markets
            .OrderBy(m => m.Planet, StringComparer.OrdinalIgnoreCase)
            .Select(m => new SupplyDiscardPileView(
                m.Planet,
                DiscardBrowseTargets.Supply(m.Planet),
                m.Discard
                    .Select(c => new SupplyDiscardCardView(
                        c.Id,
                        c.Name,
                        c.Kind,
                        c.Cost,
                        PortraitForSupply(c)))
                    .ToList()))
            .ToList();
    }

    private static IReadOnlyList<ContactDiscardPileView> BuildContacts(GameState game)
    {
        if (game.ContactDecks == null)
            return Array.Empty<ContactDiscardPileView>();

        var piles = new List<ContactDiscardPileView>();
        foreach (var deck in game.ContactDecks.All
                     .OrderBy(d => d.ContactName, StringComparer.OrdinalIgnoreCase))
        {
            string contactId = deck.ContactName;
            string contactName = deck.ContactName;
            if (game.Contacts != null
                && (game.Contacts.TryFindByName(deck.ContactName, out var contact)
                    || game.Contacts.TryGet(deck.ContactName, out contact)))
            {
                contactId = contact.Id;
                contactName = contact.Name;
            }

            var jobs = deck.DiscardPile
                .Select(j => new ContactDiscardJobView(
                    j.Id,
                    j.Name,
                    j.Legal,
                    j.Immoral,
                    PayLabel(j),
                    j.JobType))
                .ToList();

            piles.Add(new ContactDiscardPileView(
                contactId,
                contactName,
                DiscardBrowseTargets.Contact(contactName),
                CardArtUrls.Contact(contactId),
                jobs));
        }

        return piles;
    }

    private static IReadOnlyList<NavDiscardPileView> BuildNav(GameState game)
    {
        if (game.Decks == null)
            return Array.Empty<NavDiscardPileView>();

        return new[]
        {
            ToNavPile("Alliance", game.Decks.Alliance),
            ToNavPile("Border", game.Decks.Border),
            ToNavPile("Rim", game.Decks.Rim)
        };
    }

    private static NavDiscardPileView ToNavPile(string label, NavDeck deck) =>
        new(
            label,
            DiscardBrowseTargets.Nav(label),
            deck.DiscardPile
                .Select(c => new NavDiscardCardView(c.Id, c.Name, c.Type, c.IsReshuffle))
                .ToList());

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
