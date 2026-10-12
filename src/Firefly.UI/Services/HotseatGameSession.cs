using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>
/// In-process hotseat session. Designed so a future SignalR room registry
/// can hold one of these (or the same interface) per lobby code.
/// </summary>
public sealed class HotseatGameSession : IGameSession
{
    public const string DefaultStartSectorId = "alliance-lux-r1-01"; // Persephone
    public const string DefaultLeaderId = "leader_malcolm";
    public const string DefaultShipId = "ship_serenity";

    private readonly object _gate = new();
    private GameState _game;
    private string _scenarioCardId = SmokeScenarios.DefaultId;
    private bool _useBlueSun;
    private bool _useBountyDeck;
    private bool _useOperativesCorvette;
    private string? _selectedSectorId;

    public HotseatGameSession()
    {
        _game = CreateGame(_scenarioCardId, _useBlueSun, _useBountyDeck, _useOperativesCorvette);
    }

    public event Action? Changed;

    public GameState Game
    {
        get { lock (_gate) return _game; }
    }

    public string ScenarioCardId
    {
        get { lock (_gate) return _scenarioCardId; }
    }

    public bool UseBlueSun
    {
        get { lock (_gate) return _useBlueSun; }
    }

    public bool UseBountyDeck
    {
        get { lock (_gate) return _useBountyDeck; }
    }

    public bool UseOperativesCorvette
    {
        get { lock (_gate) return _useOperativesCorvette; }
    }

    public string? SelectedSectorId
    {
        get { lock (_gate) return _selectedSectorId; }
    }

    public void StartNew(
        string scenarioCardId,
        bool useBlueSun = false,
        bool useBountyDeck = false,
        bool useOperativesCorvette = false)
    {
        if (string.IsNullOrWhiteSpace(scenarioCardId))
            throw new ArgumentException("Scenario id is required.", nameof(scenarioCardId));

        lock (_gate)
        {
            _scenarioCardId = scenarioCardId;
            _useBlueSun = useBlueSun;
            _useBountyDeck = useBountyDeck;
            _useOperativesCorvette = useOperativesCorvette;
            _selectedSectorId = null;
            _game = CreateGame(scenarioCardId, useBlueSun, useBountyDeck, useOperativesCorvette);
        }

        Changed?.Invoke();
    }

    public void SelectSector(string? sectorId)
    {
        lock (_gate)
        {
            _selectedSectorId = sectorId;
        }
        Changed?.Invoke();
    }

    public void EndTurn()
    {
        lock (_gate)
        {
            _game.EndTurn();
        }
        Changed?.Invoke();
    }

    private static GameState CreateGame(
        string scenarioCardId,
        bool useBlueSun,
        bool useBountyDeck,
        bool useOperativesCorvette)
    {
        var seats = new[]
        {
            new PlayerSeat(
                id: "p1",
                name: "Mal",
                sectorId: DefaultStartSectorId,
                shipId: DefaultShipId,
                leaderId: DefaultLeaderId)
        };

        return GameSetup.Create(seats, new GameSetupOptions
        {
            ScenarioCardId = scenarioCardId,
            UseBlueSun = useBlueSun,
            UseBountyDeck = useBountyDeck,
            UseOperativesCorvette = useOperativesCorvette,
            UsePiratesBountyHunters = useBountyDeck
        });
    }
}
