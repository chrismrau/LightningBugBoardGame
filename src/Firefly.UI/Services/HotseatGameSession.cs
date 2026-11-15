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

    /// <summary>Second smoke seat — Santo / Zoe / next core Firefly (Yun Qi).</summary>
    public const string SecondStartSectorId = "alliance-qin-shi-huang-r1-01"; // Santo
    public const string SecondLeaderId = "leader_zoe_jetwash";
    public const string SecondShipId = "ship_yun-qi";

    private readonly object _gate = new();
    private GameState _game;
    private string _scenarioCardId = SmokeScenarios.DefaultId;
    private bool _useBlueSun;
    private bool _useBountyDeck;
    private bool _useOperativesCorvette;
    private int _seatCount = 1;
    private string? _selectedSectorId;
    private MovementEngine _movement = null!;
    private FlyAction _fly = null!;
    private NavResolver _nav = null!;
    private DealAction _deal = null!;
    private BuyAction _buy = null!;
    private ShoreLeaveAction _shore = null!;
    private PoachAction _poach = null!;
    private WorkAction _work = null!;
    private MisbehaveResolver _misbehave = null!;
    private GoalWorkAction _goals = null!;
    private BountyAction _bounty = null!;
    private readonly IRng _rng = new SystemRng(Environment.TickCount);

    private DealRequest? _dealStagingRequest;
    private List<JobCard>? _dealStagingDrawn;
    private string? _dealStagingContact;

    public HotseatGameSession()
    {
        _game = CreateGame(_scenarioCardId, _useBlueSun, _useBountyDeck, _useOperativesCorvette, _seatCount);
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

    public int SeatCount
    {
        get { lock (_gate) return _seatCount; }
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

    public IReadOnlyList<JobCard>? DealConsideredJobs
    {
        get { lock (_gate) return _dealStagingDrawn; }
    }

    public void StartNew(
        string scenarioCardId,
        bool useBlueSun = false,
        bool useBountyDeck = false,
        bool useOperativesCorvette = false,
        int seatCount = 1)
    {
        if (string.IsNullOrWhiteSpace(scenarioCardId))
            throw new ArgumentException("Scenario id is required.", nameof(scenarioCardId));
        if (seatCount is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(seatCount), "Hotseat smoke supports 1 or 2 seats.");

        lock (_gate)
        {
            _scenarioCardId = scenarioCardId;
            _useBlueSun = useBlueSun;
            _useBountyDeck = useBountyDeck;
            _useOperativesCorvette = useOperativesCorvette;
            _seatCount = seatCount;
            _selectedSectorId = null;
            ClearDealStaging();
            _game = CreateGame(scenarioCardId, useBlueSun, useBountyDeck, useOperativesCorvette, seatCount);
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
            CancelDealConsiderUnlocked();
            DiscardFaceUpNavIfAny();
            _game.EndTurn();
            _nav = new NavResolver();
        }
        Changed?.Invoke();
    }

    public bool TrySubmitChoice(ChoiceSubmission submission, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            var kind = _game.PendingChoice?.Kind;
            if (string.Equals(kind, PendingChoiceKinds.DealSellParts, StringComparison.Ordinal))
            {
                ok = _deal.TryResumeDealSellParts(_game, submission, out _, out error);
                suspended = !ok && _game.PendingChoice != null;
            }
            else if (string.Equals(kind, PendingChoiceKinds.HavenFuelAmount, StringComparison.Ordinal))
            {
                ok = _shore.TryResumeHavenFuel(_game, submission, out _, out error);
                suspended = !ok && _game.PendingChoice != null;
            }
            else if (MisbehaveChoiceResume.OwnsPendingChoice(_game))
            {
                ok = MisbehaveChoiceResume.TryResume(
                    _game, _misbehave, _work, _goals, submission, _rng, out error);
                suspended = !ok && _game.PendingChoice != null;
            }
            else if (IsAlertTokenChoice(kind))
            {
                ok = AlertTokenResolver.TryResume(_game, submission, _rng, out _, out error);
                suspended = !ok && _game.PendingChoice != null;
            }
            else
            {
                ok = NavChoiceResume.TryResume(_game, _nav, submission, _rng, out error);
                suspended = !ok && _game.PendingChoice != null;
            }
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
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

    public bool TryDeal(DealRequest request, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            CancelDealConsiderUnlocked();
            ok = _deal.TryDeal(_game, _game.CurrentPlayer.Id, request, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryBeginDealConsider(DealRequest request, out string? error)
    {
        lock (_gate)
        {
            CancelDealConsiderUnlocked();
            if (request == null || string.IsNullOrWhiteSpace(request.ContactName))
            {
                error = "A contact is required.";
                return false;
            }
            if (request.ConsiderCount <= 0)
            {
                error = "Consider count must be at least 1 to stage a consider.";
                return false;
            }
            if (!_game.CanTakeAction(TurnAction.Deal, out error))
                return false;
            if (_game.Contacts == null || _game.ContactDecks == null)
            {
                error = "Contact and job decks are not loaded.";
                return false;
            }
            if (!_game.Contacts.TryFindByName(request.ContactName, out var contact))
            {
                error = $"Unknown contact '{request.ContactName}'.";
                return false;
            }
            var player = _game.CurrentPlayer;
            var at = DealAction.IsAtContact(_game, player, contact);
            if (!DealAction.CanReachContact(_game, player, contact, at, out error))
                return false;
            if (!_game.ContactDecks.TryGet(contact.Name, out var deck))
            {
                error = $"No job deck for {contact.Name}.";
                return false;
            }

            var limit = DealAction.ConsiderLimit(player, contact, remote: !at);
            if (request.ConsiderCount > limit)
            {
                error = $"May consider at most {limit} job(s) with this Contact.";
                return false;
            }

            var drawn = deck.DrawConsider(request.ConsiderCount);
            if (drawn.Count == 0)
            {
                error = $"{contact.Name}'s job deck is empty.";
                return false;
            }

            _dealStagingRequest = CloneDealRequest(request);
            _dealStagingRequest.ConsiderCount = drawn.Count;
            _dealStagingDrawn = drawn.ToList();
            _dealStagingContact = contact.Name;
            error = null;
        }
        Changed?.Invoke();
        return true;
    }

    public void CancelDealConsider()
    {
        lock (_gate)
        {
            CancelDealConsiderUnlocked();
        }
        Changed?.Invoke();
    }

    public bool TryFinishDealConsider(IReadOnlyList<string> keepJobIds, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            if (_dealStagingRequest == null
                || _dealStagingDrawn == null
                || string.IsNullOrWhiteSpace(_dealStagingContact))
            {
                error = "No Deal consider is staged.";
                return false;
            }
            if (_game.ContactDecks == null
                || !_game.ContactDecks.TryGet(_dealStagingContact, out var deck))
            {
                error = $"No job deck for {_dealStagingContact}.";
                return false;
            }

            var request = CloneDealRequest(_dealStagingRequest);
            request.KeepFromConsidered = keepJobIds?.ToList() ?? new List<string>();
            var drawn = _dealStagingDrawn;
            deck.RestoreToTop(drawn);
            ClearDealStaging();

            ok = _deal.TryDeal(_game, _game.CurrentPlayer.Id, request, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryBuy(BuyRequest request, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _buy.TryBuy(_game, _game.CurrentPlayer.Id, request, out _, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryShoreLeave(out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _shore.TryShoreLeave(_game, _game.CurrentPlayer.Id, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryPoach(string fromPlayerId, string crewId, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _poach.TryPoach(
                _game,
                _game.CurrentPlayer.Id,
                fromPlayerId,
                crewId,
                out _,
                out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryWork(string jobId, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _work.TryWork(_game, _game.CurrentPlayer.Id, jobId, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryMakeWork(out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _work.TryMakeWork(_game, _game.CurrentPlayer.Id, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryDatascope(out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _work.TryDatascope(_game, _game.CurrentPlayer.Id, out _, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryWorkGoal(int goalNumber, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _work.TryWorkGoal(_game, _game.CurrentPlayer.Id, goalNumber, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryContinueGoalWork(out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            var pending = _game.PendingGoalWork;
            if (pending == null)
            {
                error = "No Goal Work is pending.";
                return false;
            }

            if (_game.PendingMisbehave != null)
            {
                error = "Finish Misbehave before continuing Goal Work.";
                return false;
            }

            // After Misbehave Proceed chain, phase is Boarding/Skill — TryResume continues.
            // If still marked Misbehave (edge), TryContinueAfterMisbehave advances the phase.
            if (pending.Phase == GoalWorkPhase.Misbehave)
                ok = _goals.TryContinueAfterMisbehave(_game, pending.PlayerId, out _, out error);
            else
                ok = _goals.TryResume(_game, pending.PlayerId, out _, out error);

            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryDrawMisbehave(out string? error)
    {
        error = null;
        lock (_gate)
        {
            try
            {
                if (_game.PendingMisbehave == null)
                {
                    error = "No Misbehave is pending.";
                    return false;
                }
                if (_game.PendingMisbehave.FaceUp != null)
                {
                    error = "A Misbehave card is already face up — resolve it first.";
                    return false;
                }
                _misbehave.DrawNext(_game);
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

    public bool TryResolveMisbehave(MisbehaveChoice choice, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _misbehave.TryResolve(
                _game,
                _game.CurrentPlayer.Id,
                choice,
                out _,
                out error,
                _rng);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryBountyConfront(string bountyId, string rivalId, string crewId, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            var player = _game.CurrentPlayer;
            var rival = _game.GetPlayer(rivalId);
            var attack = WorkChromeBuilder.BestAttackSkill(player);
            var defend = WorkChromeBuilder.BestDefendSkill(rival);
            var board = WorkChromeBuilder.BestBoardSkill(player, rival);
            ok = _bounty.TryApprehendRival(
                _game, player.Id, bountyId, rivalId, crewId,
                attack, defend, board, _rng,
                out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryBountyLone(string bountyId, string crewId, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            var player = _game.CurrentPlayer;
            var attack = WorkChromeBuilder.BestAttackSkill(player);
            ok = _bounty.TryApprehendLone(
                _game, player.Id, bountyId, crewId, attack, _rng,
                out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryBountyBetray(string bountyId, string crewId, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = _bounty.TryBetray(
                _game, _game.CurrentPlayer.Id, bountyId, crewId,
                out _, out error, _rng);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryBountyDeliver(string bountyId, out string? error)
    {
        bool ok;
        lock (_gate)
        {
            ok = _bounty.TryDeliver(_game, _game.CurrentPlayer.Id, bountyId, out _, out error);
        }
        if (ok)
            Changed?.Invoke();
        return ok;
    }

    public bool TryBountyJump(string bountyId, string fromPlayerId, bool rescue, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            var player = _game.CurrentPlayer;
            var rival = _game.GetPlayer(fromPlayerId);
            var attack = WorkChromeBuilder.BestAttackSkill(player);
            var defend = WorkChromeBuilder.BestDefendSkill(rival);
            var board = WorkChromeBuilder.BestBoardSkill(player, rival);
            ok = _bounty.TryJump(
                _game, player.Id, fromPlayerId, bountyId,
                attack, defend, board, _rng, rescue,
                out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryResolveAlertTokens(out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = AlertTokenResolver.TryResolvePending(_game, _rng, out _, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    public bool TryResolveEncounter(string optionId, out string? error)
    {
        bool ok;
        var suspended = false;
        lock (_gate)
        {
            ok = ResolveEncounterUnlocked(optionId, out error);
            suspended = !ok && _game.PendingChoice != null;
        }
        if (ok || suspended)
            Changed?.Invoke();
        return ok || suspended;
    }

    private bool ResolveEncounterUnlocked(string optionId, out string? error)
    {
        error = null;
        if (!_game.PendingEncounter.HasValue)
        {
            error = "No encounter is pending.";
            return false;
        }

        switch (_game.PendingEncounter.Value)
        {
            case TokenKind.OperativeCorvette:
            {
                CorvetteContactChoice? choice = null;
                if (!string.IsNullOrWhiteSpace(optionId) && optionId != "resolve")
                {
                    choice = new CorvetteContactChoice { RemoveWantedCrewId = optionId };
                }
                return CorvetteContact.TryResolve(_game, out _, out error, choice);
            }
            case TokenKind.AllianceCruiser:
            {
                CruiserBoardingChoice? choice = null;
                if (!string.IsNullOrWhiteSpace(optionId)
                    && optionId.StartsWith("incarcerate:", StringComparison.OrdinalIgnoreCase))
                {
                    choice = new CruiserBoardingChoice
                    {
                        UseIncarcerationOrder = true,
                        RemoveWantedCrewId = optionId["incarcerate:".Length..]
                    };
                }
                return CruiserBoarding.TryResolve(_game, _rng, out _, out error, choice);
            }
            case TokenKind.ReaverCutter:
                error =
                    "Reaver Contact requires an evade sector (ReaverContact.TryResolve); UI picker is a documented P9 gap.";
                return false;
            default:
                error = $"Encounter '{_game.PendingEncounter}' has no hotseat resolve wired.";
                return false;
        }
    }

    private bool IsAlertTokenChoice(string? kind)
    {
        if (string.Equals(kind, PendingChoiceKinds.AlertAllianceShip, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.AlertReaverCutter, StringComparison.Ordinal))
            return true;

        if (!string.Equals(kind, PendingChoiceKinds.SectorDestination, StringComparison.Ordinal))
            return false;

        var contextId = _game.PendingChoice?.ContextId ?? "";
        if (SectorDestinationContexts.TryParseAlertSafeHarbor(contextId, out _))
            return true;
        return string.Equals(
            contextId,
            SectorDestinationContexts.AlertDriveOffReaver,
            StringComparison.Ordinal);
    }

    private void CancelDealConsiderUnlocked()
    {
        if (_dealStagingDrawn != null
            && !string.IsNullOrWhiteSpace(_dealStagingContact)
            && _game.ContactDecks != null
            && _game.ContactDecks.TryGet(_dealStagingContact, out var deck))
        {
            deck.RestoreToTop(_dealStagingDrawn);
        }
        ClearDealStaging();
    }

    private void ClearDealStaging()
    {
        _dealStagingRequest = null;
        _dealStagingDrawn = null;
        _dealStagingContact = null;
    }

    private static DealRequest CloneDealRequest(DealRequest source) =>
        new()
        {
            ContactName = source.ContactName,
            ConsiderCount = source.ConsiderCount,
            KeepFromConsidered = source.KeepFromConsidered?.ToList() ?? new List<string>(),
            TakeFromDiscard = source.TakeFromDiscard?.ToList() ?? new List<string>(),
            SellContraband = source.SellContraband,
            SellCargo = source.SellCargo,
            SellParts = source.SellParts,
            ClearWarrants = source.ClearWarrants,
            LoadPassengers = source.LoadPassengers,
            LoadFugitives = source.LoadFugitives,
            BuyContraband = source.BuyContraband,
            BuyCargo = source.BuyCargo,
            BuyFuel = source.BuyFuel,
            MisbehaveReorderIds = source.MisbehaveReorderIds?.ToList() ?? new List<string>()
        };

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
        _deal = new DealAction();
        _buy = new BuyAction();
        _shore = new ShoreLeaveAction();
        _poach = new PoachAction();
        _work = new WorkAction();
        _misbehave = new MisbehaveResolver();
        _goals = new GoalWorkAction();
        _bounty = new BountyAction();
        ClearDealStaging();
    }

    private static GameState CreateGame(
        string scenarioCardId,
        bool useBlueSun,
        bool useBountyDeck,
        bool useOperativesCorvette,
        int seatCount)
    {
        var seats = new List<PlayerSeat>
        {
            new(
                id: "p1",
                name: "Mal",
                sectorId: DefaultStartSectorId,
                shipId: DefaultShipId,
                leaderId: DefaultLeaderId)
        };

        if (seatCount >= 2)
        {
            seats.Add(new PlayerSeat(
                id: "p2",
                name: "Zoe",
                sectorId: SecondStartSectorId,
                shipId: SecondShipId,
                leaderId: SecondLeaderId));
        }

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
