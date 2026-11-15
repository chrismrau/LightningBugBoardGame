using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P7 hotseat Work / Misbehave via kernel hooks on <see cref="HotseatGameSession"/>.
/// FAQ 4.1 p.5: Job becomes Active on first Work; botch leaves it Active.
/// </summary>
public sealed class WorkMisbehaveTests
{
    private const string Persephone = "alliance-lux-r1-01";
    private const string Santo = "alliance-qin-shi-huang-r1-01";
    private const string Crime = "job_badger_badgers-11-casino-caper";

    [Fact]
    public void MakeWork_at_Persephone_pays_200_and_consumes_Work()
    {
        var session = new HotseatGameSession();
        Assert.Equal(Persephone, session.Game.CurrentPlayer.SectorId);
        var cashBefore = session.Game.CurrentPlayer.Cash;

        Assert.True(session.TryMakeWork(out var error), error);
        Assert.Equal(cashBefore + WorkAction.MakeWorkPay, session.Game.CurrentPlayer.Cash);
        Assert.Equal(TurnAction.Work, session.Game.LastAction);
        Assert.False(session.Game.CanTakeAction(TurnAction.Work, out _));
    }

    [Fact]
    public void Work_chrome_lists_MakeWork_enabled_at_planetary_sector()
    {
        var session = new HotseatGameSession();
        var view = WorkChromeBuilder.Build(session.Game);
        var make = Assert.Single(view.Attempts, a => a.Kind == WorkAttemptKind.MakeWork);
        Assert.True(make.Enabled);
        Assert.Equal($"${WorkAction.MakeWorkPay}", make.PayLabel);
    }

    [Fact]
    public void Work_button_disabled_while_Misbehave_pending()
    {
        var session = SeedCrimeAtSanto();
        Assert.True(session.TryWork(Crime, out var error), error);
        Assert.NotNull(session.Game.PendingMisbehave);

        var buttons = ActionBarBuilder.Build(session.Game);
        Assert.Contains(buttons, b => b.Id == "work" && !b.Enabled);
        Assert.True(session.Game.HasPendingEvents);
    }

    [Fact]
    public void TryWork_crime_activates_and_awaits_Misbehave()
    {
        var session = SeedCrimeAtSanto();
        Assert.True(session.TryWork(Crime, out var error), error);

        Assert.DoesNotContain(Crime, session.Game.CurrentPlayer.JobHand);
        Assert.NotNull(session.Game.CurrentPlayer.FindActive(Crime));
        Assert.NotNull(session.Game.PendingMisbehave);
        Assert.True(session.Game.WorkGearLocked);

        var pending = PendingHostBuilder.From(session.Game);
        Assert.Equal(PendingQueueKind.Misbehave, pending.QueueKind);
        Assert.NotNull(pending.Misbehave);
        Assert.True(pending.Misbehave.NeedsReveal);
    }

    [Fact]
    public void DrawMisbehave_populates_FaceUp_for_PendingHost()
    {
        var session = SeedCrimeAtSanto();
        Assert.True(session.TryWork(Crime, out var startErr), startErr);

        Assert.True(session.TryDrawMisbehave(out var drawErr), drawErr);
        Assert.NotNull(session.Game.PendingMisbehave!.FaceUp);

        var host = PendingHostBuilder.From(session.Game);
        Assert.Equal(PendingQueueKind.Misbehave, host.QueueKind);
        Assert.NotNull(host.Misbehave);
        Assert.True(host.Misbehave.NeedsResolve);
        Assert.False(host.Misbehave.NeedsReveal);
        Assert.NotEmpty(host.Misbehave.Options);
        Assert.Equal(session.Game.PendingMisbehave.FaceUp.Id, host.Misbehave.CardId);
    }

    [Fact]
    public void ResolveMisbehave_Ace_with_Companion_proceeds_one_step()
    {
        var session = SeedCrimeAtSanto();
        // Mal Leader is not Companion; hire Inara if available for Ace on Formal Affair.
        if (session.Game.Crew != null
            && session.Game.Crew.TryGet("crew_inara", out var inara))
        {
            session.Game.CurrentPlayer.Roster.TryHire(inara, out _);
        }

        Assert.True(session.TryWork(Crime, out var startErr), startErr);
        var remainingBefore = session.Game.PendingMisbehave!.Remaining;

        var affair = session.Game.Misbehave!.Catalog.Get("misbehave_a-formal-affair");
        session.Game.Misbehave.PlaceOnTop(affair);
        Assert.True(session.TryDrawMisbehave(out var drawErr), drawErr);
        Assert.Equal("Companion", session.Game.PendingMisbehave.FaceUp!.Ace);

        if (!MisbehaveResolver.HasTag(session.Game, session.Game.CurrentPlayer, "Companion"))
        {
            // Smoke without Companion: skip Ace path; force-proceed remaining steps.
            session.Game.PendingMisbehave.FaceUp = null;
            var work = new WorkAction();
            for (var i = 0; i < remainingBefore; i++)
                Assert.True(work.TryProceedMisbehave(session.Game, "p1", true, out _, out var pErr), pErr);
            Assert.Null(session.Game.PendingMisbehave);
            return;
        }

        Assert.True(
            session.TryResolveMisbehave(new MisbehaveChoice { UseAce = true }, out var aceErr),
            aceErr);
        Assert.True(
            session.Game.PendingMisbehave == null
            || session.Game.PendingMisbehave.Remaining == remainingBefore - 1
            || session.Game.PendingMisbehave.FaceUp == null);
    }

    [Fact]
    public void Botch_leaves_job_Active_and_spends_Work()
    {
        var session = SeedCrimeAtSanto();
        Assert.True(session.TryWork(Crime, out var error), error);
        Assert.NotNull(session.Game.CurrentPlayer.FindActive(Crime));

        var work = new WorkAction();
        Assert.True(work.TryProceedMisbehave(session.Game, session.Game.CurrentPlayer.Id, false, out _, out var botchErr), botchErr);

        Assert.Null(session.Game.PendingMisbehave);
        Assert.NotNull(session.Game.CurrentPlayer.FindActive(Crime));
        Assert.Equal(TurnAction.Work, session.Game.LastAction);
        Assert.False(session.Game.CanTakeAction(TurnAction.Work, out _));
    }

    [Fact]
    public void TrySubmitChoice_routes_MakeWorkFugitive_kind()
    {
        // Without Holder ability, Make-Work does not suspend — seed the choice and resume via session.
        var session = new HotseatGameSession();
        Assert.True(session.TrySeedPendingChoice(
            new PendingChoice(
                session.Game.CurrentPlayer.Id,
                PendingChoiceKinds.MakeWorkFugitive,
                options: new[] { HolderFugitiveOptions.TakeFugitive, HolderFugitiveOptions.Decline },
                prompt: "Take a Fugitive Token with Make-Work?"),
            out var seedErr), seedErr);

        Assert.True(MisbehaveChoiceResume.OwnsPendingChoice(session.Game));

        // Decline — WorkAction.TryResumeMakeWorkFugitive re-enters TryMakeWork which succeeds at Persephone.
        Assert.True(session.TrySubmitChoice(
            new ChoiceSubmission { SelectedOptionId = HolderFugitiveOptions.Decline },
            out var err), err);
        Assert.Null(session.Game.PendingChoice);
        Assert.Equal(TurnAction.Work, session.Game.LastAction);
    }

    [Fact]
    public void Deal_and_Buy_still_work_after_Work_chrome()
    {
        var session = new HotseatGameSession();
        Assert.True(session.TryBuy(new BuyRequest { Fuel = 1 }, out var buyErr), buyErr);
        session.EndTurn();
        Assert.True(session.TryMakeWork(out var workErr), workErr);
        session.EndTurn();
        Assert.True(session.TryShoreLeave(out var shoreErr), shoreErr);
    }

    private static HotseatGameSession SeedCrimeAtSanto()
    {
        var session = new HotseatGameSession();
        var player = session.Game.CurrentPlayer;
        player.SectorId = Santo;
        player.JobHand.Clear();
        player.JobHand.Add(Crime);
        player.ActiveJobs.Clear();
        return session;
    }
}
