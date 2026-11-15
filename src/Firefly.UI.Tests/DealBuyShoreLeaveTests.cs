using Firefly.Core.Actions;
using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P6 hotseat Deal / Buy / Shore Leave via kernel action hooks on <see cref="HotseatGameSession"/>.
/// </summary>
public sealed class DealBuyShoreLeaveTests
{
    private const string Persephone = "alliance-lux-r1-01";

    [Fact]
    public void ShoreLeave_consumes_Buy_and_clears_Disgruntled()
    {
        var session = new HotseatGameSession();
        var player = session.Game.CurrentPlayer;
        Assert.Equal(Persephone, player.SectorId);
        Assert.NotNull(player.Roster.Leader);
        player.Roster.Disgruntle(player.Roster.Leader!);
        var cashBefore = player.Cash;

        Assert.True(session.TryShoreLeave(out var error), error);
        Assert.Equal(TurnAction.Buy, session.Game.LastAction);
        Assert.False(player.Roster.Leader!.Disgruntled);
        Assert.Equal(cashBefore - ShoreLeaveAction.CostPerCrew, player.Cash);

        var buttons = ActionBarBuilder.Build(session.Game);
        Assert.Contains(buttons, b => b.Id == "buy" && !b.Enabled);
        Assert.Contains(buttons, b => b.Id == "shore-leave" && !b.Enabled);
    }

    [Fact]
    public void Buy_fuel_at_Persephone_via_session()
    {
        var session = new HotseatGameSession();
        var player = session.Game.CurrentPlayer;
        var fuelBefore = player.Fuel;
        var cashBefore = player.Cash;

        Assert.True(session.TryBuy(new BuyRequest { Fuel = 1 }, out var error), error);
        Assert.Equal(fuelBefore + 1, player.Fuel);
        Assert.Equal(cashBefore - BuyAction.FuelPrice, player.Cash);
        Assert.Equal(TurnAction.Buy, session.Game.LastAction);

        var buyView = EconomyChromeBuilder.BuildBuy(session.Game);
        Assert.Equal("Persephone", buyView.Planet);
        Assert.False(session.Game.CanTakeAction(TurnAction.Buy, out _));
    }

    [Fact]
    public void Buy_chrome_lists_face_up_supply_at_Persephone()
    {
        var session = new HotseatGameSession();
        var view = EconomyChromeBuilder.BuildBuy(session.Game);
        Assert.True(view.AtSupplyPlanet);
        Assert.Equal("Persephone", view.Planet);
        Assert.Equal(3, view.FaceUp.Count);
        Assert.Equal(BuyActionDefaults.MaxBuyCards, view.MaxBuyCards);
    }

    [Fact]
    public void Deal_without_consider_takes_from_Badger_discard()
    {
        var session = new HotseatGameSession();
        Assert.True(session.Game.ContactDecks!.TryGet("Badger", out var deck));
        // Seed a discard card if Priming/start deal left the pile empty.
        if (deck.DiscardCount == 0)
        {
            var drawn = deck.DrawConsider(1);
            Assert.Single(drawn);
            deck.MoveToDiscard(drawn[0]);
        }
        var jobId = deck.DiscardPile[0].Id;
        // Make room in the job hand (setup deals starting jobs up to the limit).
        session.Game.CurrentPlayer.JobHand.Clear();

        Assert.True(session.TryDeal(new DealRequest
        {
            ContactName = "Badger",
            ConsiderCount = 0,
            TakeFromDiscard = { jobId }
        }, out var error), error);

        Assert.Equal(TurnAction.Deal, session.Game.LastAction);
        Assert.Contains(jobId, session.Game.CurrentPlayer.JobHand);
    }

    [Fact]
    public void Deal_consider_staging_then_keep_one()
    {
        var session = new HotseatGameSession();
        session.Game.CurrentPlayer.JobHand.Clear();

        Assert.True(session.TryBeginDealConsider(new DealRequest
        {
            ContactName = "Badger",
            ConsiderCount = 3
        }, out var beginError), beginError);

        Assert.NotNull(session.DealConsideredJobs);
        Assert.Equal(3, session.DealConsideredJobs!.Count);
        Assert.False(session.Game.ActionWasUsed(TurnAction.Deal));

        var keepId = session.DealConsideredJobs[0].Id;
        Assert.True(session.TryFinishDealConsider(new[] { keepId }, out var finishError), finishError);
        Assert.Null(session.DealConsideredJobs);
        Assert.Equal(TurnAction.Deal, session.Game.LastAction);
        Assert.Contains(keepId, session.Game.CurrentPlayer.JobHand);
    }

    [Fact]
    public void Deal_consider_cancel_restores_deck_without_action()
    {
        var session = new HotseatGameSession();
        Assert.True(session.Game.ContactDecks!.TryGet("Badger", out var deck));
        var drawBefore = deck.DrawCount;

        Assert.True(session.TryBeginDealConsider(new DealRequest
        {
            ContactName = "Badger",
            ConsiderCount = 2
        }, out var error), error);
        Assert.Equal(drawBefore - 2, deck.DrawCount);

        session.CancelDealConsider();
        Assert.Null(session.DealConsideredJobs);
        Assert.Equal(drawBefore, deck.DrawCount);
        Assert.False(session.Game.ActionTaken);
    }

    [Fact]
    public void Deal_chrome_prefers_local_Badger_at_Persephone()
    {
        var session = new HotseatGameSession();
        var view = EconomyChromeBuilder.BuildDeal(session.Game, null);
        Assert.Equal("Badger", view.SelectedContact);
        Assert.Contains(view.Contacts, c => c.Name == "Badger" && c.AtLocation && c.Reachable);
    }

    [Fact]
    public void ShoreLeave_legality_requires_planet()
    {
        var session = new HotseatGameSession();
        // Mosey into empty space if possible; otherwise assert planet gate via builder on a known empty sector.
        session.Game.CurrentPlayer.SectorId = "alliance-white-sun-r2-01";
        var (ok, reason) = EconomyChromeBuilder.ShoreLeaveLegality(session.Game);
        Assert.False(ok);
        Assert.Contains("Planet", reason);

        var buttons = ActionBarBuilder.Build(session.Game);
        Assert.Contains(buttons, b => b.Id == "shore-leave" && !b.Enabled);
    }

    [Fact]
    public void ContactDeck_RestoreToTop_preserves_order_for_restage()
    {
        var session = new HotseatGameSession();
        Assert.True(session.Game.ContactDecks!.TryGet("Badger", out var deck));
        var drawn = deck.DrawConsider(3);
        Assert.Equal(3, drawn.Count);
        deck.RestoreToTop(drawn);
        var again = deck.DrawConsider(3);
        Assert.Equal(drawn.Select(j => j.Id), again.Select(j => j.Id));
    }
}
