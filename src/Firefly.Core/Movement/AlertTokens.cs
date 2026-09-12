using System;
using System.Collections.Generic;

namespace Firefly.Core.Movement
{
    /// <summary>
    /// Blue Sun double-sided physical Alert Tokens (distinct from the C&amp;P Alliance Alert deck).
    /// </summary>
    public enum AlertTokenKind
    {
        Reaver,
        Alliance
    }

    /// <summary>
    /// Removable Alert Token counts in one Sector. Permanent Reaver Space alerts are separate.
    /// </summary>
    public readonly struct SectorAlertCounts
    {
        public int Reaver { get; }
        public int Alliance { get; }

        public SectorAlertCounts(int reaver, int alliance)
        {
            Reaver = reaver < 0 ? 0 : reaver;
            Alliance = alliance < 0 ? 0 : alliance;
        }

        public int Of(AlertTokenKind kind) =>
            kind == AlertTokenKind.Reaver ? Reaver : Alliance;

        public bool IsEmpty => Reaver == 0 && Alliance == 0;

        public SectorAlertCounts Add(AlertTokenKind kind, int amount = 1)
        {
            if (amount == 0)
                return this;
            return kind == AlertTokenKind.Reaver
                ? new SectorAlertCounts(Reaver + amount, Alliance)
                : new SectorAlertCounts(Reaver, Alliance + amount);
        }
    }

    /// <summary>
    /// Blue Sun / Director's Cut Alert Token helpers.
    /// Permanent Reaver Space = the 3 Burnham ring-2 sectors surrounding Miranda.
    /// </summary>
    public static class AlertTokenRules
    {
        /// <summary>
        /// Blue Sun: "The 3 Sectors surrounding Burnham are designated as Reaver Space."
        /// Each is marked with a permanent Reaver Alert that is never removed.
        /// </summary>
        public static readonly string[] PermanentReaverSpaceSectorIds =
        {
            "rim-burnham-r2-01",
            "rim-burnham-r2-02",
            "rim-burnham-r2-03"
        };

        public static bool HasPermanentReaverAlert(string sectorId)
        {
            foreach (var id in PermanentReaverSpaceSectorIds)
            {
                if (string.Equals(id, sectorId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Director's Cut / Kalidasa: Reaver Starting Zones = the three Burnham ring-2 sectors.
        /// The Operative's Corvette may not end its move there.
        /// </summary>
        public static bool IsReaverStartingZone(string sectorId) =>
            HasPermanentReaverAlert(sectorId);

        /// <summary>
        /// FAQ 4.1: Legal Ship = no Contraband, no Fugitives, no Wanted crew, no Warrants.
        /// </summary>
        public static bool IsOutlawShip(State.PlayerState player) =>
            player.Warrants > 0
            || player.Contraband > 0
            || player.Fugitives > 0
            || player.Roster.WantedCount > 0;

        public static int EffectiveCount(
            MapTokens tokens,
            string sectorId,
            AlertTokenKind kind,
            bool includePermanentReaverSpace)
        {
            var n = tokens.RemovableAlertCount(sectorId, kind);
            if (kind == AlertTokenKind.Reaver
                && includePermanentReaverSpace
                && HasPermanentReaverAlert(sectorId))
            {
                n += 1;
            }
            return n;
        }

        public static bool SectorHasAlerts(MapTokens tokens, string sectorId, bool useAlertTokens)
        {
            if (!useAlertTokens)
                return false;
            return EffectiveCount(tokens, sectorId, AlertTokenKind.Reaver, includePermanentReaverSpace: true) > 0
                || EffectiveCount(tokens, sectorId, AlertTokenKind.Alliance, includePermanentReaverSpace: false) > 0;
        }
    }
}
