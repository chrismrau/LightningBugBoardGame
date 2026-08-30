using System;
using System.Collections.Generic;
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
        public bool ActionTaken { get; set; }
        public TurnAction LastAction { get; set; }
        public IList<PendingNavDraw> PendingNavDraws { get; }
        public TokenKind? PendingEncounter { get; set; }
        public string? PendingEncounterSectorId { get; set; }

        public GameState(SectorMap map, IReadOnlyList<PlayerState> players, MapTokens? tokens = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            if (players == null || players.Count == 0)
                throw new ArgumentException("At least one player is required.", nameof(players));
            Players = players;
            Tokens = tokens ?? MapTokens.None;
            PendingNavDraws = new List<PendingNavDraw>();
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
        }

        public void EndTurn()
        {
            ClearPendingEvents();
            ActionTaken = false;
            LastAction = TurnAction.None;
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        }
    }
}
