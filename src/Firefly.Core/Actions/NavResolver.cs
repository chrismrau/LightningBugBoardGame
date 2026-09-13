using System;
using System.Text.RegularExpressions;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class NavResolution
    {
        public DrawnNav Drawn { get; }
        public NavOption Option { get; }
        public FlightOutcome Outcome { get; }
        public bool Stopped { get; }
        public SkillCheckResult? SkillCheck { get; }
        public ReaverContactResult? ReaverContact { get; }
        public CorvetteContactResult? CorvetteContact { get; }
        public bool ReaverCutterBlockedByCorvette { get; }

        public NavResolution(
            DrawnNav drawn,
            NavOption option,
            FlightOutcome outcome,
            bool stopped,
            SkillCheckResult? skillCheck = null,
            ReaverContactResult? reaverContact = null,
            CorvetteContactResult? corvetteContact = null,
            bool reaverCutterBlockedByCorvette = false)
        {
            Drawn = drawn;
            Option = option;
            Outcome = outcome;
            Stopped = stopped;
            SkillCheck = skillCheck;
            ReaverContact = reaverContact;
            CorvetteContact = corvetteContact;
            ReaverCutterBlockedByCorvette = reaverCutterBlockedByCorvette;
        }
    }

    /// <summary>
    /// Optional destinations when resolving Nav options that move tokens or Evade.
    /// </summary>
    public sealed class NavResolveChoice
    {
        public string? EvadeToSectorId { get; set; }
        public string? ReaverCutterToSectorId { get; set; }
        public int ReaverCutterIndex { get; set; }
        public string? OperativeCorvetteToSectorId { get; set; }
        /// <summary>
        /// When Corvette enters a Cutter's Sector, where that Cutter is driven off to (Reaver Starting Zone).
        /// </summary>
        public string? DriveOffReaverToSectorId { get; set; }
        public CorvetteContactChoice? CorvetteContact { get; set; }
        /// <summary>
        /// Destination for Cruiser Patrol / Alliance Entanglements (not the named Alliance Cruiser snap).
        /// Patrol: chosen by the player to the right. Entanglements: chosen by the drawer.
        /// </summary>
        public string? AllianceCruiserToSectorId { get; set; }
        /// <summary>
        /// Crew discarded when an option Requires Discarding 1 Crew (not the Leader).
        /// </summary>
        public string? DiscardCrewId { get; set; }
    }

    /// <summary>
    /// Resolves queued Full Burn Nav draws in order.
    /// Conditional options run a Fight/Tech/Talk test to pick Keep Flying vs Full Stop.
    /// Option Requires / Spend costs (Parts, Fuel, Cargo, crew keywords, Solid, Moral Crew, …)
    /// are enforced before applying flight outcomes; unmet gates fail closed.
    /// Named "Alliance Cruiser" Nav snaps the Cruiser onto the ship and queues Contact.
    /// Cruiser Patrol / Alliance Entanglements move the Cruiser per card text without that snap/Contact.
    /// Reaver Cutter cards move a Cutter; the named "Reaver Cutter" card applies Contact immediately.
    /// Operative's Corvette cards move the Corvette per card text; Contact if it ends on an Outlaw.
    /// Evade moves to an adjacent Sector and clears remaining Nav draws.
    /// </summary>
    public sealed class NavResolver
    {
        public DrawnNav? FaceUp { get; private set; }

        public bool HasPending(GameState game) => game.PendingNavDraws.Count > 0 || FaceUp != null;

        public DrawnNav DrawNext(GameState game)
        {
            if (FaceUp != null)
                return FaceUp;
            if (game.Decks == null)
                throw new System.InvalidOperationException("Nav decks have not been loaded.");
            if (game.PendingNavDraws.Count == 0)
                throw new System.InvalidOperationException("No pending Nav draws.");
            if (MustResolveAlertsBeforeNav(game))
            {
                throw new System.InvalidOperationException(
                    "Resolve Alert Tokens before drawing a Nav Card.");
            }

            var pending = game.PendingNavDraws[0];
            game.PendingNavDraws.RemoveAt(0);
            var card = game.Decks.For(pending.Region).Draw();
            FaceUp = new DrawnNav(card, pending.Region, pending.SectorId);
            return FaceUp;
        }

        /// <summary>
        /// Director's Cut / Blue Sun: resolve Alert Tokens before drawing a Nav Card for that Sector.
        /// </summary>
        public static bool MustResolveAlertsBeforeNav(GameState game)
        {
            if (game.PendingAlertSectors.Count == 0 || game.PendingNavDraws.Count == 0)
                return false;
            return string.Equals(
                game.PendingAlertSectors[0],
                game.PendingNavDraws[0].SectorId,
                System.StringComparison.OrdinalIgnoreCase);
        }

        public bool TryResolve(
            GameState game,
            int optionIndex,
            out NavResolution? resolution,
            out string? error,
            IRng? rng = null,
            NavResolveChoice? choice = null)
        {
            resolution = null;
            error = null;
            if (FaceUp == null)
            {
                error = "No Nav card is face up. Draw next first.";
                return false;
            }

            var drawnEarly = FaceUp;
            if (IsReaverCutterCardProtectedByCorvette(game, drawnEarly))
            {
                game.Decks!.For(drawnEarly.Region).ResolveIntoDiscard(drawnEarly.Card);
                FaceUp = null;
                resolution = new NavResolution(
                    drawnEarly,
                    drawnEarly.Card.Options.Count > 0
                        ? drawnEarly.Card.Options[0]
                        : new NavOption(null, "", FlightOutcome.KeepFlying),
                    FlightOutcome.KeepFlying,
                    stopped: false,
                    reaverCutterBlockedByCorvette: true);
                return true;
            }

            if (optionIndex < 0 || optionIndex >= FaceUp.Card.Options.Count)
            {
                error = "Invalid option index.";
                return false;
            }

            var drawn = FaceUp;
            var option = drawn.Card.Options[optionIndex];
            var outcome = option.Outcome;
            SkillCheckResult? check = null;
            if (outcome == FlightOutcome.Conditional &&
                SkillCheck.TryParse(option.Details, out var skillCheck))
            {
                check = skillCheck.Resolve(game.CurrentPlayer, rng ?? new SystemRng());
                outcome = SkillCheck.OutcomeFor(option.Details, check.Success);
            }

            var tokensBefore = game.Tokens;
            var pendingBefore = game.PendingEncounter;
            var pendingSectorBefore = game.PendingEncounterSectorId;
            var player = game.CurrentPlayer;
            var fuelBefore = player.Fuel;
            var partsBefore = player.Parts;
            var cargoBefore = player.Cargo;
            var cashBefore = player.Cash;

            if (!ApplyTokenMoves(
                game,
                drawn,
                option,
                choice,
                out var triggersReaverContact,
                out var triggersCorvetteContact,
                out error))
                return false;

            void RollbackTokens()
            {
                game.Tokens = tokensBefore;
                game.PendingEncounter = pendingBefore;
                game.PendingEncounterSectorId = pendingSectorBefore;
            }

            void RollbackResources()
            {
                player.Fuel = fuelBefore;
                player.Parts = partsBefore;
                player.Cargo = cargoBefore;
                player.Cash = cashBefore;
            }

            if (triggersReaverContact || outcome == FlightOutcome.Evade)
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.EvadeToSectorId))
                {
                    RollbackTokens();
                    error = triggersReaverContact
                        ? "Reaver Contact requires an Evade destination."
                        : "Evade requires an adjacent destination sector.";
                    return false;
                }
                var fromSector = game.CurrentPlayer.SectorId;
                game.CurrentPlayer.SectorId = drawn.SectorId;
                var canEvade = FlightEvade.CanMove(game, game.CurrentPlayer, choice.EvadeToSectorId!, out error);
                game.CurrentPlayer.SectorId = fromSector;
                if (!canEvade)
                {
                    RollbackTokens();
                    return false;
                }
            }

            if (triggersReaverContact && rng == null)
            {
                RollbackTokens();
                error = "Reaver Contact requires a Fight roll.";
                return false;
            }

            if (!TryApplyRequiresAndCosts(game, option.Details, ref outcome, choice, out error))
            {
                RollbackTokens();
                RollbackResources();
                return false;
            }

            if (!TryApplyAlertTokenEffects(game, drawn, option.Details, out error))
            {
                RollbackTokens();
                RollbackResources();
                return false;
            }

            ReaverContactResult? reaverContact = null;
            CorvetteContactResult? corvetteContact = null;
            var stopped = outcome == FlightOutcome.FullStop || outcome == FlightOutcome.Evade;

            if (triggersReaverContact)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!ReaverContact.TryApplyImmediate(game, rng!, choice!.EvadeToSectorId!, out reaverContact, out error))
                {
                    RollbackTokens();
                    RollbackResources();
                    return false;
                }
                game.PendingNavDraws.Clear();
                outcome = FlightOutcome.Evade;
                stopped = true;
            }
            else if (triggersCorvetteContact)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!CorvetteContact.TryApplyImmediate(
                    game,
                    out corvetteContact,
                    out error,
                    choice?.CorvetteContact))
                {
                    RollbackTokens();
                    RollbackResources();
                    return false;
                }
                game.PendingEncounter = null;
                game.PendingEncounterSectorId = null;
                game.PendingNavDraws.Clear();
                outcome = FlightOutcome.FullStop;
                stopped = true;
            }
            else if (outcome == FlightOutcome.FullStop)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                game.PendingNavDraws.Clear();
            }
            else if (outcome == FlightOutcome.Evade)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!FlightEvade.TryMove(game, game.CurrentPlayer, choice!.EvadeToSectorId!, out error))
                {
                    RollbackTokens();
                    RollbackResources();
                    return false;
                }
                game.PendingNavDraws.Clear();
            }

            game.Decks!.For(drawn.Region).ResolveIntoDiscard(drawn.Card);
            FaceUp = null;
            resolution = new NavResolution(
                drawn, option, outcome, stopped, check, reaverContact, corvetteContact);
            return true;
        }

        private static bool TryApplyAlertTokenEffects(
            GameState game,
            DrawnNav drawn,
            string details,
            out string? error)
        {
            error = null;
            if (!ContainsPlaceAlert(details))
                return true;

            if (Contains(details, "Place Reaver Token"))
            {
                game.Tokens = game.Tokens.PlaceAlertToken(drawn.SectorId, AlertTokenKind.Reaver);
            }

            if (Contains(details, "Place Alliance Alert Token in this Sector")
                || Contains(details, "Place an Alliance Alert Token in this Sector"))
            {
                game.Tokens = game.Tokens.PlaceAlertToken(drawn.SectorId, AlertTokenKind.Alliance);
            }

            if (Contains(details, "Place an Alliance Alert Token in all Sectors adjacent")
                || Contains(details, "Place an Alliance Alert Token in all adjacent")
                || Contains(details, "Place Alliance Alert Token in all adjacent"))
            {
                foreach (var neighbor in game.Map.Neighbors(drawn.SectorId))
                    game.Tokens = game.Tokens.PlaceAlertToken(neighbor, AlertTokenKind.Alliance);
            }

            if (Contains(details, "Place an Alliance Alert Token in every Sector occupied by an Outlaw Ship"))
            {
                foreach (var player in game.Players)
                {
                    if (AlertTokenRules.IsOutlawShip(player))
                        game.Tokens = game.Tokens.PlaceAlertToken(player.SectorId, AlertTokenKind.Alliance);
                }
            }

            return true;
        }

        private static bool ContainsPlaceAlert(string details) =>
            Contains(details, "Place Reaver Token")
            || Contains(details, "Place Alliance Alert Token")
            || Contains(details, "Place an Alliance Alert Token");

        public bool TryAutoResolve(
            GameState game,
            out NavResolution? resolution,
            out string? error,
            IRng? rng = null,
            NavResolveChoice? choice = null)
        {
            resolution = null;
            var drawn = DrawNext(game);
            // Corvette protects against Reaver Cutter Nav even when the card has multiple options.
            if (IsReaverCutterCardProtectedByCorvette(game, drawn))
                return TryResolve(game, 0, out resolution, out error, rng, choice);
            if (drawn.Card.Options.Count != 1)
            {
                error = "Card requires an option choice.";
                return false;
            }
            if (drawn.Card.Options[0].Outcome == FlightOutcome.Conditional && rng == null)
            {
                error = "Card requires an option choice.";
                return false;
            }
            return TryResolve(game, 0, out resolution, out error, rng, choice);
        }

        private static readonly Regex RequiresClause = new Regex(
            @"Requires\s*:?\s*([^.;]+?)(?=\s*(?:--|:|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpendAmount = new Regex(
            @"Spend\s+(\d+)\s+(Parts?|Fuel|Cargo)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadingFugitives = new Regex(
            @"Loading\s+(\d+)\s+Fugitives",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MoralCrewNeed = new Regex(
            @"(\d+)\s+or more Moral Crew",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool TryApplyRequiresAndCosts(
            GameState game,
            string details,
            ref FlightOutcome outcome,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var player = game.CurrentPlayer;
            var text = details ?? "";
            var skipGenericFuelSpend = false;
            var skipGenericPartsSpend = false;

            // Nav Hazard: "If you have Pilot, Keep Flying. Otherwise: Spend 1 Fuel, Keep Flying."
            if (IsPilotOrSpendFuel(text))
            {
                skipGenericFuelSpend = true;
                if (!MisbehaveResolver.HasTag(game, player, "Pilot"))
                {
                    if (player.Fuel < 1)
                    {
                        error = "Not enough fuel.";
                        return false;
                    }
                    player.Fuel -= 1;
                }
            }

            // "Spend 1 Part to Keep Flying. Otherwise, Full Stop."
            if (IsSpendPartToKeepFlyingOtherwise(text))
            {
                skipGenericPartsSpend = true;
                if (player.Parts >= 1)
                {
                    player.Parts -= 1;
                    outcome = FlightOutcome.KeepFlying;
                }
                else
                {
                    outcome = FlightOutcome.FullStop;
                }
            }

            if (!TryMeetRequires(game, player, text, choice, out var discardCrewId, out error))
                return false;

            if (!TryApplySpends(player, text, skipGenericFuelSpend, skipGenericPartsSpend, out error))
                return false;

            if (discardCrewId != null)
            {
                if (!player.Roster.TryDismiss(discardCrewId, out error))
                    return false;
            }

            if (Contains(text, "Take $500"))
                player.Cash += 500;

            // Resource-gated Conditional (no skill check) is resolved above; leftover Conditional → Keep Flying.
            if (outcome == FlightOutcome.Conditional)
                outcome = FlightOutcome.KeepFlying;

            return true;
        }

        private static bool IsPilotOrSpendFuel(string details) =>
            Contains(details, "If you have Pilot")
            && Contains(details, "Otherwise")
            && Contains(details, "Spend 1 Fuel");

        private static bool IsSpendPartToKeepFlyingOtherwise(string details) =>
            Contains(details, "Spend 1 Part to Keep Flying")
            && Contains(details, "Otherwise")
            && Contains(details, "Full Stop");

        private static bool TryMeetRequires(
            GameState game,
            PlayerState player,
            string details,
            NavResolveChoice? choice,
            out string? discardCrewId,
            out string? error)
        {
            error = null;
            discardCrewId = null;
            var match = RequiresClause.Match(details);
            if (!match.Success)
                return true;

            var need = match.Groups[1].Value.Trim();

            if (Contains(need, "Pilot and Mechanic"))
            {
                if (!MisbehaveResolver.HasTag(game, player, "Pilot")
                    || !MisbehaveResolver.HasTag(game, player, "Mechanic"))
                {
                    error = "Requires Pilot and Mechanic.";
                    return false;
                }
                return true;
            }

            var moral = MoralCrewNeed.Match(need);
            if (moral.Success)
            {
                var n = int.Parse(moral.Groups[1].Value);
                if (player.Roster.MoralCount < n)
                {
                    error = $"Requires {n} or more Moral Crew.";
                    return false;
                }
                return true;
            }

            if (Contains(need, "Discarding 1 Crew") || Contains(need, "Discarding one Crew"))
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.DiscardCrewId))
                {
                    error = "Requires Discarding 1 Crew.";
                    return false;
                }
                var member = player.Roster.Find(choice.DiscardCrewId!);
                if (member == null)
                {
                    error = "That crew is not on the ship.";
                    return false;
                }
                if (member.IsLeader)
                {
                    error = "Cannot dismiss your Leader.";
                    return false;
                }
                discardCrewId = member.Id;
                return true;
            }

            var fugitives = LoadingFugitives.Match(need);
            if (fugitives.Success)
            {
                var n = int.Parse(fugitives.Groups[1].Value);
                if (!HoldSpace.Fits(player, addFugitives: n))
                {
                    error = $"Requires Loading {n} Fugitives (not enough hold space).";
                    return false;
                }
                // Actual load is a salvage effect (separate slice). Gate only.
                return true;
            }

            if (TryParseSolidNeed(need, out var contactName))
            {
                if (!HasSolidWith(game, player, contactName))
                {
                    error = $"Requires Solid {contactName}.";
                    return false;
                }
                return true;
            }

            // Single keyword / profession / gear (Pilot, Mechanic, Soldier, Medic, Fake ID, …)
            var tag = need.Trim().TrimEnd(':').Trim();
            if (!MisbehaveResolver.HasTag(game, player, tag))
            {
                error = $"Requires {tag}.";
                return false;
            }
            return true;
        }

        private static bool TryParseSolidNeed(string need, out string contactName)
        {
            contactName = "";
            var with = Regex.Match(need, @"Solid\s+with\s+(.+)$", RegexOptions.IgnoreCase);
            if (with.Success)
            {
                contactName = with.Groups[1].Value.Trim();
                return contactName.Length > 0;
            }
            var harken = Regex.Match(
                need,
                @"Solid(?:\s+Rep)?\s+(?:with\s+)?(Harken)\s*(?:Rep)?",
                RegexOptions.IgnoreCase);
            if (harken.Success)
            {
                contactName = "Harken";
                return true;
            }
            return false;
        }

        private static bool TryApplySpends(
            PlayerState player,
            string details,
            bool skipFuel,
            bool skipParts,
            out string? error)
        {
            error = null;
            foreach (Match match in SpendAmount.Matches(details))
            {
                var amount = int.Parse(match.Groups[1].Value);
                var kind = match.Groups[2].Value;
                if (kind.StartsWith("Part", StringComparison.OrdinalIgnoreCase))
                {
                    if (skipParts)
                        continue;
                    if (player.Parts < amount)
                    {
                        error = amount == 1 ? "Not enough parts." : $"Need {amount} Parts.";
                        return false;
                    }
                    player.Parts -= amount;
                }
                else if (kind.Equals("Fuel", StringComparison.OrdinalIgnoreCase))
                {
                    if (skipFuel)
                        continue;
                    if (player.Fuel < amount)
                    {
                        error = "Not enough fuel.";
                        return false;
                    }
                    player.Fuel -= amount;
                }
                else if (kind.Equals("Cargo", StringComparison.OrdinalIgnoreCase))
                {
                    if (player.Cargo < amount)
                    {
                        error = amount == 1 ? "Not enough cargo." : $"Need {amount} Cargo.";
                        return false;
                    }
                    player.Cargo -= amount;
                }
            }
            return true;
        }

        private static bool HasSolidWith(GameState game, PlayerState player, string contactName)
        {
            if (ActiveAlertRules.CountsAsSolidWith(game, player, contactName))
                return true;
            if (game.Contacts != null && game.Contacts.TryFindByName(contactName, out var contact))
            {
                if (ActiveAlertRules.CountsAsSolidWith(game, player, contact.Id)
                    || ActiveAlertRules.CountsAsSolidWith(game, player, contact.Name))
                    return true;
            }

            if (!ActiveAlertRules.IsHarken(contactName))
                return false;
            foreach (var id in player.SolidWith)
            {
                if (ActiveAlertRules.IsHarken(id)
                    && ActiveAlertRules.CountsAsSolidWith(game, player, id))
                    return true;
            }
            return false;
        }

        private static bool ApplyTokenMoves(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersReaverContact,
            out bool triggersCorvetteContact,
            out string? error)
        {
            triggersReaverContact = false;
            triggersCorvetteContact = false;
            error = null;
            var type = drawn.Card.Type ?? "";
            if (type.Equals("Alliance Cruiser", System.StringComparison.OrdinalIgnoreCase)
                || IsNamedAllianceCruiserCard(drawn.Card))
            {
                return ApplyAllianceCruiserTypedCard(game, drawn, option, choice, out error);
            }

            if (type.Equals("Operative's Corvette", System.StringComparison.OrdinalIgnoreCase))
                return ApplyOperativeCorvetteCard(
                    game, drawn, option, choice, out triggersCorvetteContact, out error);

            if (!type.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
                return true;

            return ApplyReaverCutterCard(game, drawn, option, choice, out triggersReaverContact, out error);
        }

        /// <summary>
        /// GF9 p.8 / Director's Cut p.17: named Alliance Cruiser snaps to the drawer;
        /// Alliance Entanglements — drawer moves the Cruiser; Cruiser Patrol — player to the right
        /// moves it 1 Sector within Alliance Space. Only the named card queues Contact / Full Stop.
        /// </summary>
        private static bool ApplyAllianceCruiserTypedCard(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            if (IsNamedAllianceCruiserCard(drawn.Card))
            {
                game.Tokens = game.Tokens.WithAllianceCruiser(drawn.SectorId);
                game.PendingEncounter = TokenKind.AllianceCruiser;
                game.PendingEncounterSectorId = drawn.SectorId;
                game.BountyDeck?.CycleWantedList(game.RemovedFromPlay);
                game.AllianceAlertDeck?.DrawAndActivate();
                return true;
            }

            var name = drawn.Card.Name ?? "";
            var id = drawn.Card.Id ?? "";
            if (IsCruiserPatrolCard(id, name))
                return ApplyCruiserPatrol(game, choice, out error);
            if (IsAllianceEntanglementsCard(id, name))
                return ApplyAllianceEntanglements(game, option, choice, out error);

            error = $"Unknown Alliance Cruiser-type Nav card '{id}'.";
            return false;
        }

        private static bool IsNamedAllianceCruiserCard(NavCard card)
        {
            var id = card.Id ?? "";
            var name = card.Name ?? "";
            return id.Equals("nav_alliance-cruiser", System.StringComparison.OrdinalIgnoreCase)
                || name.Equals("Alliance Cruiser", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCruiserPatrolCard(string id, string name) =>
            id.Equals("nav_cruiser-patrol", System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cruiser Patrol", System.StringComparison.OrdinalIgnoreCase);

        private static bool IsAllianceEntanglementsCard(string id, string name) =>
            id.Equals("nav_alliance-entanglements", System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("Alliance Entanglements", System.StringComparison.OrdinalIgnoreCase);

        private static bool ApplyCruiserPatrol(
            GameState game,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var destination = choice?.AllianceCruiserToSectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Cruiser Patrol requires a destination sector (player to the right).";
                return false;
            }

            var from = game.Tokens.AllianceCruiserSectorId;
            if (string.IsNullOrEmpty(from))
            {
                error = "Alliance Cruiser is not on the board.";
                return false;
            }

            if (!IsAllianceSpace(game, destination!))
            {
                error = "Cruiser Patrol destination must be within Alliance Space.";
                return false;
            }

            if (!AreAdjacent(game, from!, destination!))
            {
                error = "Cruiser Patrol must move the Cruiser 1 Sector.";
                return false;
            }

            game.Tokens = game.Tokens.WithAllianceCruiser(destination);
            return true;
        }

        private static bool ApplyAllianceEntanglements(
            GameState game,
            NavOption option,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var destination = choice?.AllianceCruiserToSectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Alliance Entanglements requires a destination sector.";
                return false;
            }

            if (!IsAllianceSpace(game, destination!))
            {
                error = "Alliance Entanglements destination must be an Alliance Sector.";
                return false;
            }

            var details = option.Details ?? "";
            if (RequiresUnoccupiedByFirefly(details) && HasFireflyInSector(game, destination!))
            {
                error = "Alliance Entanglements destination must not be occupied by a Firefly.";
                return false;
            }

            if (RequiresOutlawShipInSector(details) && !HasOutlawShipInSector(game, destination!))
            {
                error = "Alliance Entanglements destination must have an Outlaw Ship.";
                return false;
            }

            game.Tokens = game.Tokens.WithAllianceCruiser(destination);
            return true;
        }

        private static bool IsAllianceSpace(GameState game, string sectorId)
        {
            if (!game.Map.TryGet(sectorId, out var sector))
                return false;
            return sector.NavRegion == NavRegion.Alliance;
        }

        private static bool AreAdjacent(GameState game, string fromSectorId, string toSectorId)
        {
            foreach (var n in game.Map.Neighbors(fromSectorId))
            {
                if (string.Equals(n, toSectorId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool RequiresUnoccupiedByFirefly(string details) =>
            Contains(details, "not occupied by a Firefly");

        private static bool RequiresOutlawShipInSector(string details) =>
            Contains(details, "with an Outlaw Ship");

        private static bool HasFireflyInSector(GameState game, string sectorId)
        {
            foreach (var player in game.Players)
            {
                if (string.Equals(player.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasOutlawShipInSector(GameState game, string sectorId)
        {
            foreach (var player in game.Players)
            {
                if (string.Equals(player.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase)
                    && AlertTokenRules.IsOutlawShip(player))
                    return true;
            }
            return false;
        }

        private static bool ApplyOperativeCorvetteCard(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersCorvetteContact,
            out string? error)
        {
            triggersCorvetteContact = false;
            error = null;
            var destination = choice?.OperativeCorvetteToSectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Operative's Corvette move requires a destination sector.";
                return false;
            }

            if (!TryValidateCorvetteDestination(game, drawn, option.Details ?? "", destination!, out error))
                return false;

            if (!game.Tokens.TryMoveOperativeCorvette(
                destination!,
                out var moved,
                out error,
                choice?.DriveOffReaverToSectorId))
                return false;

            game.Tokens = moved;

            // Contact when Corvette ends its move in an Outlaw Ship's Sector (current player).
            if (string.Equals(game.CurrentPlayer.SectorId, destination, System.StringComparison.OrdinalIgnoreCase)
                && AlertTokenRules.IsOutlawShip(game.CurrentPlayer))
            {
                game.PendingEncounter = TokenKind.OperativeCorvette;
                game.PendingEncounterSectorId = destination;
                triggersCorvetteContact = true;
            }
            return true;
        }

        private static bool TryValidateCorvetteDestination(
            GameState game,
            DrawnNav drawn,
            string details,
            string destination,
            out string? error)
        {
            error = null;
            if (!game.Map.TryGet(destination, out var sector))
            {
                error = $"Unknown sector '{destination}'.";
                return false;
            }

            // Corvette may enter Alliance, Border, or Rim Space (any map sector) — already true for our map.
            if (RequiresUnoccupied(details) && !IsUnoccupiedSector(game, destination))
            {
                error = "Operative's Corvette destination must be unoccupied.";
                return false;
            }

            if (RequiresPlanetary(details) && !sector.IsPlanetary && string.IsNullOrWhiteSpace(sector.Planet))
            {
                error = "Operative's Corvette destination must be a Planetary Sector.";
                return false;
            }

            if (RequiresAdjacentToDraw(details))
            {
                var adjacent = false;
                foreach (var n in game.Map.Neighbors(drawn.SectorId))
                {
                    if (string.Equals(n, destination, System.StringComparison.OrdinalIgnoreCase))
                    {
                        adjacent = true;
                        break;
                    }
                }
                if (!adjacent)
                {
                    error = "Operative's Corvette destination must be adjacent to your current location.";
                    return false;
                }
            }

            if (RequiresOneOrTwoSectors(details))
            {
                var from = game.Tokens.OperativeCorvetteSectorId;
                if (string.IsNullOrEmpty(from))
                {
                    error = "Operative's Corvette is not on the board.";
                    return false;
                }
                var path = new Pathfinder(game.Map).ShortestPath(from!, destination);
                if (path == null)
                {
                    error = "No path for Operative's Corvette move.";
                    return false;
                }
                var distance = path.Count - 1;
                if (distance < 1 || distance > 2)
                {
                    error = "Operative's Corvette must move 1 or 2 Sectors.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsUnoccupiedSector(GameState game, string sectorId)
        {
            if (game.Tokens.EncounterAt(sectorId).HasValue)
                return false;
            foreach (var player in game.Players)
            {
                if (string.Equals(player.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static bool RequiresUnoccupied(string details) =>
            Contains(details, "unoccupied");

        private static bool RequiresPlanetary(string details) =>
            Contains(details, "Planetary Sector");

        private static bool RequiresAdjacentToDraw(string details) =>
            Contains(details, "adjacent to your current location");

        private static bool RequiresOneOrTwoSectors(string details) =>
            Contains(details, "1 or 2 Sectors") || Contains(details, "1 or 2 sectors");

        /// <summary>
        /// Kalidasa / Director's Cut: Reaver Cutter Nav while moving into the Corvette's Sector
        /// is cancelled; reshuffle normally.
        /// </summary>
        private static bool IsReaverCutterCardProtectedByCorvette(GameState game, DrawnNav drawn)
        {
            var type = drawn.Card.Type ?? "";
            var name = drawn.Card.Name ?? "";
            if (!type.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
                return false;
            var corvette = game.Tokens.OperativeCorvetteSectorId;
            return corvette != null
                && string.Equals(corvette, drawn.SectorId, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool ApplyReaverCutterCard(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersReaverContact,
            out string? error)
        {
            triggersReaverContact = false;
            error = null;
            var details = option.Details ?? "";
            var cardName = drawn.Card.Name ?? "";

            // Named "Reaver Cutter" Nav Card: Cutter moves to the draw sector; Contact if that option applies.
            if (cardName.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!game.Tokens.TryMoveReaverCutter(
                    drawn.SectorId,
                    out var moved,
                    out error,
                    choice?.ReaverCutterIndex ?? 0,
                    leaveReaverAlertToken: game.UseAlertTokens))
                {
                    // Already occupied by a Reaver: do not move another; still resolve Contact if applicable.
                    // Blue Sun Alert Token note — also matches "already occupied by a Reaver ship".
                    if (game.Tokens.EncounterAt(drawn.SectorId) != TokenKind.ReaverCutter)
                        return false;
                    error = null;
                }
                else
                {
                    game.Tokens = moved;
                }

                if (IsImmediateReaverContactOption(details))
                    triggersReaverContact = true;
                return true;
            }

            // Other Reaver Cutter-type cards (Hunt / Dead Ahead / Bait / Orbit): move without Contact.
            // "If the Cutter moves into your Sector as a result of 'Reavers on the Hunt', do not resolve Reaver Contact yet."
            var destination = choice?.ReaverCutterToSectorId;
            if (string.IsNullOrWhiteSpace(destination) && MovesCutterToDrawSector(details))
                destination = drawn.SectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Reaver Cutter move requires a destination sector.";
                return false;
            }

            if (!game.Tokens.TryMoveReaverCutter(
                destination!,
                out var updated,
                out error,
                choice?.ReaverCutterIndex ?? 0,
                leaveReaverAlertToken: game.UseAlertTokens))
                return false;
            game.Tokens = updated;
            return true;
        }

        private static bool IsImmediateReaverContactOption(string details) =>
            Contains(details, "Kill all Passengers") || Contains(details, "Fight 8");

        private static bool MovesCutterToDrawSector(string details) =>
            Contains(details, "to your current location")
            || Contains(details, "to your Sector")
            || Contains(details, "to your sector");

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
