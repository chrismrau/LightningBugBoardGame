using Firefly.Core.Cards;
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

        public NavResolution(
            DrawnNav drawn,
            NavOption option,
            FlightOutcome outcome,
            bool stopped,
            SkillCheckResult? skillCheck = null,
            ReaverContactResult? reaverContact = null)
        {
            Drawn = drawn;
            Option = option;
            Outcome = outcome;
            Stopped = stopped;
            SkillCheck = skillCheck;
            ReaverContact = reaverContact;
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
    }

    /// <summary>
    /// Resolves queued Full Burn Nav draws in order.
    /// Conditional options run a Fight/Tech/Talk test to pick Keep Flying vs Full Stop.
    /// Alliance Cruiser cards move the Cruiser onto the ship.
    /// Reaver Cutter cards move a Cutter; the named "Reaver Cutter" card applies Contact immediately.
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

            var pending = game.PendingNavDraws[0];
            game.PendingNavDraws.RemoveAt(0);
            var card = game.Decks.For(pending.Region).Draw();
            FaceUp = new DrawnNav(card, pending.Region, pending.SectorId);
            return FaceUp;
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

            if (!ApplyTokenMoves(game, drawn, option, choice, out var triggersReaverContact, out error))
                return false;

            void RollbackTokens()
            {
                game.Tokens = tokensBefore;
                game.PendingEncounter = pendingBefore;
                game.PendingEncounterSectorId = pendingSectorBefore;
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

            if (!TryApplyRequiresAndCosts(game, option.Details, out error))
            {
                RollbackTokens();
                return false;
            }

            ReaverContactResult? reaverContact = null;
            var stopped = outcome == FlightOutcome.FullStop || outcome == FlightOutcome.Evade;

            if (triggersReaverContact)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!ReaverContact.TryApplyImmediate(game, rng!, choice!.EvadeToSectorId!, out reaverContact, out error))
                {
                    RollbackTokens();
                    return false;
                }
                game.PendingNavDraws.Clear();
                outcome = FlightOutcome.Evade;
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
                    return false;
                }
                game.PendingNavDraws.Clear();
            }

            game.Decks!.For(drawn.Region).ResolveIntoDiscard(drawn.Card);
            FaceUp = null;
            resolution = new NavResolution(drawn, option, outcome, stopped, check, reaverContact);
            return true;
        }

        public bool TryAutoResolve(
            GameState game,
            out NavResolution? resolution,
            out string? error,
            IRng? rng = null,
            NavResolveChoice? choice = null)
        {
            resolution = null;
            var drawn = DrawNext(game);
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

        private static bool TryApplyRequiresAndCosts(GameState game, string details, out string? error)
        {
            error = null;
            if (Contains(details, "Requires Pilot and Mechanic"))
            {
                if (!MisbehaveResolver.HasTag(game, game.CurrentPlayer, "Pilot")
                    || !MisbehaveResolver.HasTag(game, game.CurrentPlayer, "Mechanic"))
                {
                    error = "Requires Pilot and Mechanic.";
                    return false;
                }
            }

            if (Contains(details, "Spend 1 Fuel"))
            {
                if (game.CurrentPlayer.Fuel < 1)
                {
                    error = "Not enough fuel.";
                    return false;
                }
                game.CurrentPlayer.Fuel -= 1;
            }

            return true;
        }

        private static bool ApplyTokenMoves(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersReaverContact,
            out string? error)
        {
            triggersReaverContact = false;
            error = null;
            var type = drawn.Card.Type ?? "";
            if (type.Equals("Alliance Cruiser", System.StringComparison.OrdinalIgnoreCase))
            {
                game.Tokens = game.Tokens.WithAllianceCruiser(drawn.SectorId);
                game.PendingEncounter = TokenKind.AllianceCruiser;
                game.PendingEncounterSectorId = drawn.SectorId;
                game.BountyDeck?.CycleWantedList(game.RemovedFromPlay);
                game.AllianceAlertDeck?.DrawAndActivate();
                return true;
            }

            if (!type.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
                return true;

            return ApplyReaverCutterCard(game, drawn, option, choice, out triggersReaverContact, out error);
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
                if (!game.Tokens.TryMoveReaverCutter(drawn.SectorId, out var moved, out error, choice?.ReaverCutterIndex ?? 0))
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

            if (!game.Tokens.TryMoveReaverCutter(destination!, out var updated, out error, choice?.ReaverCutterIndex ?? 0))
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
