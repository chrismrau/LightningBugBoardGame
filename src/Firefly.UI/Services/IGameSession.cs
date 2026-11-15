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

    /// <param name="seatCount">1 (default smoke) or 2 (rival-sheet smoke). Unique ships/leaders per seat.</param>
    void StartNew(
        string scenarioCardId,
        bool useBlueSun = false,
        bool useBountyDeck = false,
        bool useOperativesCorvette = false,
        int seatCount = 1);

    /// <summary>Configured hotseat seat count (1 or 2 for smoke).</summary>
    int SeatCount { get; }

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

    /// <summary>Kernel <c>WorkAction.TryWork</c> for a Contact Job in hand or Active.</summary>
    bool TryWork(string jobId, out string? error);

    /// <summary>Kernel <c>WorkAction.TryMakeWork</c> — planetary $200 Work.</summary>
    bool TryMakeWork(out string? error);

    /// <summary>Kernel <c>WorkAction.TryDatascope</c> when Early's Datascope is carried.</summary>
    bool TryDatascope(out string? error);

    /// <summary>Kernel <c>WorkAction.TryWorkGoal</c> / Goal Work at a Story Card site.</summary>
    bool TryWorkGoal(int goalNumber, out string? error);

    /// <summary>
    /// Continue Goal Work after Misbehave (or when boarding/skill is already pending).
    /// Kernel <c>GoalWorkAction.TryResume</c> — may suspend into PendingChoice.
    /// </summary>
    bool TryContinueGoalWork(out string? error);

    /// <summary>Draw the next Misbehave card onto <c>GameState.PendingMisbehave.FaceUp</c>.</summary>
    bool TryDrawMisbehave(out string? error);

    /// <summary>Resolve the face-up Misbehave via <c>MisbehaveResolver.TryResolve</c>.</summary>
    bool TryResolveMisbehave(MisbehaveChoice choice, out string? error);

    /// <summary>PBH Confrontation — <c>BountyAction.TryApprehendRival</c>.</summary>
    bool TryBountyConfront(string bountyId, string rivalId, string crewId, out string? error);

    /// <summary>PBH Lone Target — <c>BountyAction.TryApprehendLone</c>.</summary>
    bool TryBountyLone(string bountyId, string crewId, out string? error);

    /// <summary>PBH Betrayal — <c>BountyAction.TryBetray</c>.</summary>
    bool TryBountyBetray(string bountyId, string crewId, out string? error);

    /// <summary>PBH Deliver — <c>BountyAction.TryDeliver</c>.</summary>
    bool TryBountyDeliver(string bountyId, out string? error);

    /// <summary>PBH Jump / Rescue — <c>BountyAction.TryJump</c>.</summary>
    bool TryBountyJump(string bountyId, string fromPlayerId, bool rescue, out string? error);

    /// <summary>Blue Sun Alert Tokens — <c>AlertTokenResolver.TryResolvePending</c>.</summary>
    bool TryResolveAlertTokens(out string? error);

    /// <summary>
    /// Resolve pending Cruiser / Corvette / Reaver encounter.
    /// Option ids come from <see cref="PendingHostBuilder"/> encounter chrome.
    /// </summary>
    bool TryResolveEncounter(string optionId, out string? error);
}
