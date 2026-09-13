using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class PiracyTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string ContractJumper = "job_amnon-duul_contract-jumper";
        private const string Pilfer = "job_badger_pilfer-purloin-and-plunder";

        private static (GameState Game, PlayerState Mal, PlayerState Zoe) TwoShips(bool pbh = true)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone, cash: 0);
            var zoe = new PlayerState("p2", "Zoe", Persephone);
            var game = new GameState(map, new[] { mal, zoe })
            {
                Jobs = JobCatalog.LoadDefault(),
                Contacts = ContactCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                Leaders = LeaderCatalog.LoadDefault(),
                UsePiratesBountyHunters = pbh
            };
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1), BountyCatalog.LoadDefault());
            return (game, mal, zoe);
        }

        private static PiracyChoice Choice(
            string rivalId,
            Skill board = Skill.Tech,
            Skill attack = Skill.Fight,
            Skill defend = Skill.Talk) =>
            new PiracyChoice
            {
                RivalId = rivalId,
                BoardSkill = board,
                AttackSkill = attack,
                DefendSkill = defend
            };

        [Fact]
        public void Piracy_terms_parse_contract_jumper_and_pilfer()
        {
            var jobs = JobCatalog.LoadDefault();
            var jumper = PiracyTerms.FromJob(jobs.Get(ContractJumper));
            Assert.Equal(3, jumper.MaxStealJobs);
            Assert.True(jumper.PrintedWarrantOnLoss);
            Assert.Equal(800, jumper.PayPerJob);
            Assert.Equal(6, jumper.BoardingTarget);

            var pilfer = PiracyTerms.FromJob(jobs.Get(Pilfer));
            Assert.Equal(6, pilfer.MaxStealGoods);
            Assert.Equal(1, pilfer.KillOnLoss);
            Assert.True(pilfer.PrintedWarrantOnLoss);
            Assert.Equal(200, pilfer.PayPerGood);
        }

        [Fact]
        public void Setup_pbh_flag_enables_piracy_and_bounty_deck()
        {
            var core = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });
            Assert.False(core.UsePiratesBountyHunters);
            Assert.Null(core.BountyDeck);

            var pbh = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UsePiratesBountyHunters = true,
                    Rng = new SystemRng(1)
                });
            Assert.True(pbh.UsePiratesBountyHunters);
            Assert.NotNull(pbh.BountyDeck);
        }

        [Fact]
        public void WorkAction_rejects_any_rival_in_favor_of_PiracyAction()
        {
            var (game, mal, _) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            Assert.False(new WorkAction().TryWork(game, "p1", ContractJumper, out _, out var error));
            Assert.Contains("PiracyAction", error);
        }

        [Fact]
        public void Piracy_requires_expansion_flag()
        {
            var (game, mal, zoe) = TwoShips(pbh: false);
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            Assert.False(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, Choice("p2"), ScriptedRng.FromDieFaces(6, 6, 1),
                out _, out var error));
            Assert.Contains("Pirates & Bounty Hunters", error);
        }

        [Fact]
        public void Boarding_fail_leaves_job_active_not_in_hand()
        {
            // PBH p.5: "If the Boarding Test is failed, the Piracy Job Card remains
            // in your Active Job area but your attempt is over."
            var (game, mal, _) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 1;
            var piracy = new PiracyAction();
            Assert.True(piracy.TryPirate(
                game, "p1", ContractJumper, Choice("p2"), ScriptedRng.FromDieFaces(1),
                out var result, out var error), error);
            Assert.True(result!.BoardingFailed);
            Assert.False(result.Success);
            Assert.DoesNotContain(ContractJumper, mal.JobHand);
            Assert.NotNull(mal.FindActive(ContractJumper));
            Assert.Equal(TurnAction.Work, game.LastAction);

            // Retry later from the Active slot (FAQ / PBH: stay Active until complete/discard).
            game.EndTurn();
            game.EndTurn();
            mal.TechBonus = 5;
            mal.FightBonus = 5;
            Assert.True(piracy.TryPirate(
                game, "p1", ContractJumper, Choice("p2"),
                ScriptedRng.FromDieFaces(6, 6, 1),
                out var retry, out error), error);
            Assert.False(retry!.BoardingFailed);
        }

        [Fact]
        public void Boarding_rejects_fight_skill()
        {
            var (game, mal, _) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.FightBonus = 5;
            var choice = Choice("p2", board: Skill.Fight);
            Assert.False(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, choice, ScriptedRng.FromDieFaces(6),
                out _, out var error));
            Assert.Contains("Tech or Negotiate", error);
        }

        [Fact]
        public void Same_sector_required()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            zoe.SectorId = "alliance-white-sun-r1-02";
            Assert.False(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, Choice("p2"), ScriptedRng.FromDieFaces(6),
                out _, out var error));
            Assert.Contains("sector", error);
        }

        [Fact]
        public void Contract_jumper_steals_inactive_jobs_and_pays_per_job()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            mal.FightBonus = 5;
            zoe.JobHand.Add("job_badger_badgers-11-casino-caper");
            zoe.JobHand.Add("job_amnon-duul_feeding-alliance-fat-cats");
            zoe.JobHand.Add("job_patience_hard-rustlin");
            // Boarding 6, attack 6, defend 1 → win showdown.
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, Choice("p2"),
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal(3, result.JobsStolen);
            Assert.Equal(2400, result.Pay);
            Assert.Equal(2400, mal.Cash);
            Assert.Empty(zoe.JobHand);
            Assert.DoesNotContain(ContractJumper, mal.JobHand);
            Assert.Null(mal.FindActive(ContractJumper));
            // Hand has 3 stolen jobs (at limit) — no discard required.
            Assert.Equal(3, mal.JobHand.Count);
            Assert.True(mal.IsSolidWith("contact_amnon-duul"));
        }

        [Fact]
        public void Contract_jumper_requires_discard_when_hand_exceeds_limit()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.JobHand.Add("job_badger_send-them-to-the-ruttin-mines");
            mal.TechBonus = 5;
            mal.FightBonus = 5;
            zoe.JobHand.Add("job_badger_badgers-11-casino-caper");
            zoe.JobHand.Add("job_amnon-duul_feeding-alliance-fat-cats");
            zoe.JobHand.Add("job_patience_hard-rustlin");
            var choice = Choice("p2");
            // After removing jumper from hand: mines + 3 stolen = 4 → must discard 1.
            Assert.False(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, choice,
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out _, out var error));
            Assert.Contains("discard down to", error);

            choice.DiscardJobHandIds = new[] { "job_badger_send-them-to-the-ruttin-mines" };
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, choice,
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out var result, out error), error);
            Assert.True(result!.Success);
            Assert.Equal(3, result.JobsStolen);
            Assert.Equal(3, mal.JobHand.Count);
            Assert.DoesNotContain(ContractJumper, mal.JobHand);
        }

        [Fact]
        public void Pilfer_steals_unprotected_goods_and_pays_per_good()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(Pilfer);
            mal.TechBonus = 5;
            mal.FightBonus = 5;
            mal.Fuel = 0;
            mal.Parts = 0;
            zoe.StashHold = 0;
            zoe.Fuel = 1;
            zoe.Parts = 0;
            zoe.Contraband = 3;
            zoe.Cargo = 2;
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", Pilfer, Choice("p2"),
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal(6, result.GoodsStolen);
            Assert.Equal(1200, result.Pay);
            Assert.Equal(0, zoe.Contraband + zoe.Cargo + zoe.Fuel + zoe.Parts);
            Assert.Equal(3, mal.Contraband);
            Assert.Equal(2, mal.Cargo);
            Assert.Equal(1, mal.Fuel);
        }

        [Fact]
        public void Stashed_goods_cannot_be_stolen()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(Pilfer);
            mal.TechBonus = 5;
            mal.FightBonus = 5;
            zoe.StashHold = 4;
            zoe.Fuel = 0;
            zoe.Parts = 0;
            zoe.Cargo = 0;
            zoe.Contraband = 4; // all fit in stash
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", Pilfer, Choice("p2"),
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 6, 1),
                out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal(0, result.GoodsStolen);
            Assert.Equal(0, result.Pay);
            Assert.Equal(4, zoe.Contraband);
        }

        [Fact]
        public void Showdown_loss_issues_warrant_and_discards_illegal_piracy_job()
        {
            // FAQ 4.1 p.12: Illegal Piracy → Warrant on Showdown loss; discard Job.
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(ContractJumper);
            mal.TechBonus = 5;
            // Attack die low, defend high → lose showdown.
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", ContractJumper, Choice("p2"),
                ScriptedRng.FromDieFaces(6, 6, 6, 6, 6, 1, 6),
                out var result, out var error), error);
            Assert.True(result!.ShowdownLost);
            Assert.Equal(1, result.WarrantsIssued);
            Assert.Equal(1, mal.Warrants);
            Assert.DoesNotContain(ContractJumper, mal.JobHand);
            Assert.Null(mal.FindActive(ContractJumper));
            Assert.True(game.ContactDecks!.TryGet("Amnon Duul", out var deck));
            Assert.Contains(deck!.DiscardPile, j => j.Id == ContractJumper);
        }

        [Fact]
        public void Pilfer_showdown_loss_kills_crew_and_issues_warrant()
        {
            var (game, mal, _) = TwoShips();
            mal.JobHand.Add(Pilfer);
            mal.TalkBonus = 1;
            Assert.True(mal.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            // Boarding Negotiate 1d6=6; showdown attack 1 vs defend 6; no Medic → kill die unused.
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", Pilfer, Choice("p2", board: Skill.Talk),
                ScriptedRng.FromDieFaces(6, 1, 6),
                out var result, out var error), error);
            Assert.True(result!.ShowdownLost);
            Assert.Equal(1, result.CrewKilled);
            Assert.Equal(1, result.WarrantsIssued);
            Assert.Equal(0, mal.Roster.Count);
        }

        [Fact]
        public void Subjective_morality_disgruntles_when_target_leader_is_moral()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(Pilfer);
            mal.TalkBonus = 1;
            mal.FightBonus = 5;
            Assert.True(mal.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _)); // Moral
            Assert.True(zoe.Roster.TryHire(game.Leaders!.Get("leader_malcolm"), out _)); // Moral Leader
            zoe.StashHold = 0;
            zoe.Cargo = 1;
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", Pilfer, Choice("p2", board: Skill.Talk),
                ScriptedRng.FromDieFaces(6, 6, 1),
                out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.True(result.MoralDisgruntled >= 1);
            Assert.True(mal.Roster.Find("crew_kaylee")!.Disgruntled);
        }

        [Fact]
        public void Subjective_morality_skips_disgruntle_vs_non_moral_leader()
        {
            var (game, mal, zoe) = TwoShips();
            mal.JobHand.Add(Pilfer);
            mal.TalkBonus = 1;
            mal.FightBonus = 5;
            Assert.True(mal.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            Assert.True(zoe.Roster.TryHire(game.Leaders!.Get("leader_womack"), out _));
            zoe.StashHold = 0;
            zoe.Cargo = 1;
            Assert.True(new PiracyAction().TryPirate(
                game, "p1", Pilfer, Choice("p2", board: Skill.Talk),
                ScriptedRng.FromDieFaces(6, 6, 1),
                out var result, out var error), error);
            Assert.True(result!.Success);
            Assert.Equal(0, result.MoralDisgruntled);
            Assert.False(mal.Roster.Find("crew_kaylee")!.Disgruntled);
        }

        [Fact]
        public void Bounty_confrontation_boarding_uses_shared_boarding_test()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var mal = new PlayerState("p1", "Mal", Persephone) { TalkBonus = 1 };
            var zoe = new PlayerState("p2", "Zoe", Persephone);
            var game = new GameState(map, new[] { mal, zoe })
            {
                Bounties = BountyCatalog.LoadDefault(),
                Crew = CrewCatalog.LoadDefault(),
                UsePiratesBountyHunters = true
            };
            game.BountyDeck = new BountyDeck(
                new[] { game.Bounties.Get("bounty_billy") }, new SystemRng(2), game.Bounties);
            Assert.True(zoe.Roster.TryHire(game.Crew.Get("crew_billy"), out _));

            // Fight is not a boarding skill — must fail closed.
            Assert.False(new BountyAction().TryApprehendRival(
                game, "p1", "bounty_billy", "p2", "crew_billy",
                Skill.Fight, Skill.Talk, Skill.Fight, ScriptedRng.FromDieFaces(6),
                out _, out var error));
            Assert.Contains("Tech or Negotiate", error);

            Assert.True(new BountyAction().TryApprehendRival(
                game, "p1", "bounty_billy", "p2", "crew_billy",
                Skill.Fight, Skill.Talk, Skill.Talk, ScriptedRng.FromDieFaces(6, 6, 1),
                out var ok, out error), error);
            Assert.True(ok!.Success);
        }
    }
}
