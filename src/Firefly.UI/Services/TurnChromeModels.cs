using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>One button on the bottom action bar (legal-first via kernel <see cref="GameState.CanTakeAction"/>).</summary>
public sealed record ActionButtonView(
    string Id,
    string Label,
    bool Enabled,
    string? DisabledReason,
    TurnAction? TurnAction,
    ActionChromeKind Kind);

public enum ActionChromeKind
{
    TurnAction,
    ShoreLeave,
    BetterOffer,
    EndTurn
}

/// <summary>What the pending host should show right now (banner + optional choice payload).</summary>
public sealed record PendingHostView(
    bool HasPending,
    string PriorityLabel,
    string Summary,
    PendingQueueKind QueueKind,
    PendingChoiceView? Choice,
    NavRevealView? Nav = null,
    MisbehaveRevealView? Misbehave = null);

public enum PendingQueueKind
{
    None,
    Alerts,
    Encounter,
    Nav,
    Misbehave,
    GoalWork,
    Choice
}

/// <summary>Immutable PendingChoice projection for Blazor (no live GameState refs).</summary>
public sealed record PendingChoiceView(
    string PlayerId,
    string Kind,
    string? ContextId,
    string? Prompt,
    IReadOnlyList<string>? Options,
    ChoiceWidgetMode WidgetMode);

/// <summary>
/// Pending banner priority per playspace plan: Alerts → Encounter → Nav → Misbehave/Goal → Choice.
/// When a <see cref="PendingChoice"/> is set mid-resolve it still wins the interactive host
/// (player must answer), but the sticky label follows queue priority when no choice is open.
/// </summary>
public static class PendingHostBuilder
{
    /// <param name="navFaceUp">
    /// Session-owned face-up Nav (not on GameState). Counts as pending until resolved.
    /// </param>
    public static PendingHostView From(GameState game, DrawnNav? navFaceUp = null)
    {
        var navView = FlyLegalBuilder.FromNav(game, navFaceUp);
        var misbehaveView = WorkChromeBuilder.FromPending(game);
        var hasNavPending = game.PendingNavDraws.Count > 0 || navFaceUp != null;

        if (!game.HasPendingEvents && navFaceUp == null)
        {
            return new PendingHostView(
                false,
                "",
                "",
                PendingQueueKind.None,
                null,
                navView,
                misbehaveView);
        }

        PendingChoiceView? choice = null;
        if (game.PendingChoice != null)
        {
            var pc = game.PendingChoice;
            choice = new PendingChoiceView(
                pc.PlayerId,
                pc.Kind,
                pc.ContextId,
                pc.Prompt,
                pc.Options,
                ChoiceWidgetRegistry.ModeFor(pc.Kind, pc.Options));
        }

        // Interactive choice always surfaces in the host when present.
        if (choice != null)
        {
            return new PendingHostView(
                true,
                "Choice",
                choice.Prompt ?? $"PendingChoice: {choice.Kind}",
                PendingQueueKind.Choice,
                choice,
                navView,
                misbehaveView);
        }

        if (game.PendingAlertSectors.Count > 0)
        {
            return new PendingHostView(
                true,
                "Alert Tokens",
                $"Alert Tokens ({game.PendingAlertSectors.Count}) — resolve before other actions",
                PendingQueueKind.Alerts,
                null,
                navView,
                misbehaveView);
        }

        if (game.PendingEncounter.HasValue)
        {
            return new PendingHostView(
                true,
                "Encounter",
                $"Encounter: {game.PendingEncounter}",
                PendingQueueKind.Encounter,
                null,
                navView,
                misbehaveView);
        }

        if (hasNavPending)
        {
            var summary = navFaceUp != null
                ? $"Nav: {navFaceUp.Card.Name} — choose an option"
                : $"Nav draws ({game.PendingNavDraws.Count}) — reveal to resolve";
            return new PendingHostView(
                true,
                "Nav",
                summary,
                PendingQueueKind.Nav,
                null,
                navView,
                misbehaveView);
        }

        if (game.PendingMisbehave != null)
        {
            var summary = misbehaveView.HasFaceUp
                ? $"Misbehave: {misbehaveView.CardName} — {misbehaveView.Remaining} remaining"
                : $"Misbehave ({misbehaveView.Remaining}) — reveal to resolve";
            return new PendingHostView(
                true,
                "Misbehave",
                summary,
                PendingQueueKind.Misbehave,
                null,
                navView,
                misbehaveView);
        }

        if (game.PendingGoalWork != null)
        {
            return new PendingHostView(
                true,
                "Goal Work",
                $"Goal {game.PendingGoalWork.GoalNumber} — {game.PendingGoalWork.Phase}",
                PendingQueueKind.GoalWork,
                null,
                navView,
                misbehaveView);
        }

        return new PendingHostView(true, "Pending", "Pending events", PendingQueueKind.None, null, navView, misbehaveView);
    }
}

/// <summary>Builds action-bar buttons from kernel legality only — no client-side rules.</summary>
public static class ActionBarBuilder
{
    private static readonly TurnAction[] TurnActions =
    {
        TurnAction.Fly,
        TurnAction.Deal,
        TurnAction.Work,
        TurnAction.Buy,
        TurnAction.Crew
    };

    /// <param name="navFaceUpBlocking">
    /// True when a Nav card is face-up on the session resolver (GameState.HasPendingEvents
    /// does not see FaceUp). Blocks turn actions the same way queued Nav draws do.
    /// </param>
    public static IReadOnlyList<ActionButtonView> Build(GameState game, bool navFaceUpBlocking = false)
    {
        var list = new List<ActionButtonView>(TurnActions.Length + 3);
        var faceUpBlock = navFaceUpBlocking
            ? "Resolve the face-up Nav card before taking another action."
            : null;

        foreach (var action in TurnActions)
        {
            var ok = game.CanTakeAction(action, out var error);
            if (ok && faceUpBlock != null)
            {
                ok = false;
                error = faceUpBlock;
            }
            list.Add(new ActionButtonView(
                action.ToString().ToLowerInvariant(),
                action.ToString(),
                ok,
                ok ? null : error,
                action,
                ActionChromeKind.TurnAction));
        }

        // Shore Leave consumes Buy (GF9 / AGENTS.md). Legality mirrors ShoreLeaveAction gates.
        {
            var (ok, reason) = EconomyChromeBuilder.ShoreLeaveLegality(game, navFaceUpBlocking);
            list.Add(new ActionButtonView(
                "shore-leave",
                "Shore Leave",
                ok,
                ok ? null : reason,
                TurnAction.Buy,
                ActionChromeKind.ShoreLeave));
        }

        // A Better Offer is not an action (FAQ 4.1 p.1). Enabled only when a legal poach target exists.
        {
            var (ok, reason) = BetterOfferLegality(game);
            list.Add(new ActionButtonView(
                "better-offer",
                "A Better Offer",
                ok,
                ok ? null : reason,
                null,
                ActionChromeKind.BetterOffer));
        }

        // EndTurn always allowed with 0/1/2 actions (GF9 BBG p.6 "may take 2 Actions").
        // Kernel EndTurn clears pending queues; chrome does not invent a second gate.
        list.Add(new ActionButtonView(
            "end-turn",
            "End Turn",
            Enabled: true,
            DisabledReason: null,
            TurnAction: null,
            ActionChromeKind.EndTurn));

        return list;
    }

    /// <summary>
    /// Table-visible scan for same-sector Disgruntled non-Leader crew the current seat can afford.
    /// Does not invent rules — mirrors <c>PoachAction.TryPoach</c> preconditions.
    /// </summary>
    public static (bool Ok, string Reason) BetterOfferLegality(GameState game)
    {
        if (game.HasPendingEvents)
            return (false, "Resolve pending events first.");

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
                    return (false, $"Roster is full ({player.Roster.MaxCrew}).");
                return (true, "");
            }
        }

        return (false, "No Disgruntled rival crew in this sector.");
    }
}
