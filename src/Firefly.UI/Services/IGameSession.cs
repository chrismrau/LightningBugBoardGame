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

    void StartNew(
        string scenarioCardId,
        bool useBlueSun = false,
        bool useBountyDeck = false,
        bool useOperativesCorvette = false);

    void SelectSector(string? sectorId);

    /// <summary>
    /// End the current seat's turn with 0, 1, or 2 actions used.
    /// GF9 BBG p.6: "On your turn, you may take 2 Actions…"
    /// </summary>
    void EndTurn();

    /// <summary>
    /// Submit the current <see cref="GameState.PendingChoice"/> via kernel
    /// <see cref="GameState.TrySubmitChoice"/>. Returns false with an error when rejected.
    /// </summary>
    bool TrySubmitChoice(ChoiceSubmission submission, out string? error);

    /// <summary>
    /// Smoke / test helper: seed a single PendingChoice on the live game.
    /// Does not invent kinds — caller supplies a kernel <see cref="PendingChoice"/>.
    /// </summary>
    bool TrySeedPendingChoice(PendingChoice choice, out string? error);
}
