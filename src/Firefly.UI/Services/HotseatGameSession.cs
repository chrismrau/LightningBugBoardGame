using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Movement;
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
    private MovementEngine _movement = null!;
    private FlyAction _fly = null!;
    private NavResolver _nav = null!;
    private readonly IRng _rng = new SystemRng(Environment.TickCount);

    public HotseatGameSession()
    {
        _game = CreateGame(_scenarioCardId, _useBlueSun, _useBountyDeck, _useOperativesCorvette);
        WireEngines(_game);
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

    public DrawnNav? NavFaceUp
    {
        get { lock (_gate) return _nav.FaceUp; }
    }

    public MovementEngine Movement
    {
        get { lock (_gate) return _movement; }
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
            WireEngines(_game);
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
            DiscardFaceUpNavIfAny();
            _game.EndTurn();
            _nav = new NavResolver();
        }
        Changed?.Invoke();
    }

    public bool TrySubmitChoice(ChoiceSubmission submission, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = NavChoiceResume.TryResume(_game, _nav, submission, _rng, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TrySeedPendingChoice(PendingChoice choice, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _game.TrySetPendingChoice(choice, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryMosey(string toSectorId, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _fly.TryMosey(_game, _game.CurrentPlayer.Id, toSectorId, out _, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryFullBurn(IReadOnlyList<string> path, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _fly.TryFullBurn(_game, _game.CurrentPlayer.Id, path, out _, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryFullBurnTo(string toSectorId, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _fly.TryFullBurnTo(_game, _game.CurrentPlayer.Id, toSectorId, out _, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryDrawNav(out string? error)
    {
        error = null;
        lock (_gate)
        {
            try
            {
                if (_nav.FaceUp != null)
                {
                    error = "A Nav card is already face up — resolve it first.";
                    return false;
                }
                if (_game.PendingNavDraws.Count == 0)
                {
                    error = "No pending Nav draws.";
                    return false;
                }
                _nav.DrawNext(_game);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        Changed?.Invoke();
        return true;
    }

    public bool TryResolveNavOption(int optionIndex, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _nav.TryResolve(_game, optionIndex, out _, out error, _rng);
            // TryResolve returns false when it suspends into PendingChoice; still notify UI.
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    private void DiscardFaceUpNavIfAny()
    {
        var faceUp = _nav.FaceUp;
        if (faceUp == null || _game.Decks == null)
            return;
        _game.Decks.For(faceUp.Region).ResolveIntoDiscard(faceUp.Card);
    }

    private void WireEngines(GameState game)
    {
        _movement = new MovementEngine(game.Map);
        _fly = new FlyAction(_movement);
        _nav = new NavResolver();
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
