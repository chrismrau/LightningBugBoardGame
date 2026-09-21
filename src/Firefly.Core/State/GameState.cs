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
        Buy,
        /// <summary>
        /// Crew-ability actions (Morale Booster / Love Bot). Counts as one of the two
        /// turn actions; may not repeat the same action type (GF9 two-action turn).
        /// </summary>
        Crew
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
        /// Consecutive "The Big Black" Nav resolves during the current Full Burn Fly Action
        /// (Emissions Recycler). Cleared on new Fly / EndTurn.
        /// </summary>
        public int ConsecutiveBigBlackNavThisFly { get; set; }
        /// <summary>
        /// Emissions Recycler already took its once-per-Fly Fuel this Fly Action.
        /// </summary>
        public bool EmissionsFuelTakenThisFly { get; set; }
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
        public ShipUpgradeIndex? ShipUpgradeCatalog { get; set; }
        public SetupCard? Setup { get; set; }
        public ScenarioCard? Scenario { get; set; }
        public PendingMisbehave? PendingMisbehave { get; set; }
        /// <summary>
        /// Shared player-decision wait (at most one). Suspend mid-resolve; resume via
        /// <see cref="TrySubmitChoice"/>. Consumers migrate later — auto-resolve stays until wired.
        /// </summary>
        public PendingChoice? PendingChoice { get; private set; }
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

        /// <summary>
        /// Time's Not on Our Side: Disgruntled Tokens used as Game Length Tokens.
        /// Null when that Setup is not active. Discarded when the first player begins a turn.
        /// </summary>
        public int? GameLengthTokensRemaining { get; set; }
        /// <summary>
        /// Seat index of the player who holds / discards Game Length Tokens (first turn seat).
        /// </summary>
        public int FirstPlayerIndex { get; set; }
        /// <summary>
        /// True after the final Game Length Token is discarded; each player then gets one final turn.
        /// </summary>
        public bool GameLengthFinalRound { get; set; }

        public bool ActionTaken => ActionsUsedThisTurn > 0;
        public bool TurnComplete => ActionsUsedThisTurn >= ActionsPerTurn;
        public bool HasPendingEvents =>
            PendingNavDraws.Count > 0
            || PendingAlertSectors.Count > 0
            || PendingEncounter.HasValue
            || PendingMisbehave != null
            || PendingChoice != null;

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

        /// <summary>
        /// Seat index of <paramref name="playerId"/>, or -1 when unknown.
        /// </summary>
        public int IndexOfPlayer(string playerId)
        {
            for (var i = 0; i < Players.Count; i++)
            {
                if (string.Equals(Players[i].Id, playerId, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// GF9 / card text "player to the right": next seat clockwise in table order
        /// (index + 1, wrapping). Solo games return the same player.
        /// </summary>
        public PlayerState PlayerToTheRightOf(string playerId)
        {
            var index = IndexOfPlayer(playerId);
            if (index < 0)
                throw new KeyNotFoundException($"Unknown player '{playerId}'.");
            if (Players.Count == 1)
                return Players[0];
            return Players[(index + 1) % Players.Count];
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
            PendingChoice = null;
            FlyRangeBonusThisAction = 0;
            DiscardFuelPerExtraSectorThisFly = false;
            ConsecutiveBigBlackNavThisFly = 0;
            EmissionsFuelTakenThisFly = false;
        }

        /// <summary>
        /// Suspend mid-resolve with exactly one pending choice. Fails if one is already set.
        /// </summary>
        public bool TrySetPendingChoice(PendingChoice choice, out string? error)
        {
            error = null;
            if (choice == null)
            {
                error = "A pending choice is required.";
                return false;
            }
            if (PendingChoice != null)
            {
                error = "A choice is already pending; only one PendingChoice is allowed at a time.";
                return false;
            }
            PendingChoice = choice;
            return true;
        }

        /// <summary>
        /// Resume after a pending choice. Clears <see cref="PendingChoice"/> on success and
        /// returns the resolved wait state so the caller can continue the suspended action.
        /// Rejects when none is pending, the player does not match, or a discrete option is illegal.
        /// </summary>
        public bool TrySubmitChoice(
            string playerId,
            ChoiceSubmission submission,
            out PendingChoice? resolved,
            out string? error)
        {
            resolved = null;
            error = null;
            if (PendingChoice == null)
            {
                error = "No choice is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(playerId) ||
                !string.Equals(PendingChoice.PlayerId, playerId, StringComparison.Ordinal))
            {
                error = "Choice must be submitted by the player who owns the pending choice.";
                return false;
            }
            if (submission == null)
            {
                error = "A choice submission is required.";
                return false;
            }
            if (PendingChoice.Options != null && PendingChoice.Options.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(submission.SelectedOptionId))
                {
                    error = "A selected option id is required.";
                    return false;
                }
                var legal = false;
                foreach (var option in PendingChoice.Options)
                {
                    if (string.Equals(option, submission.SelectedOptionId, StringComparison.Ordinal))
                    {
                        legal = true;
                        break;
                    }
                }
                if (!legal)
                {
                    error = $"Option '{submission.SelectedOptionId}' is not legal for this choice.";
                    return false;
                }
            }

            resolved = PendingChoice;
            PendingChoice = null;
            return true;
        }

        /// <summary>Clear a pending choice without submitting (tests / EndTurn / abort).</summary>
        public void ClearPendingChoice()
        {
            PendingChoice = null;
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
                error = "Resolve pending Alert Tokens, Nav cards, encounters, Misbehave, or choices before taking another action.";
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
                ConsecutiveBigBlackNavThisFly = 0;
                EmissionsFuelTakenThisFly = false;
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
            if (GameOver)
                return;

            // Time's Not: after the last token is discarded, everyone gets one final turn;
            // when play wraps back to the first player, time has run out.
            if (GameLengthFinalRound && CurrentPlayerIndex == FirstPlayerIndex)
            {
                WinCheck.ClaimMostCreditsTimeExpired(this);
                return;
            }

            TryDiscardGameLengthTokenAtTurnStart();
            if (!GameOver)
            {
                WinCheck.Refresh(this, WinPhase.StartOfTurn);
                QueueStartOfTurnReaverContact();
            }
        }

        /// <summary>
        /// SetupCards.json Time's Not: "Give a pile of 20 Disgruntled Tokens to the player taking
        /// the first turn… Each time that player takes a turn, discard one…"
        /// Call once when the game begins (first player's opening turn).
        /// </summary>
        public void BeginOpeningTurn()
        {
            TryDiscardGameLengthTokenAtTurnStart();
        }

        /// <summary>
        /// Discard one Game Length Token when the first player begins a turn.
        /// When the final token is discarded, <see cref="GameLengthFinalRound"/> starts.
        /// </summary>
        internal void TryDiscardGameLengthTokenAtTurnStart()
        {
            if (GameLengthTokensRemaining is not int remaining || remaining <= 0)
                return;
            if (CurrentPlayerIndex != FirstPlayerIndex)
                return;

            GameLengthTokensRemaining = remaining - 1;
            if (GameLengthTokensRemaining == 0)
                GameLengthFinalRound = true;
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
