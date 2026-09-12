using System.Collections.Generic;
using Firefly.Core.Map;

namespace Firefly.Core.Movement
{
    public enum MovementKind
    {
        Mosey,
        FullBurn
    }

    public enum TokenKind
    {
        AllianceCruiser,
        ReaverCutter,
        OperativeCorvette
    }

    public sealed class MovementStep
    {
        public string SectorId { get; }
        public NavRegion NavRegion { get; }
        public bool DrawsNavCard { get; }
        public TokenKind? Encounter { get; }

        public MovementStep(string sectorId, NavRegion navRegion, bool drawsNavCard, TokenKind? encounter)
        {
            SectorId = sectorId;
            NavRegion = navRegion;
            DrawsNavCard = drawsNavCard;
            Encounter = encounter;
        }
    }

    public sealed class MovementPlan
    {
        public MovementKind Kind { get; }
        public string FromSectorId { get; }
        public string ToSectorId { get; }
        public IReadOnlyList<string> Path { get; }
        public IReadOnlyList<MovementStep> EnteredSteps { get; }
        public int Distance { get; }
        public int FuelCost { get; }
        public int NavCardsToDraw { get; }

        public MovementPlan(
            MovementKind kind,
            string fromSectorId,
            string toSectorId,
            IReadOnlyList<string> path,
            IReadOnlyList<MovementStep> enteredSteps,
            int fuelCost)
        {
            Kind = kind;
            FromSectorId = fromSectorId;
            ToSectorId = toSectorId;
            Path = path;
            EnteredSteps = enteredSteps;
            Distance = path.Count > 0 ? path.Count - 1 : 0;
            FuelCost = fuelCost;
            var draws = 0;
            foreach (var step in enteredSteps)
            {
                if (step.DrawsNavCard)
                    draws++;
            }
            NavCardsToDraw = draws;
        }
    }

    public sealed class MapTokens
    {
        public string? AllianceCruiserSectorId { get; }
        public string? OperativeCorvetteSectorId { get; }
        public IReadOnlyList<string> ReaverCutterSectorIds { get; }

        public MapTokens(
            string? allianceCruiserSectorId = null,
            IReadOnlyList<string>? reaverCutterSectorIds = null,
            string? operativeCorvetteSectorId = null)
        {
            AllianceCruiserSectorId = allianceCruiserSectorId;
            ReaverCutterSectorIds = reaverCutterSectorIds ?? new List<string>();
            OperativeCorvetteSectorId = operativeCorvetteSectorId;
        }

        public static MapTokens None { get; } = new MapTokens();

        public MapTokens WithAllianceCruiser(string? sectorId) =>
            new MapTokens(sectorId, ReaverCutterSectorIds, OperativeCorvetteSectorId);

        public MapTokens WithOperativeCorvette(string? sectorId) =>
            new MapTokens(AllianceCruiserSectorId, ReaverCutterSectorIds, sectorId);

        public MapTokens WithReaverCutters(IReadOnlyList<string> sectorIds) =>
            new MapTokens(AllianceCruiserSectorId, sectorIds, OperativeCorvetteSectorId);

        /// <summary>
        /// Moves one Reaver Cutter token to <paramref name="toSectorId"/>.
        /// Only one Reaver ship may occupy a Sector (Blue Sun); rejects stacking Cutters.
        /// </summary>
        public bool TryMoveReaverCutter(string toSectorId, out MapTokens updated, out string? error, int cutterIndex = 0)
        {
            updated = this;
            error = null;
            if (ReaverCutterSectorIds.Count == 0)
            {
                error = "No Reaver Cutter is on the board.";
                return false;
            }
            if (cutterIndex < 0 || cutterIndex >= ReaverCutterSectorIds.Count)
            {
                error = "Invalid Reaver Cutter index.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(toSectorId))
            {
                error = "Reaver Cutter destination is required.";
                return false;
            }
            for (var i = 0; i < ReaverCutterSectorIds.Count; i++)
            {
                if (i == cutterIndex)
                    continue;
                if (ReaverCutterSectorIds[i] == toSectorId)
                {
                    error = "Only 1 Reaver ship may ever be in a Sector.";
                    return false;
                }
            }

            var next = new string[ReaverCutterSectorIds.Count];
            for (var i = 0; i < ReaverCutterSectorIds.Count; i++)
                next[i] = i == cutterIndex ? toSectorId : ReaverCutterSectorIds[i];
            updated = WithReaverCutters(next);
            return true;
        }

        public TokenKind? EncounterAt(string sectorId)
        {
            if (AllianceCruiserSectorId != null && AllianceCruiserSectorId == sectorId)
                return TokenKind.AllianceCruiser;
            if (OperativeCorvetteSectorId != null && OperativeCorvetteSectorId == sectorId)
                return TokenKind.OperativeCorvette;
            foreach (var cutter in ReaverCutterSectorIds)
            {
                if (cutter == sectorId)
                    return TokenKind.ReaverCutter;
            }
            return null;
        }
    }
}
