using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>
/// Server-owned play session. Hotseat v1 is a single in-process game;
/// SignalR lobby rooms can wrap the same shape later without rewriting rules.
/// </summary>
public interface IGameSession
{
    event Action? Changed;

    GameState Game { get; }
    string ScenarioCardId { get; }
    bool UseBlueSun { get; }
    bool UseBountyDeck { get; }
    bool UseOperativesCorvette { get; }
    string? SelectedSectorId { get; }

    /// <summary>Stateful Nav resolver (FaceUp lives here — not on GameState).</summary>
    DrawnNav? NavFaceUp { get; }

    /// <summary>Movement helpers for Fly legal highlights (kernel Pathfinder / range).</summary>
    MovementEngine Movement { get; }

    /// <summary>Jobs drawn while staging a Deal consider (P6); null when idle.</summary>
    IReadOnlyList<JobCard>? DealConsideredJobs { get; }

    void StartNew(
        string scenarioCardId,
        bool useBlueSun = false,
        bool useBountyDeck = false,
        bool useOperativesCorvette = false);

    void SelectSector(string? sectorId);

    /// <summary>
    /// End the current seat's turn with 0, 1, or 2 actions used.
    /// GF9 BBG p.6: "On your turn, you may take 2 Actions…"
    /// Face-up Nav (if any) is discarded into the Nav discard pile before clearing.
    /// </summary>
    void EndTurn();

    /// <summary>
    /// Submit the current <see cref="GameState.PendingChoice"/>. When a Nav card is face-up,
    /// routes through <see cref="NavChoiceResume"/> / kernel Nav resume hooks.
    /// DealSellParts / HavenFuelAmount resume via session-owned action instances.
    /// </summary>
    bool TrySubmitChoice(ChoiceSubmission submission, out string? error);

    /// <summary>
    /// Smoke / test helper: seed a single PendingChoice on the live game.
    /// Does not invent kinds — caller supplies a kernel <see cref="PendingChoice"/>.
    /// </summary>
    bool TrySeedPendingChoice(PendingChoice choice, out string? error);

    /// <summary>Kernel <c>FlyAction.TryMosey</c> for the current seat.</summary>
    bool TryMosey(string toSectorId, out string? error);

    /// <summary>
    /// Kernel <c>FlyAction.TryFullBurn</c> with an explicit path (origin first).
    /// Fuel / range / Reaver gates stay in the kernel — UI must not invent fuel math.
    /// </summary>
    bool TryFullBurn(IReadOnlyList<string> path, out string? error);

    /// <summary>Shortest-path Full Burn via kernel <c>FlyAction.TryFullBurnTo</c>.</summary>
    bool TryFullBurnTo(string toSectorId, out string? error);

    /// <summary>
    /// Draw the next queued Nav card onto the session's <see cref="NavFaceUp"/>.
    /// FAQ 4.1 p.14: Contact / Alert Tokens before Nav — kernel throws / rejects when blocked.
    /// </summary>
    bool TryDrawNav(out string? error);

    /// <summary>Resolve the face-up Nav option by index via <c>NavResolver.TryResolve</c>.</summary>
    bool TryResolveNavOption(int optionIndex, out string? error);

    /// <summary>Kernel <c>DealAction.TryDeal</c> when ConsiderCount is 0 (or keep ids already known).</summary>
    bool TryDeal(DealRequest request, out string? error);

    /// <summary>
    /// Stage a Deal consider: draw jobs into <see cref="DealConsideredJobs"/> without consuming
    /// the Deal action. Finish with <see cref="TryFinishDealConsider"/> or cancel.
    /// </summary>
    bool TryBeginDealConsider(DealRequest request, out string? error);

    /// <summary>Restore staged consider jobs to the Contact deck top; does not spend an action.</summary>
    void CancelDealConsider();

    /// <summary>
    /// Restore staged jobs to the deck top, then <c>DealAction.TryDeal</c> with the chosen keep ids.
    /// </summary>
    bool TryFinishDealConsider(IReadOnlyList<string> keepJobIds, out string? error);

    /// <summary>Kernel <c>BuyAction.TryBuy</c> for the current seat.</summary>
    bool TryBuy(BuyRequest request, out string? error);

    /// <summary>Kernel <c>ShoreLeaveAction.TryShoreLeave</c> (consumes Buy action type).</summary>
    bool TryShoreLeave(out string? error);

    /// <summary>Kernel <c>PoachAction.TryPoach</c> — A Better Offer; not a turn action.</summary>
    bool TryPoach(string fromPlayerId, string crewId, out string? error);
}
