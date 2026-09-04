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
        public TokenKind? PendingEncounter { get; set; }
        public string? PendingEncounterSectorId { get; set; }
        public NavDecks? Decks { get; set; }
        public JobCatalog? Jobs { get; set; }
        public ContactCatalog? Contacts { get; set; }
        public ContactDecks? ContactDecks { get; set; }
        public CrewCatalog? Crew { get; set; }
        public LeaderCatalog? Leaders { get; set; }
        public ShipCatalog? Ships { get; set; }
        public SupplyCatalog? Supply { get; set; }
        public SupplyDecks? SupplyDecks { get; set; }
        public MisbehaveDeck? Misbehave { get; set; }
        public MisbehaveCatalog? MisbehaveCatalog { get; set; }
        public GearIndex? Gear { get; set; }
        public SetupCard? Setup { get; set; }
        public ScenarioCard? Scenario { get; set; }
        public PendingMisbehave? PendingMisbehave { get; set; }

        public bool ActionTaken => ActionsUsedThisTurn > 0;
        public bool TurnComplete => ActionsUsedThisTurn >= ActionsPerTurn;
        public bool HasPendingEvents =>
            PendingNavDraws.Count > 0 || PendingEncounter.HasValue || PendingMisbehave != null;

        public GameState(SectorMap map, IReadOnlyList<PlayerState> players, MapTokens? tokens = null, NavDecks? decks = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            if (players == null || players.Count == 0)
                throw new ArgumentException("At least one player is required.", nameof(players));
            Players = players;
            Tokens = tokens ?? MapTokens.None;
            PendingNavDraws = new List<PendingNavDraw>();
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
            PendingEncounter = null;
            PendingEncounterSectorId = null;
            PendingMisbehave = null;
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
                error = "Resolve pending Nav cards, encounters, or Misbehave before taking another action.";
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
            _used.Add(action);
            ActionsUsedThisTurn++;
            LastAction = action;
            return true;
        }

        public bool ActionWasUsed(TurnAction action) => _used.Contains(action);

        private readonly HashSet<TurnAction> _used = new HashSet<TurnAction>();

        public void EndTurn()
        {
            ClearPendingEvents();
            ActionsUsedThisTurn = 0;
            LastAction = TurnAction.None;
            _used.Clear();
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        }
    }
}
