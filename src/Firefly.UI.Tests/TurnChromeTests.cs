using Firefly.Core.Map;
using Firefly.Core.State;
using Firefly.UI.Services;
using Xunit;

namespace Firefly.UI.Tests;

/// <summary>
/// P2 hotseat chrome: action-bar legality via CanTakeAction, EndTurn 0–2,
/// PendingChoice seed → TrySubmitChoice clears via kernel.
/// </summary>
public sealed class TurnChromeTests
{
    [Fact]
    public void ActionBar_enables_turn_actions_when_legal()
    {
        var session = new HotseatGameSession();
        var game = session.Game;

        Assert.False(game.HasPendingEvents);
        Assert.True(game.CanTakeAction(TurnAction.Fly, out _));

        var buttons = ActionBarBuilder.Build(game);
        Assert.Contains(buttons, b => b.Id == "fly" && b.Enabled);
        Assert.Contains(buttons, b => b.Id == "deal" && b.Enabled);
        Assert.Contains(buttons, b => b.Id == "work" && b.Enabled);
        Assert.Contains(buttons, b => b.Id == "buy" && b.Enabled);
        Assert.Contains(buttons, b => b.Id == "crew" && b.Enabled);
        Assert.Contains(buttons, b => b.Id == "shore-leave" && b.Enabled);
        Assert.Contains(buttons, b => b.Kind == ActionChromeKind.EndTurn && b.Enabled);
        Assert.Contains(buttons, b => b.Kind == ActionChromeKind.BetterOffer && !b.Enabled);
    }

    [Fact]
    public void ActionBar_disables_actions_while_pending_choice()
    {
        var session = new HotseatGameSession();
        Assert.True(session.TrySeedPendingChoice(
            new PendingChoice(
                session.Game.CurrentPlayer.Id,
                PendingChoiceKinds.NavPayOrDecline,
                options: new[] { SkillRerollOptions.Keep, "decline" },
                prompt: "Pay or Full Stop?"),
            out var seedError), seedError);

        var buttons = ActionBarBuilder.Build(session.Game);
        Assert.All(
            buttons.Where(b => b.Kind is ActionChromeKind.TurnAction or ActionChromeKind.ShoreLeave),
            b => Assert.False(b.Enabled));
        Assert.Contains(buttons, b => b.Kind == ActionChromeKind.EndTurn && b.Enabled);
    }

    [Fact]
    public void ActionBar_disables_repeat_action_type_after_consume()
    {
        var session = new HotseatGameSession();
        Assert.True(session.Game.TryConsumeAction(TurnAction.Buy, out var err), err);

        var buttons = ActionBarBuilder.Build(session.Game);
        Assert.Contains(buttons, b => b.Id == "buy" && !b.Enabled);
        Assert.Contains(buttons, b => b.Id == "shore-leave" && !b.Enabled);
        Assert.Contains(buttons, b => b.Id == "fly" && b.Enabled);
    }

    [Fact]
    public void EndTurn_allowed_with_zero_actions_via_session()
    {
        var session = new HotseatGameSession();
        Assert.Equal(0, session.Game.ActionsUsedThisTurn);
        session.EndTurn();
        Assert.Equal(0, session.Game.ActionsUsedThisTurn);
        Assert.Equal(TurnAction.None, session.Game.LastAction);
    }

    [Fact]
    public void Seeded_PendingChoice_submit_clears_via_kernel()
    {
        var session = new HotseatGameSession();
        var playerId = session.Game.CurrentPlayer.Id;
        Assert.True(session.TrySeedPendingChoice(
            new PendingChoice(
                playerId,
                PendingChoiceKinds.SkillReroll,
                options: new[] { SkillRerollOptions.Keep, SkillRerollOptions.Reroll },
                prompt: "Keep or re-roll?"),
            out var seedError), seedError);

        Assert.True(session.Game.HasPendingEvents);
        Assert.Equal(PendingChoiceKinds.SkillReroll, session.Game.PendingChoice!.Kind);

        var snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.True(snap.Pending.HasPending);
        Assert.Equal(PendingQueueKind.Choice, snap.Pending.QueueKind);
        Assert.NotNull(snap.Pending.Choice);
        Assert.Equal(ChoiceWidgetMode.AcceptDecline, snap.Pending.Choice!.WidgetMode);

        Assert.True(session.TrySubmitChoice(
            new ChoiceSubmission { SelectedOptionId = SkillRerollOptions.Keep },
            out var submitError), submitError);

        Assert.Null(session.Game.PendingChoice);
        Assert.False(session.Game.HasPendingEvents);

        snap = PlayViewSnapshot.From(session, session.Game.Map);
        Assert.False(snap.Pending.HasPending);
        Assert.All(
            snap.ActionButtons.Where(b => b.Kind == ActionChromeKind.TurnAction),
            b => Assert.True(b.Enabled));
    }

    [Fact]
    public void PendingHost_priority_alerts_before_nav_when_no_choice()
    {
        var session = new HotseatGameSession();
        var game = session.Game;
        game.PendingNavDraws.Add(new PendingNavDraw(game.CurrentPlayer.SectorId, NavRegion.Alliance));
        game.PendingAlertSectors.Add(game.CurrentPlayer.SectorId);

        var pending = PendingHostBuilder.From(game);
        Assert.Equal(PendingQueueKind.Alerts, pending.QueueKind);
        Assert.Contains("Alert", pending.PriorityLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChoiceWidgetRegistry_covers_known_kernel_kinds()
    {
        Assert.Contains(PendingChoiceKinds.NavPayOrDecline, ChoiceWidgetRegistry.RegisteredKinds);
        Assert.Contains(PendingChoiceKinds.BribeAmount, ChoiceWidgetRegistry.RegisteredKinds);
        Assert.Contains(PendingChoiceKinds.GoodsMix, ChoiceWidgetRegistry.RegisteredKinds);
        Assert.Equal(ChoiceWidgetMode.Options, ChoiceWidgetRegistry.ModeFor("unknown-kind", new[] { "a" }));
        Assert.Equal(ChoiceWidgetMode.Generic, ChoiceWidgetRegistry.ModeFor("unknown-kind", null));
    }
}
