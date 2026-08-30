using Firefly.Core.Cards;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class WantedCrewFate
    {
        public int Die { get; }
        public bool RemovedFromGame { get; }

        public WantedCrewFate(int die, bool removedFromGame)
        {
            Die = die;
            RemovedFromGame = removedFromGame;
        }
    }

    public sealed class CruiserBoardingResult
    {
        public int FineAssessed { get; }
        public int FinePaid { get; }
        public int ContrabandSeized { get; }
        public int FugitivesSeized { get; }
        public WantedCrewFate[] WantedRolls { get; }
        public int WantedRemoved { get; }

        public CruiserBoardingResult(
            int fineAssessed,
            int finePaid,
            int contrabandSeized,
            int fugitivesSeized,
            WantedCrewFate[] wantedRolls)
        {
            FineAssessed = fineAssessed;
            FinePaid = finePaid;
            ContrabandSeized = contrabandSeized;
            FugitivesSeized = fugitivesSeized;
            WantedRolls = wantedRolls;
            var removed = 0;
            foreach (var fate in wantedRolls)
            {
                if (fate.RemovedFromGame)
                    removed++;
            }
            WantedRemoved = removed;
        }
    }

    public static class CruiserBoarding
    {
        public const int FinePerWarrant = 1000;

        public static bool TryResolve(GameState game, IRng rng, out CruiserBoardingResult? result, out string? error)
        {
            result = null;
            error = null;
            if (game.PendingEncounter != TokenKind.AllianceCruiser)
            {
                error = "No Alliance Cruiser encounter is pending.";
                return false;
            }

            var player = game.CurrentPlayer;
            var sector = game.PendingEncounterSectorId ?? player.SectorId;
            game.Tokens = new MapTokens(sector, game.Tokens.ReaverCutterSectorIds);

            var fine = FinePerWarrant * player.Warrants;
            var paid = fine <= player.Cash ? fine : player.Cash;
            player.Cash -= paid;
            player.Warrants = 0;

            var contraband = player.Contraband;
            var fugitives = player.Fugitives;
            player.Contraband = 0;
            player.Fugitives = 0;

            var rolls = new WantedCrewFate[player.WantedCrew];
            var remainingWanted = 0;
            for (var i = 0; i < rolls.Length; i++)
            {
                var die = Dice.D6(rng);
                var removed = die == 1;
                rolls[i] = new WantedCrewFate(die, removed);
                if (!removed)
                    remainingWanted++;
            }
            player.WantedCrew = remainingWanted;

            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingNavDraws.Clear();

            result = new CruiserBoardingResult(fine, paid, contraband, fugitives, rolls);
            return true;
        }
    }
}
