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

        /// <summary>
        /// Removable Blue Sun Alert Tokens by sector. Permanent Reaver Space alerts are not stored here.
        /// </summary>
        public IReadOnlyDictionary<string, SectorAlertCounts> AlertTokens { get; }

        public MapTokens(
            string? allianceCruiserSectorId = null,
            IReadOnlyList<string>? reaverCutterSectorIds = null,
            string? operativeCorvetteSectorId = null,
            IReadOnlyDictionary<string, SectorAlertCounts>? alertTokens = null)
        {
            AllianceCruiserSectorId = allianceCruiserSectorId;
            ReaverCutterSectorIds = reaverCutterSectorIds ?? new List<string>();
            OperativeCorvetteSectorId = operativeCorvetteSectorId;
            AlertTokens = alertTokens ?? EmptyAlerts;
        }

        private static readonly IReadOnlyDictionary<string, SectorAlertCounts> EmptyAlerts =
            new Dictionary<string, SectorAlertCounts>();

        public static MapTokens None { get; } = new MapTokens();

        public MapTokens WithAllianceCruiser(string? sectorId) =>
            new MapTokens(sectorId, ReaverCutterSectorIds, OperativeCorvetteSectorId, AlertTokens);

        public MapTokens WithOperativeCorvette(string? sectorId) =>
            new MapTokens(AllianceCruiserSectorId, ReaverCutterSectorIds, sectorId, AlertTokens);

        public MapTokens WithReaverCutters(IReadOnlyList<string> sectorIds) =>
            new MapTokens(AllianceCruiserSectorId, sectorIds, OperativeCorvetteSectorId, AlertTokens);

        public MapTokens WithAlertTokens(IReadOnlyDictionary<string, SectorAlertCounts> alertTokens) =>
            new MapTokens(AllianceCruiserSectorId, ReaverCutterSectorIds, OperativeCorvetteSectorId, alertTokens);

        public int RemovableAlertCount(string sectorId, AlertTokenKind kind)
        {
            if (AlertTokens.TryGetValue(sectorId, out var counts))
                return counts.Of(kind);
            return 0;
        }

        public SectorAlertCounts AlertsAt(string sectorId) =>
            AlertTokens.TryGetValue(sectorId, out var counts) ? counts : default;

        /// <summary>
        /// Place one or more removable Alert Tokens in a Sector (stacks allowed).
        /// </summary>
        public MapTokens PlaceAlertToken(string sectorId, AlertTokenKind kind, int count = 1)
        {
            if (string.IsNullOrWhiteSpace(sectorId) || count <= 0)
                return this;
            var next = CopyAlerts();
            next.TryGetValue(sectorId, out var existing);
            next[sectorId] = existing.Add(kind, count);
            return WithAlertTokens(next);
        }

        /// <summary>
        /// Remove all removable Alert Tokens from a Sector. Permanent Reaver Space alerts remain.
        /// </summary>
        public MapTokens ClearRemovableAlerts(string sectorId)
        {
            if (!AlertTokens.ContainsKey(sectorId))
                return this;
            var next = CopyAlerts();
            next.Remove(sectorId);
            return WithAlertTokens(next);
        }

        private Dictionary<string, SectorAlertCounts> CopyAlerts()
        {
            var next = new Dictionary<string, SectorAlertCounts>(AlertTokens.Count, System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in AlertTokens)
                next[pair.Key] = pair.Value;
            return next;
        }

        /// <summary>
        /// Moves one Reaver Cutter token to <paramref name="toSectorId"/>.
        /// Only one Reaver ship may occupy a Sector (Blue Sun); rejects stacking Cutters.
        /// When <paramref name="leaveReaverAlertToken"/> is set and the Cutter changes Sector,
        /// places a Reaver Alert Token in the vacated Sector (Blue Sun / Director's Cut).
        /// </summary>
        public bool TryMoveReaverCutter(
            string toSectorId,
            out MapTokens updated,
            out string? error,
            int cutterIndex = 0,
            bool leaveReaverAlertToken = false)
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

            var fromSectorId = ReaverCutterSectorIds[cutterIndex];
            var next = new string[ReaverCutterSectorIds.Count];
            for (var i = 0; i < ReaverCutterSectorIds.Count; i++)
                next[i] = i == cutterIndex ? toSectorId : ReaverCutterSectorIds[i];
            updated = WithReaverCutters(next);
            if (leaveReaverAlertToken
                && !string.IsNullOrEmpty(fromSectorId)
                && !string.Equals(fromSectorId, toSectorId, System.StringComparison.OrdinalIgnoreCase))
            {
                updated = updated.PlaceAlertToken(fromSectorId, AlertTokenKind.Reaver);
            }
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
