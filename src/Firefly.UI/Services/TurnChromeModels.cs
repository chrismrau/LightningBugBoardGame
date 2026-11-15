using Firefly.Core.Actions;
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
public sealed record EncounterView(
    string TokenKind,
    string? SectorId,
    string? SubjectPlayerId,
    string Prompt,
    IReadOnlyList<EncounterOptionView> Options);

public sealed record EncounterOptionView(string Id, string Label, string? Detail = null);

public sealed record AlertTokensPendingView(
    int SectorCount,
    string? FirstSectorId,
    string Prompt);

public sealed record PendingHostView(
    bool HasPending,
    string PriorityLabel,
    string Summary,
    PendingQueueKind QueueKind,
    PendingChoiceView? Choice,
    NavRevealView? Nav = null,
    MisbehaveRevealView? Misbehave = null,
    EncounterView? Encounter = null,
    AlertTokensPendingView? AlertTokens = null);

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

        var encounterView = BuildEncounter(game);
        var alertTokensView = game.PendingAlertSectors.Count > 0
            ? new AlertTokensPendingView(
                game.PendingAlertSectors.Count,
                game.PendingAlertSectors[0],
                $"Resolve Alert Tokens in {game.PendingAlertSectors.Count} sector(s) before Nav (Blue Sun).")
            : null;

        if (!game.HasPendingEvents && navFaceUp == null)
        {
            return new PendingHostView(
                false,
                "",
                "",
                PendingQueueKind.None,
                null,
                navView,
                misbehaveView,
                encounterView,
                alertTokensView);
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
                misbehaveView,
                encounterView,
                alertTokensView);
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
                misbehaveView,
                encounterView,
                alertTokensView);
        }

        if (game.PendingEncounter.HasValue)
        {
            var encSummary = encounterView?.Prompt
                ?? $"Encounter: {game.PendingEncounter}";
            return new PendingHostView(
                true,
                "Encounter",
                encSummary,
                PendingQueueKind.Encounter,
                null,
                navView,
                misbehaveView,
                encounterView,
                alertTokensView);
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
                misbehaveView,
                encounterView,
                alertTokensView);
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
                misbehaveView,
                encounterView,
                alertTokensView);
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
                misbehaveView,
                encounterView,
                alertTokensView);
        }

        return new PendingHostView(
            true, "Pending", "Pending events", PendingQueueKind.None, null,
            navView, misbehaveView, encounterView, alertTokensView);
    }

    private static EncounterView? BuildEncounter(GameState game)
    {
        if (!game.PendingEncounter.HasValue)
            return null;

        var kind = game.PendingEncounter.Value;
        var sector = game.PendingEncounterSectorId;
        var subject = game.PendingEncounterPlayerId;
        var options = new List<EncounterOptionView>();

        switch (kind)
        {
            case Firefly.Core.Movement.TokenKind.OperativeCorvette:
            {
                var player = game.CurrentPlayer;
                var wanted = player.Roster.WantedMembers();
                if (wanted.Count == 0)
                {
                    options.Add(new EncounterOptionView(
                        "resolve",
                        "Resolve Corvette Contact",
                        "Discard Fugitive Tokens not in Stash; Full Stop if Flying."));
                }
                else
                {
                    foreach (var member in wanted)
                    {
                        options.Add(new EncounterOptionView(
                            member.Id,
                            $"Remove {member.Name}",
                            "Wanted crew removed from play (Corvette Contact)."));
                    }
                }
                return new EncounterView(
                    kind.ToString(),
                    sector,
                    subject,
                    "Operative's Corvette Contact — Outlaw ship",
                    options);
            }
            case Firefly.Core.Movement.TokenKind.AllianceCruiser:
                options.Add(new EncounterOptionView(
                    "board",
                    "Resolve Cruiser boarding",
                    "Pay fine, clear warrants, seize Contraband / Fugitive Tokens."));
                if (ActiveAlertRules.CanUseIncarcerationOrder(game, AllianceCruiserContact.Subject(game)))
                {
                    foreach (var member in AllianceCruiserContact.Subject(game).Roster.WantedMembers())
                    {
                        options.Add(new EncounterOptionView(
                            "incarcerate:" + member.Id,
                            $"Incarceration Order · {member.Name}",
                            "Waive one warrant by removing Wanted crew (Active Alert)."));
                    }
                }
                return new EncounterView(
                    kind.ToString(),
                    sector,
                    subject,
                    "Alliance Cruiser Contact",
                    options);
            case Firefly.Core.Movement.TokenKind.ReaverCutter:
                // ReaverContact.TryResolve requires evadeToSectorId — exotic gap (document in P9 status).
                return new EncounterView(
                    kind.ToString(),
                    sector,
                    subject,
                    "Reaver Cutter Contact — evade sector picker not wired (see P9 gaps)",
                    Array.Empty<EncounterOptionView>());
            default:
                return new EncounterView(
                    kind.ToString(),
                    sector,
                    subject,
                    $"Encounter: {kind}",
                    Array.Empty<EncounterOptionView>());
        }
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
