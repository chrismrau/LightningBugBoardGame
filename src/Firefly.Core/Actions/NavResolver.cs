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

        public NavResolution(DrawnNav drawn, NavOption option, FlightOutcome outcome, bool stopped)
        {
            Drawn = drawn;
            Option = option;
            Outcome = outcome;
            Stopped = stopped;
        }
    }

    /// <summary>
    /// Resolves queued Full Burn Nav draws in order.
    /// Full Stop rewinds the ship to the sector of that draw and cancels remaining draws.
    /// Alliance Cruiser cards move the Cruiser onto the ship.
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

        public bool TryResolve(GameState game, int optionIndex, out NavResolution? resolution, out string? error)
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
            ApplyTokenMoves(game, drawn);

            var stopped = outcome == FlightOutcome.FullStop;
            if (stopped)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                game.PendingNavDraws.Clear();
            }

            game.Decks!.For(drawn.Region).ResolveIntoDiscard(drawn.Card);
            FaceUp = null;
            resolution = new NavResolution(drawn, option, outcome, stopped);
            return true;
        }

        /// <summary>
        /// Draws and resolves when the card has a single non-conditional option.
        /// Conditional / multi-option cards stay face up for the player to choose.
        /// </summary>
        public bool TryAutoResolve(GameState game, out NavResolution? resolution, out string? error)
        {
            resolution = null;
            var drawn = DrawNext(game);
            if (drawn.Card.Options.Count != 1 || drawn.Card.Options[0].Outcome == FlightOutcome.Conditional)
            {
                error = "Card requires an option choice.";
                return false;
            }
            return TryResolve(game, 0, out resolution, out error);
        }

        private static void ApplyTokenMoves(GameState game, DrawnNav drawn)
        {
            var type = drawn.Card.Type ?? "";
            if (type.Equals("Alliance Cruiser", System.StringComparison.OrdinalIgnoreCase))
            {
                game.Tokens = new MapTokens(drawn.SectorId, game.Tokens.ReaverCutterSectorIds);
                game.PendingEncounter = TokenKind.AllianceCruiser;
                game.PendingEncounterSectorId = drawn.SectorId;
            }
        }
    }
}
