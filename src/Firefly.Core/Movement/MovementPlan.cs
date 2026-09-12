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
