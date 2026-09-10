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

    public sealed class CruiserBoardingChoice
    {
        public bool UseIncarcerationOrder { get; set; }
        public string? RemoveWantedCrewId { get; set; }
    }

    /// <summary>
    /// Alliance Cruiser contact: fines, warrants cleared, contraband/fugitives seized,
    /// wanted crew roll 1 = removed from game, 2-6 dodge. Always Full Stop.
    /// Background Checks raises the dodge target to (warrants + 1).
    /// Incarceration Order may drop one Wanted crew from play in place of one warrant fine.
    /// </summary>
    public static class CruiserBoarding
    {
        public const int FinePerWarrant = 1000;

        public static bool TryResolve(GameState game, IRng rng, out CruiserBoardingResult? result, out string? error) =>
            TryResolve(game, rng, out result, out error, null);

        public static bool TryResolve(
            GameState game,
            IRng rng,
            out CruiserBoardingResult? result,
            out string? error,
            CruiserBoardingChoice? choice)
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

            var warrantsAtEncounter = player.Warrants;
            var waivedWarrants = 0;
            if (choice != null && choice.UseIncarcerationOrder)
            {
                if (!ActiveAlertRules.CanUseIncarcerationOrder(game, player))
                {
                    error = "Incarceration Order is not available.";
                    return false;
                }
                var removeId = choice.RemoveWantedCrewId;
                CrewMember? target = null;
                foreach (var member in player.Roster.WantedMembers())
                {
                    if (string.IsNullOrWhiteSpace(removeId)
                        || member.Id.Equals(removeId, System.StringComparison.OrdinalIgnoreCase)
                        || member.Name.Equals(removeId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        target = member;
                        break;
                    }
                }
                if (target == null)
                {
                    error = "Incarceration Order requires a Wanted crew member to remove from play.";
                    return false;
                }
                player.Roster.Remove(target.Id);
                game.RemovedFromPlay.Add(target.Id);
                waivedWarrants = 1;
            }

            game.Tokens = new MapTokens(sector, game.Tokens.ReaverCutterSectorIds);

            var fine = FinePerWarrant * System.Math.Max(0, warrantsAtEncounter - waivedWarrants);
            var paid = fine <= player.Cash ? fine : player.Cash;
            player.Cash -= paid;
            player.Warrants = 0;

            var contraband = player.Contraband;
            var fugitives = player.Fugitives;
            player.Contraband = 0;
            player.Fugitives = 0;

            var wanted = player.Roster.WantedMembers();
            var rolls = new WantedCrewFate[wanted.Count];
            for (var i = 0; i < wanted.Count; i++)
            {
                var die = Dice.D6(rng);
                var removed = ActiveAlertRules.WantedCrewCaptured(game, die, warrantsAtEncounter);
                rolls[i] = new WantedCrewFate(die, removed);
                if (removed)
                    player.Roster.Remove(wanted[i].Id);
            }

            game.PendingEncounter = null;
            game.PendingEncounterSectorId = null;
            game.PendingNavDraws.Clear();

            result = new CruiserBoardingResult(fine, paid, contraband, fugitives, rolls);
            return true;
        }
    }
}
