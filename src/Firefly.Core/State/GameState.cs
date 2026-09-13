using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;

namespace Firefly.Core.State
{
    public enum TurnAction
    {
        None,
        Fly,
        Deal,
        Work,
        Buy
    }

    public sealed class PendingNavDraw
    {
        public string SectorId { get; }
        public NavRegion Region { get; }

        public PendingNavDraw(string sectorId, NavRegion region)
        {
            SectorId = sectorId;
            Region = region;
        }
    }

    public sealed class GameState
    {
        public SectorMap Map { get; }
        public IReadOnlyList<PlayerState> Players { get; }
        public MapTokens Tokens { get; set; }
        public int CurrentPlayerIndex { get; private set; }
        public int ActionsPerTurn { get; set; } = 2;
        public int ActionsUsedThisTurn { get; private set; }
        public TurnAction LastAction { get; private set; }
        public IList<PendingNavDraw> PendingNavDraws { get; }
        /// <summary>
        /// Extra Full Burn sectors granted by Nav this Fly Action (First Rule / Grav-Well).
        /// Spent by <see cref="Actions.FlyAction.TryContinueFullBurn"/>; cleared on EndTurn / new Fly / non-Fly action.
        /// </summary>
        public int FlyRangeBonusThisAction { get; set; }
        /// <summary>
        /// Fuel Coupling Failure: discard 1 Fuel per extra Sector entered after the Nav option.
        /// </summary>
        public bool DiscardFuelPerExtraSectorThisFly { get; set; }
        /// <summary>
        /// Sectors entered during Fly that still need Alert Token resolution before their Nav draw.
        /// </summary>
        public IList<string> PendingAlertSectors { get; }
        public TokenKind? PendingEncounter { get; set; }
        public string? PendingEncounterSectorId { get; set; }
        /// <summary>
        /// Player who must resolve <see cref="PendingEncounter"/> (FAQ multi-seat Cruiser Contact).
        /// Null means <see cref="CurrentPlayer"/> (legacy / named Cruiser Nav on the flyer).
        /// </summary>
        public string? PendingEncounterPlayerId { get; set; }
        /// <summary>
        /// Additional Outlaw player ids waiting for Alliance Cruiser Contact after the head pending.
        /// </summary>
        public IList<string> PendingAllianceContactQueue { get; }
        /// <summary>
        /// Fly entered the Cruiser Sector and skipped Nav (FAQ Contact-before-Nav). Cry Baby can
        /// clear Contact and restore that Sector's Nav draw.
        /// </summary>
        public bool PendingEncounterDeferredNav { get; set; }
        /// <summary>
        /// Blue Sun physical Alert Tokens (spawn / resolve / permanent Reaver Space).
        /// Off for core-only; on when <see cref="GameSetupOptions.UseBlueSun"/> unless a Setup card disables them.
        /// </summary>
        public bool UseAlertTokens { get; set; }
        /// <summary>
        /// Pirates &amp; Bounty Hunters: piracy Work / boarding (optional expansion).
        /// Set when <see cref="GameSetupOptions.UsePiratesBountyHunters"/> or <see cref="GameSetupOptions.UseBountyDeck"/>.
        /// </summary>
        public bool UsePiratesBountyHunters { get; set; }
        public NavDecks? Decks { get; set; }
        public JobCatalog? Jobs { get; set; }
        public ContactCatalog? Contacts { get; set; }
        public ContactDecks? ContactDecks { get; set; }
        public CrewCatalog? Crew { get; set; }
        public LeaderCatalog? Leaders { get; set; }
        public ShipCatalog? Ships { get; set; }
        public DriveCoreCatalog? DriveCores { get; set; }
        public SupplyCatalog? Supply { get; set; }
        public SupplyDecks? SupplyDecks { get; set; }
        public MisbehaveDeck? Misbehave { get; set; }
        public MisbehaveCatalog? MisbehaveCatalog { get; set; }
        public GearIndex? Gear { get; set; }
        public SetupCard? Setup { get; set; }
        public ScenarioCard? Scenario { get; set; }
        public PendingMisbehave? PendingMisbehave { get; set; }
        /// <summary>
        /// FAQ 4.1 p.2: Gear may not be switched during a Work Action.
        /// Set while Work/Misbehave for the current attempt is in flight.
        /// </summary>
        public bool WorkGearLocked { get; set; }
        public BountyCatalog? Bounties { get; set; }
        public BountyDeck? BountyDeck { get; set; }
        public AllianceAlertCatalog? AllianceAlerts { get; set; }
        public AllianceAlertDeck? AllianceAlertDeck { get; set; }
        public ISet<string> RemovedFromPlay { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string? WinnerId { get; set; }
        public string? WinReason { get; set; }
        public bool GameOver => !string.IsNullOrEmpty(WinnerId);

        public bool ActionTaken => ActionsUsedThisTurn > 0;
        public bool TurnComplete => ActionsUsedThisTurn >= ActionsPerTurn;
        public bool HasPendingEvents =>
            PendingNavDraws.Count > 0
            || PendingAlertSectors.Count > 0
            || PendingEncounter.HasValue
            || PendingMisbehave != null;

        public GameState(SectorMap map, IReadOnlyList<PlayerState> players, MapTokens? tokens = null, NavDecks? decks = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            if (players == null || players.Count == 0)
                throw new ArgumentException("At least one player is required.", nameof(players));
            Players = players;
            Tokens = tokens ?? MapTokens.None;
            PendingNavDraws = new List<PendingNavDraw>();
            PendingAlertSectors = new List<string>();
            PendingAllianceContactQueue = new List<string>();
            Decks = decks;
        }

        public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];

        public PlayerState GetPlayer(string playerId)
        {
            foreach (var player in Players)
            {
                if (player.Id == playerId)
                    return player;
            }
            throw new KeyNotFoundException($"Unknown player '{playerId}'.");
        }

        public void ClearPendingEvents()
        {
            PendingNavDraws.Clear();
            PendingAlertSectors.Clear();
            PendingEncounter = null;
            PendingEncounterSectorId = null;
            PendingEncounterPlayerId = null;
            PendingAllianceContactQueue.Clear();
            PendingEncounterDeferredNav = false;
            PendingMisbehave = null;
            FlyRangeBonusThisAction = 0;
            DiscardFuelPerExtraSectorThisFly = false;
        }

        public bool CanTakeAction(TurnAction action, out string? error)
        {
            error = null;
            if (action == TurnAction.None)
            {
                error = "A real action is required.";
                return false;
            }
            if (HasPendingEvents)
            {
                error = "Resolve pending Alert Tokens, Nav cards, encounters, or Misbehave before taking another action.";
                return false;
            }
            if (TurnComplete)
            {
                error = "This player has already taken both actions this turn.";
                return false;
            }
            if (ActionWasUsed(action))
            {
                error = $"{action} was already used this turn.";
                return false;
            }
            return true;
        }

        public bool TryConsumeAction(TurnAction action, out string? error)
        {
            if (!CanTakeAction(action, out error))
                return false;
            // Range / fuel-coupling bonuses are for the current Fly Action only.
            if (action != TurnAction.Fly)
            {
                FlyRangeBonusThisAction = 0;
                DiscardFuelPerExtraSectorThisFly = false;
            }
            _used.Add(action);
            ActionsUsedThisTurn++;
            LastAction = action;
            return true;
        }

        public bool ActionWasUsed(TurnAction action) => _used.Contains(action);

        private readonly HashSet<TurnAction> _used = new HashSet<TurnAction>();

        public void EndTurn()
        {
            WinCheck.Refresh(this, WinPhase.EndOfTurn);
            ClearPendingEvents();
            WorkGearLocked = false;
            ActionsUsedThisTurn = 0;
            LastAction = TurnAction.None;
            _used.Clear();
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
            if (!GameOver)
            {
                WinCheck.Refresh(this, WinPhase.StartOfTurn);
                QueueStartOfTurnReaverContact();
            }
        }

        /// <summary>
        /// GF9 p.8: "If you start your turn in the same Sector as the Reaver Cutter,
        /// resolve the Reaver Contact event."
        /// </summary>
        private void QueueStartOfTurnReaverContact()
        {
            if (Tokens.EncounterAt(CurrentPlayer.SectorId) != TokenKind.ReaverCutter)
                return;
            PendingEncounter = TokenKind.ReaverCutter;
            PendingEncounterSectorId = CurrentPlayer.SectorId;
        }
    }
}
