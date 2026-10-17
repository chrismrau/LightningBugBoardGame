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
}
