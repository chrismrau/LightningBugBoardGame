using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>UI Fly mode — presentation only; legality stays in the kernel.</summary>
public enum FlyUiMode
{
    None,
    /// <summary>Action bar Fly pressed — choose Mosey vs Full Burn.</summary>
    PickKind,
    Mosey,
    FullBurn
}

/// <summary>Chrome strip while the seat is picking a Fly move.</summary>
public sealed record FlyChromeView(
    FlyUiMode Mode,
    bool CanFullBurn,
    string? FullBurnBlockReason,
    int DriveRange,
    int MoseyRange,
    int Fuel,
    bool FullBurnRequiresFuel,
    IReadOnlyList<string> LegalSectorIds,
    IReadOnlyList<string> PathSectorIds,
    string? PathDestinationId,
    bool PathReady);

/// <summary>One printable option on a face-up Nav card.</summary>
public sealed record NavOptionView(
    int Index,
    string Label,
    string Details,
    string Outcome);

/// <summary>Queued / face-up Nav for the PendingHost reveal/resolve happy path.</summary>
public sealed record NavRevealView(
    int QueuedCount,
    bool HasFaceUp,
    string? CardId,
    string? CardName,
    string? CardType,
    string? Region,
    string? SectorId,
    IReadOnlyList<NavOptionView> Options)
{
    public bool NeedsReveal => QueuedCount > 0 && !HasFaceUp;
    public bool NeedsResolve => HasFaceUp;
}

/// <summary>Builds Fly legal-sector highlights from kernel movement helpers (no fuel math in the UI).</summary>
public static class FlyLegalBuilder
{
    public static IReadOnlyList<string> MoseyTargets(GameState game, MovementEngine movement)
    {
        var player = game.CurrentPlayer;
        var range = player.GetEffectiveMoseyRange(game);
        if (range <= 1)
            return movement.MoseyDestinations(player.SectorId).ToList();

        var reachable = movement.Pathfinder.Reachable(player.SectorId, range);
        return reachable
            .Where(id => !string.Equals(id, player.SectorId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<string> FullBurnTargets(GameState game, MovementEngine movement)
    {
        var player = game.CurrentPlayer;
        var range = player.GetEffectiveDriveRange(game);
        return movement.FullBurnDestinations(player.SectorId, range).ToList();
    }

    /// <summary>
    /// Mirror <see cref="FlyAction.TryFullBurn"/> initiate-fuel gate (no Catalyzer in P3 chrome).
    /// </summary>
    public static bool CanInitiateFullBurn(GameState game, out string? reason)
    {
        reason = null;
        if (!game.CanTakeAction(TurnAction.Fly, out reason))
            return false;

        var player = game.CurrentPlayer;
        var fuelNeeded = player.FullBurnRequiresFuel ? 1 : 0;
        if (player.Fuel < fuelNeeded)
        {
            reason = "Not enough fuel for Full Burn.";
            return false;
        }

        return true;
    }

    public static FlyChromeView Build(
        GameState game,
        MovementEngine movement,
        FlyUiMode mode,
        IReadOnlyList<string>? pathPreview)
    {
        var player = game.CurrentPlayer;
        var canBurn = CanInitiateFullBurn(game, out var burnReason);
        var path = pathPreview ?? Array.Empty<string>();
        IReadOnlyList<string> legal = mode switch
        {
            FlyUiMode.Mosey => MoseyTargets(game, movement),
            FlyUiMode.FullBurn => FullBurnTargets(game, movement),
            _ => Array.Empty<string>()
        };

        return new FlyChromeView(
            mode,
            canBurn,
            canBurn ? null : burnReason,
            player.GetEffectiveDriveRange(game),
            player.GetEffectiveMoseyRange(game),
            player.Fuel,
            player.FullBurnRequiresFuel,
            legal,
            path,
            path.Count >= 2 ? path[^1] : null,
            path.Count >= 2);
    }

    public static NavRevealView FromNav(GameState game, DrawnNav? faceUp)
    {
        if (faceUp == null)
        {
            return new NavRevealView(
                game.PendingNavDraws.Count,
                false,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<NavOptionView>());
        }

        var options = new List<NavOptionView>(faceUp.Card.Options.Count);
        for (var i = 0; i < faceUp.Card.Options.Count; i++)
        {
            var opt = faceUp.Card.Options[i];
            options.Add(new NavOptionView(
                i,
                string.IsNullOrWhiteSpace(opt.Name) ? $"Option {i + 1}" : opt.Name!,
                opt.Details,
                opt.Outcome.ToString()));
        }

        return new NavRevealView(
            game.PendingNavDraws.Count,
            true,
            faceUp.Card.Id,
            faceUp.Card.Name,
            faceUp.Card.Type,
            faceUp.Region.ToString(),
            faceUp.SectorId,
            options);
    }
}

/// <summary>
/// Routes PendingChoice submits that belong to a face-up Nav resolve back through
/// <see cref="NavResolver"/> resume hooks. Plain GameState submits stay for non-Nav waits.
/// </summary>
public static class NavChoiceResume
{
    public static bool TryResume(
        GameState game,
        NavResolver nav,
        ChoiceSubmission submission,
        IRng rng,
        out string? error)
    {
        error = null;
        var kind = game.PendingChoice?.Kind;
        if (kind == null)
        {
            error = "No choice is pending.";
            return false;
        }

        // Emissions Recycler is Fly-scoped (after Nav discard); not NavResolver state.
        if (string.Equals(kind, PendingChoiceKinds.EmissionsFuel, StringComparison.Ordinal))
            return FlyAction.TryResumeEmissionsFuel(game, submission, out _, out error);

        if (nav.FaceUp == null)
        {
            // Non-Nav PendingChoice (P2 smoke / other actions) — clear via kernel only.
            return game.TrySubmitChoice(
                game.PendingChoice!.PlayerId,
                submission,
                out _,
                out error);
        }

        if (string.Equals(kind, PendingChoiceKinds.NavPayOrDecline, StringComparison.Ordinal))
            return nav.TryResumeNavPayOrDecline(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.KillVictim, StringComparison.Ordinal))
        {
            if (nav.TryResumeKillVictims(game, submission, out _, out error, rng))
                return true;
            // Mid-Nav Reaver Contact kill suspend uses a different resume entry.
            return nav.TryResumeMidNavReaverKillVictims(game, submission, rng, out _, out error);
        }

        if (string.Equals(kind, PendingChoiceKinds.BribeAmount, StringComparison.Ordinal))
            return nav.TryResumeBribeAmount(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.SkillReroll, StringComparison.Ordinal))
            return nav.TryResumeSkillReroll(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.DiscardToReroll, StringComparison.Ordinal))
            return nav.TryResumeDiscardToReroll(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MedFoamDiscard, StringComparison.Ordinal))
        {
            if (nav.TryResumeMedFoam(game, submission, out _, out error, rng))
                return true;
            return nav.TryResumeMidNavReaverMedFoam(game, submission, rng, out _, out error);
        }

        if (string.Equals(kind, PendingChoiceKinds.GoodsMix, StringComparison.Ordinal))
            return nav.TryResumeGoodsMix(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.NavFakeIdSalvage, StringComparison.Ordinal))
            return nav.TryResumeFakeIdSalvage(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.SectorDestination, StringComparison.Ordinal))
            return nav.TryResumeSectorDestination(game, submission, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.DiscardShipUpgrade, StringComparison.Ordinal))
            return nav.TryResumeDiscardShipUpgrade(game, submission, out _, out error, rng);

        // Unknown Nav-tied kind: document as gap — clear choice only so the host unblocks;
        // FaceUp remains for a later resolve attempt.
        error =
            $"PendingChoice kind '{kind}' has no Nav resume wired in P3; resolve manually or End Turn.";
        return false;
    }
}
