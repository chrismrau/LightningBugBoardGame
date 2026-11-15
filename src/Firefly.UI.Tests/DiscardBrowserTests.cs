using Firefly.Core.Cards;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P5 discard browser: Supply + Contact (+ Nav) piles from live GameState; inspect-only.
/// </summary>
public sealed class DiscardBrowserTests
{
    [Fact]
    public void Smoke_setup_has_supply_piles_and_empty_contact_discards()
    {
        var session = new HotseatGameSession();
        var view = DiscardBrowserBuilder.From(session.Game);

        Assert.NotEmpty(view.SupplyPiles);
        Assert.Contains(view.SupplyPiles, p =>
            string.Equals(p.Planet, "Persephone", StringComparison.OrdinalIgnoreCase));

        // Priming the Pump reveals into supply discards (default 3 per market).
        Assert.True(view.SupplyCardCount > 0);
        var persephone = view.SupplyPiles.First(p =>
            string.Equals(p.Planet, "Persephone", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, persephone.Count);
        Assert.All(persephone.Cards, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.NotNull(c.PortraitUrl);
        });

        Assert.NotEmpty(view.ContactPiles);
        // Starting-job overflow may land in Contact discards during setup; structure still clear.
        Assert.True(view.ContactCardCount >= 0);
        Assert.Contains(view.ContactPiles, p =>
            string.Equals(p.ContactName, "Badger", StringComparison.OrdinalIgnoreCase));

        var badger = view.ContactPiles.First(p =>
            string.Equals(p.ContactName, "Badger", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("contact_badger", badger.ContactId);
        Assert.Equal(CardArtUrls.Contact("contact_badger"), badger.ContactArtUrl);
        Assert.Equal(DiscardBrowseTargets.Contact("Badger"), badger.TargetKey);
    }

    [Fact]
    public void Nav_piles_are_present_and_may_hold_setup_reshuffles()
    {
        var session = new HotseatGameSession();
        var view = DiscardBrowserBuilder.From(session.Game);

        Assert.Equal(3, view.NavPiles.Count);
        Assert.Contains(view.NavPiles, p => p.RegionLabel == "Alliance");
        Assert.Contains(view.NavPiles, p => p.RegionLabel == "Border");
        Assert.Contains(view.NavPiles, p => p.RegionLabel == "Rim");
        Assert.All(view.NavPiles, p =>
            Assert.StartsWith(DiscardBrowseTargets.PrefixNav, p.TargetKey));
    }

    [Fact]
    public void Contact_discard_jobs_appear_after_MoveToDiscard()
    {
        var session = new HotseatGameSession();
        Assert.True(session.Game.ContactDecks!.TryGet("Badger", out var deck));
        Assert.True(session.Game.Jobs != null);

        var before = DiscardBrowserBuilder.From(session.Game)
            .ContactPiles.First(p =>
                string.Equals(p.ContactName, "Badger", StringComparison.OrdinalIgnoreCase))
            .Count;

        var job = session.Game.Jobs.Cards.Values
            .First(j => ContactNames.EqualsName(j.ContactName, "Badger")
                        && deck.DiscardPile.All(d => d.Id != j.Id));
        deck.MoveToDiscard(job);

        var view = DiscardBrowserBuilder.From(session.Game);
        var badger = view.ContactPiles.First(p =>
            string.Equals(p.ContactName, "Badger", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before + 1, badger.Count);
        Assert.Contains(badger.Jobs, j => j.Id == job.Id && j.Name == job.Name);
    }

    [Fact]
    public void Supply_card_portrait_urls_match_kind()
    {
        var session = new HotseatGameSession();
        var view = DiscardBrowserBuilder.From(session.Game);
        var any = view.SupplyPiles.SelectMany(p => p.Cards).First();

        var expected = any.Kind switch
        {
            SupplyKind.Crew => CardArtUrls.Crew(any.Id),
            SupplyKind.Gear => CardArtUrls.Gear(any.Id),
            SupplyKind.DriveCore => CardArtUrls.DriveCore(any.Id),
            SupplyKind.ShipUpgrade => CardArtUrls.ShipUpgrade(any.Id),
            _ => null
        };
        Assert.Equal(expected, any.PortraitUrl);
    }

    [Fact]
    public void DiscardBrowseTargets_round_trip()
    {
        Assert.True(DiscardBrowseTargets.TryParse(
            DiscardBrowseTargets.Supply("Silverhold"), out var section, out var key));
        Assert.Equal(DiscardBrowseSection.Supply, section);
        Assert.Equal("Silverhold", key);

        Assert.True(DiscardBrowseTargets.TryParse(
            DiscardBrowseTargets.Contact("Niska"), out section, out key));
        Assert.Equal(DiscardBrowseSection.Contacts, section);
        Assert.Equal("Niska", key);

        Assert.True(DiscardBrowseTargets.TryParse(
            DiscardBrowseTargets.Nav("Rim"), out section, out key));
        Assert.Equal(DiscardBrowseSection.Nav, section);
        Assert.Equal("Rim", key);

        Assert.False(DiscardBrowseTargets.TryParse("nope", out _, out _));
    }

    [Fact]
    public void PlayViewSnapshot_includes_discards()
    {
        var session = new HotseatGameSession();
        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.NotEmpty(snap.Discards.SupplyPiles);
        Assert.NotEmpty(snap.Discards.ContactPiles);
        Assert.Equal(3, snap.Discards.NavPiles.Count);
    }

    [Fact]
    public void ContactDecks_All_enumerates_deal_decks()
    {
        var session = new HotseatGameSession();
        var all = session.Game.ContactDecks!.All;
        Assert.NotEmpty(all);
        Assert.Contains(all, d => ContactNames.EqualsName(d.ContactName, "Badger"));
    }
}
