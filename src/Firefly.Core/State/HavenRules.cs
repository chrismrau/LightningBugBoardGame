using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;

namespace Firefly.Core.State
{
    /// <summary>
    /// Blue Sun Choosing Havens + Any Port in a Storm Safe Harbor / Haven checks.
    /// Director's Cut / Blue Sun: Haven tokens on planetary sectors, not Supply or Contact
    /// planets, not another player's Haven. Story Card further restricts eligible planets.
    /// Missing Haven picks suspend via <see cref="PendingChoiceKinds.HavenSector"/>.
    /// </summary>
    public static class HavenRules
    {
        public static bool IsAnyHaven(GameState game, string sectorId)
        {
            if (string.IsNullOrWhiteSpace(sectorId))
                return false;
            foreach (var player in game.Players)
            {
                if (!string.IsNullOrWhiteSpace(player.HavenSectorId)
                    && string.Equals(player.HavenSectorId, sectorId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool IsOwnHaven(PlayerState player, string sectorId) =>
            !string.IsNullOrWhiteSpace(player.HavenSectorId)
            && string.Equals(player.HavenSectorId, sectorId, StringComparison.OrdinalIgnoreCase);

        public static bool SafeHarborActive(GameState game) =>
            game.Scenario != null && game.Scenario.SafeHarbor;

        /// <summary>
        /// Eligible Haven sectors for the active Story Card's chooseHavens setup
        /// (Blue Sun general exclusions + Story Card space / excludeLocations).
        /// </summary>
        public static IReadOnlyList<string> EligibleHavenSectors(GameState game)
        {
            var choose = game.Scenario?.ChooseHavens;
            var result = new List<string>();
            foreach (var sector in game.Map.Sectors.Values)
            {
                if (IsEligibleHavenSector(game, sector, choose, out _))
                    result.Add(sector.Id);
            }
            return result;
        }

        public static bool IsEligibleHavenSector(
            GameState game,
            string sectorId,
            out string? error) =>
            game.Map.TryGet(sectorId, out var sector)
                ? IsEligibleHavenSector(game, sector, game.Scenario?.ChooseHavens, out error)
                : Fail($"Unknown sector '{sectorId}'.", out error);

        private static bool IsEligibleHavenSector(
            GameState game,
            Sector sector,
            ChooseHavensSetup? choose,
            out string? error)
        {
            error = null;
            // Blue Sun / Director's Cut Choosing Havens: planetary sectors only.
            if (!sector.IsPlanetary && string.IsNullOrWhiteSpace(sector.Planet))
                return Fail("Haven must be placed in a Sector containing a planet.", out error);

            // Blue Sun: may not be placed at Supply or Contact planets.
            if (sector.HasSupplyDeck)
                return Fail("Haven may not be placed at a Supply planet.", out error);
            if (!string.IsNullOrWhiteSpace(sector.Contact))
                return Fail("Haven may not be placed at a Contact planet.", out error);

            if (choose != null)
            {
                if (!string.IsNullOrWhiteSpace(choose.Space)
                    && !string.Equals(sector.Region, choose.Space, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail($"Haven must be in {choose.Space} Space.", out error);
                }

                foreach (var exclude in choose.ExcludeLocations)
                {
                    if (MatchesLocation(sector, exclude))
                        return Fail($"Haven may not be placed at {exclude}.", out error);
                }
            }

            foreach (var player in game.Players)
            {
                if (!string.IsNullOrWhiteSpace(player.HavenSectorId)
                    && string.Equals(player.HavenSectorId, sector.Id, StringComparison.OrdinalIgnoreCase))
                    return Fail("Haven may not share another player's Haven Sector.", out error);
            }

            return true;
        }

        /// <summary>
        /// True when Safe Harbor is active, the intended Sector is a Haven, and no adjacent
        /// redirect has been supplied yet (PendingChoice required).
        /// </summary>
        public static bool NeedsSafeHarborRedirect(
            GameState game,
            string intendedSectorId,
            string? adjacentRedirectSectorId) =>
            SafeHarborActive(game)
            && IsAnyHaven(game, intendedSectorId)
            && string.IsNullOrWhiteSpace(adjacentRedirectSectorId);

        /// <summary>
        /// Legal adjacent non-Haven sectors for a Safe Harbor PTR redirect.
        /// </summary>
        public static IReadOnlyList<string> EligibleSafeHarborRedirects(
            GameState game,
            string havenSectorId)
        {
            var result = new List<string>();
            foreach (var neighbor in game.Map.Neighbors(havenSectorId))
            {
                if (!IsAnyHaven(game, neighbor))
                    result.Add(neighbor);
            }
            return result;
        }

        /// <summary>
        /// Place the Alliance Cruiser, redirecting off a Haven when Safe Harbor is active.
        /// ScenarioCards.json Safe Harbor: "If a Card would move it to a Haven, the player
        /// to the right places it in an adjacent Sector."
        /// Missing redirect → caller should suspend <see cref="PendingChoiceKinds.SectorDestination"/>.
        /// </summary>
        public static bool TryPlaceAllianceCruiser(
            GameState game,
            string intendedSectorId,
            string? adjacentRedirectSectorId,
            out string? error)
        {
            error = null;
            if (!SafeHarborActive(game) || !IsAnyHaven(game, intendedSectorId))
            {
                game.Tokens = game.Tokens.WithAllianceCruiser(intendedSectorId);
                return true;
            }

            if (string.IsNullOrWhiteSpace(adjacentRedirectSectorId))
            {
                error = "Safe Harbor: Cruiser would enter a Haven; choose an adjacent Sector.";
                return false;
            }

            if (!AreAdjacent(game, intendedSectorId, adjacentRedirectSectorId!))
            {
                error = "Safe Harbor redirect must be adjacent to the Haven Sector.";
                return false;
            }

            if (IsAnyHaven(game, adjacentRedirectSectorId!))
            {
                error = "Safe Harbor: Cruiser may not be placed on a Haven.";
                return false;
            }

            game.Tokens = game.Tokens.WithAllianceCruiser(adjacentRedirectSectorId);
            return true;
        }

        /// <summary>
        /// First player still missing a Haven after chooseHavens setup → suspend
        /// <see cref="PendingChoiceKinds.HavenSector"/>. Returns false when a choice is pending
        /// (error prompts) or when setup is complete (error null).
        /// </summary>
        public static bool TrySuspendNextHavenPick(GameState game, out string? error)
        {
            error = null;
            if (game.Scenario?.ChooseHavens is not { Required: true })
                return true;

            foreach (var player in game.Players)
            {
                if (!string.IsNullOrWhiteSpace(player.HavenSectorId))
                    continue;

                var eligible = EligibleHavenSectors(game);
                if (eligible.Count == 0)
                {
                    error = "No eligible Haven sectors remain.";
                    return false;
                }

                var pending = new PendingChoice(
                    player.Id,
                    PendingChoiceKinds.HavenSector,
                    contextId: player.Id,
                    options: eligible,
                    prompt: "Choose your Haven sector.");
                if (!game.TrySetPendingChoice(pending, out error))
                    return false;
                error = "Choose your Haven sector.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resume after <see cref="PendingChoiceKinds.HavenSector"/>: assign Haven, move ship
        /// there (Blue Sun), then suspend for the next seat still missing a Haven.
        /// </summary>
        public static bool TryResumeHavenSector(
            GameState game,
            string playerId,
            ChoiceSubmission submission,
            out string? error)
        {
            error = null;
            if (game.PendingChoice == null
                || !string.Equals(
                    game.PendingChoice.Kind,
                    PendingChoiceKinds.HavenSector,
                    StringComparison.Ordinal))
            {
                error = "No Haven sector choice is pending.";
                return false;
            }

            var sectorId = submission.SelectedOptionId ?? submission.Value;
            if (string.IsNullOrWhiteSpace(sectorId))
            {
                error = "A Haven sector id is required.";
                return false;
            }

            if (!IsEligibleHavenSector(game, sectorId!, out error))
                return false;

            if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                return false;

            var player = game.GetPlayer(playerId);
            player.HavenSectorId = sectorId!;
            player.SectorId = sectorId!;

            // Place Alliance Alert Tokens only after every Haven is chosen.
            if (!TrySuspendNextHavenPick(game, out error))
            {
                // Pending next seat, or hard failure (no eligible left).
                if (game.PendingChoice != null)
                {
                    error = "Choose your Haven sector.";
                    return true;
                }
                return false;
            }

            if (game.Scenario != null && game.Scenario.AllianceAlertTokensOnNonHavenAlliancePlanets)
                PlaceAllianceAlertTokensOnNonHavenAlliancePlanets(game);

            return true;
        }

        /// <summary>
        /// Blue Sun / Any Port: Alliance Alert Tokens on non-Haven Alliance planets.
        /// Distinct from the C&amp;P Alliance Alert deck.
        /// </summary>
        public static void PlaceAllianceAlertTokensOnNonHavenAlliancePlanets(GameState game)
        {
            game.UseAlertTokens = true;
            var tokens = game.Tokens;
            foreach (var sector in game.Map.Sectors.Values)
            {
                if (!string.Equals(sector.Region, "Alliance", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!sector.IsPlanetary && string.IsNullOrWhiteSpace(sector.Planet))
                    continue;
                if (IsAnyHaven(game, sector.Id))
                    continue;
                tokens = tokens.PlaceAlertToken(sector.Id, AlertTokenKind.Alliance, 1);
            }
            game.Tokens = tokens;
        }

        /// <summary>
        /// Choice-driven Cruiser moves (Patrol / Entanglements / Cry Baby) may not target a Haven.
        /// </summary>
        public static bool CanChooseCruiserDestination(GameState game, string sectorId, out string? error)
        {
            error = null;
            if (SafeHarborActive(game) && IsAnyHaven(game, sectorId))
            {
                error = "Safe Harbor: the Alliance Cruiser may not be moved to a Haven.";
                return false;
            }
            return true;
        }

        private static bool AreAdjacent(GameState game, string fromSectorId, string toSectorId)
        {
            foreach (var n in game.Map.Neighbors(fromSectorId))
            {
                if (string.Equals(n, toSectorId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool MatchesLocation(Sector sector, string location)
        {
            if (string.Equals(sector.Planet, location, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(sector.DisplayName, location, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(sector.Id, location, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var alias in sector.Aliases)
            {
                if (string.Equals(alias, location, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool Fail(string message, out string? error)
        {
            error = message;
            return false;
        }
    }
}
