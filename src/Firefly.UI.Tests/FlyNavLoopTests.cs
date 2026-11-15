using Firefly.Core.Map;
using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P3 hotseat Fly lean loop: Mosey / Full Burn via kernel FlyAction,
/// Nav reveal/resolve via session NavResolver, EndTurn still clears.
/// </summary>
public sealed class FlyNavLoopTests
{
    private const string Persephone = "alliance-lux-r1-01";
    private const string Pelorum = "alliance-lux-r1-02";

    [Fact]
    public void Mosey_moves_ship_and_consumes_Fly_action()
    {
        var session = new HotseatGameSession();
        Assert.Equal(Persephone, session.Game.CurrentPlayer.SectorId);
        Assert.True(session.Game.CanTakeAction(TurnAction.Fly, out _));

        Assert.True(session.TryMosey(Pelorum, out var error), error);
        Assert.Equal(Pelorum, session.Game.CurrentPlayer.SectorId);
        Assert.Equal(TurnAction.Fly, session.Game.LastAction);
        Assert.Empty(session.Game.PendingNavDraws);
        Assert.Null(session.NavFaceUp);

        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.Equal(Pelorum, snap.SectorId);
        Assert.Contains(snap.ShipTokens, t => t.IsCurrent && t.SectorId == Pelorum);
        Assert.Contains(snap.ActionButtons, b => b.Id == "fly" && !b.Enabled);
    }

    [Fact]
    public void FullBurn_spends_fuel_queues_Nav_and_surfaces_in_PendingHost()
    {
        var session = new HotseatGameSession();
        var fuelBefore = session.Game.CurrentPlayer.Fuel;
        Assert.True(fuelBefore >= 1);

        Assert.True(session.TryFullBurnTo(Pelorum, out var error), error);
        Assert.Equal(Pelorum, session.Game.CurrentPlayer.SectorId);
        Assert.Equal(fuelBefore - 1, session.Game.CurrentPlayer.Fuel);
        Assert.NotEmpty(session.Game.PendingNavDraws);

        var pending = PendingHostBuilder.From(session.Game, session.NavFaceUp);
        Assert.True(pending.HasPending);
        Assert.Equal(PendingQueueKind.Nav, pending.QueueKind);
        Assert.NotNull(pending.Nav);
        Assert.True(pending.Nav!.NeedsReveal);

        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.True(snap.HasPendingEvents);
        Assert.Equal(PendingQueueKind.Nav, snap.Pending.QueueKind);
        Assert.Contains(snap.ActionButtons, b => b.Id == "fly" && !b.Enabled);
    }

    [Fact]
    public void Nav_reveal_then_resolve_clears_queue_happy_path()
    {
        var session = new HotseatGameSession();
        Assert.True(session.TryFullBurnTo(Pelorum, out var burnError), burnError);
        var queued = session.Game.PendingNavDraws.Count;
        Assert.True(queued >= 1);

        Assert.True(session.TryDrawNav(out var drawError), drawError);
        Assert.NotNull(session.NavFaceUp);
        Assert.Equal(queued - 1, session.Game.PendingNavDraws.Count);

        var faceUpPending = PendingHostBuilder.From(session.Game, session.NavFaceUp);
        Assert.Equal(PendingQueueKind.Nav, faceUpPending.QueueKind);
        Assert.True(faceUpPending.Nav!.NeedsResolve);
        Assert.NotEmpty(faceUpPending.Nav.Options);

        // Resolve option 0; may suspend into PendingChoice (exotic) or clear FaceUp.
        Assert.True(session.TryResolveNavOption(0, out var resolveError), resolveError);

        if (session.Game.PendingChoice != null)
        {
            // Happy-path fallback: choice host takes over; FaceUp may still be set mid-resume.
            var choicePending = PendingHostBuilder.From(session.Game, session.NavFaceUp);
            Assert.Equal(PendingQueueKind.Choice, choicePending.QueueKind);
            Assert.NotNull(choicePending.Choice);
        }
        else
        {
            Assert.Null(session.NavFaceUp);
        }
    }

    [Fact]
    public void FlyLegalBuilder_mosey_targets_include_adjacent()
    {
        var session = new HotseatGameSession();
        var targets = FlyLegalBuilder.MoseyTargets(session.Game, session.Movement);
        Assert.Contains(Pelorum, targets);
    }

    [Fact]
    public void FlyLegalBuilder_full_burn_blocked_without_fuel()
    {
        var session = new HotseatGameSession();
        session.Game.CurrentPlayer.Fuel = 0;
        // Default Serenity Mark I requires fuel to initiate Full Burn.
        Assert.False(FlyLegalBuilder.CanInitiateFullBurn(session.Game, out var reason));
        Assert.Contains("fuel", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActionBar_blocks_while_Nav_face_up()
    {
        var session = new HotseatGameSession();
        Assert.True(session.TryFullBurnTo(Pelorum, out _));
        Assert.True(session.TryDrawNav(out _));
        Assert.NotNull(session.NavFaceUp);
        // Last draw may empty PendingNavDraws — FaceUp must still block.
        var buttons = ActionBarBuilder.Build(session.Game, navFaceUpBlocking: true);
        Assert.All(
            buttons.Where(b => b.Kind is ActionChromeKind.TurnAction or ActionChromeKind.ShoreLeave),
            b => Assert.False(b.Enabled));
        Assert.Contains(buttons, b => b.Kind == ActionChromeKind.EndTurn && b.Enabled);
    }

    [Fact]
    public void EndTurn_discards_face_up_Nav_and_clears_pending()
    {
        var session = new HotseatGameSession();
        Assert.True(session.TryFullBurnTo(Pelorum, out _));
        Assert.True(session.TryDrawNav(out _));
        Assert.NotNull(session.NavFaceUp);

        session.EndTurn();
        Assert.Null(session.NavFaceUp);
        Assert.False(session.Game.HasPendingEvents);
        Assert.Equal(0, session.Game.ActionsUsedThisTurn);
    }

    [Fact]
    public void Seeded_PendingChoice_still_submits_when_no_Nav_face_up()
    {
        var session = new HotseatGameSession();
        Assert.True(session.TrySeedPendingChoice(
            new PendingChoice(
                session.Game.CurrentPlayer.Id,
                PendingChoiceKinds.SkillReroll,
                options: new[] { SkillRerollOptions.Keep, SkillRerollOptions.Reroll },
                prompt: "Keep or re-roll?"),
            out var seedError), seedError);

        Assert.True(session.TrySubmitChoice(
            new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Keep },
            out var submitError), submitError);
        Assert.Null(session.Game.PendingChoice);
    }
}
