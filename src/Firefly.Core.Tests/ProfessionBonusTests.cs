using System;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// GF9 p.15 / Director's Cut: Bonus Tab pays once for the listed Profession.
    /// Jobs.json bonus inventory (171 non-null): cash professions, Mechanic Parts,
    /// and TRANSPORT +200 keyword cash (Higgins Mud Runs; user ruling).
    /// </summary>
    public class ProfessionBonusTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Harvest = "border-red-sun-r2-02";
        private const string Albion = "alliance-white-sun-r4-11";
        private const string Bazaar = "border-red-sun-r3-08";
        private const string Aphrodite = "border-murphy-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Jiangyin = "border-red-sun-r1-01";
        private const string Ithaca = "border-georgia-r2-01";
        private const string CortexRelay2 = "rim-cortex-relay-2-r1-11";
        private const string Angel = "rim-kalidasa-r2-04";
        private const string Aberdeen = "rim-kalidasa-r3-01";

        private const string CompanionJob = "job_amnon-duul_feeding-alliance-fat-cats"; // Companion +500
        private const string GrifterJob = "job_amnon-duul_courting-aphrodite"; // Grifter +500
        private const string Soldier300Job = "job_amnon-duul_gun-running-jiangyin"; // Soldier +300
        private const string MedicJob = "job_amnon-duul_homesteader-transport"; // Medic +400
        private const string Mechanic1Job = "job_amnon-duul_haulin-military-scrap"; // Mechanic +1 Part
        private const string Mechanic2Job = "job_harken_repair-team-transport"; // Mechanic +2 Parts
        private const string Soldier500Job = "job_niska_where-angels-fear-to-tread"; // Soldier +500
        private const string TransportBonusJob = "job_magistrate-higgins_mud-run-aberdeen"; // TRANSPORT +200

        private static CrewCatalog Crew => CrewCatalog.LoadDefault();
        private static JobCatalog Jobs => JobCatalog.LoadDefault();

        private static JobCard Job(string id) => Jobs.Get(id);

        private static JobCard Synthetic(string bonus) =>
            new JobCard(
                "job_test",
                "Test",
                "Contact",
                "Shipping",
                true,
                false,
                null,
                null,
                null,
                null,
                1000,
                "1000",
                bonus,
                null,
                null);

        private static GameState NewGame(string sectorId = Persephone)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: 0, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = Jobs;
            game.Contacts = ContactCatalog.LoadDefault();
            game.Crew = Crew;
            return game;
        }

        [Theory]
        [InlineData("Soldier +300", "Soldier", 300)]
        [InlineData("Soldier +500", "Soldier", 500)]
        [InlineData("Grifter +500", "Grifter", 500)]
        [InlineData("Companion +500", "Companion", 500)]
        [InlineData("Medic +400", "Medic", 400)]
        public void Cash_profession_bonus_parses_once(string bonus, string profession, int expected)
        {
            var job = Synthetic(bonus);
            Assert.Equal(expected, JobTerms.ProfessionBonus(job, p => p == profession));
            Assert.Equal(0, JobTerms.ProfessionPartsBonus(job, p => p == profession));
            // GF9: "A Bonus is only paid once, regardless of how many Crew..."
            var calls = 0;
            Func<string, bool> multi = p =>
            {
                if (p == profession)
                {
                    calls++;
                    return true;
                }
                return false;
            };
            Assert.Equal(expected, JobTerms.ProfessionBonus(job, multi));
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData("Mechanic +1 Part", 1)]
        [InlineData("Mechanic +2 Parts", 2)]
        public void Mechanic_parts_bonus_parses_once(string bonus, int parts)
        {
            var job = Synthetic(bonus);
            Assert.Equal(0, JobTerms.ProfessionBonus(job, p => p == "Mechanic"));
            Assert.Equal(parts, JobTerms.ProfessionPartsBonus(job, p => p == "Mechanic"));
            Assert.Equal(0, JobTerms.ProfessionPartsBonus(job, _ => false));
        }

        [Fact]
        public void Jobs_json_cash_and_parts_strings_match_parsers()
        {
            Assert.Equal(500, JobTerms.ProfessionBonus(Job(CompanionJob), p => p == "Companion"));
            Assert.Equal(500, JobTerms.ProfessionBonus(Job(GrifterJob), p => p == "Grifter"));
            Assert.Equal(300, JobTerms.ProfessionBonus(Job(Soldier300Job), p => p == "Soldier"));
            Assert.Equal(500, JobTerms.ProfessionBonus(Job(Soldier500Job), p => p == "Soldier"));
            Assert.Equal(400, JobTerms.ProfessionBonus(Job(MedicJob), p => p == "Medic"));
            Assert.Equal(1, JobTerms.ProfessionPartsBonus(Job(Mechanic1Job), p => p == "Mechanic"));
            Assert.Equal(2, JobTerms.ProfessionPartsBonus(Job(Mechanic2Job), p => p == "Mechanic"));
        }

        [Fact]
        public void Transport_bonus_is_keyword_cash_not_profession()
        {
            // User ruling: TRANSPORT +200 is a Transport keyword bonus, not a profession.
            var job = Job(TransportBonusJob);
            Assert.Equal("TRANSPORT +200", job.Bonus);
            Assert.Equal(0, JobTerms.ProfessionBonus(job, _ => true));
            Assert.Equal(0, JobTerms.ProfessionPartsBonus(job, _ => true));
            Assert.Equal(0, JobTerms.ProfessionBonus(Synthetic("TRANSPORT +200"), p => p == "TRANSPORT"));
            Assert.Equal(200, JobTerms.KeywordBonus(job, kw => kw == "TRANSPORT"));
            Assert.Equal(0, JobTerms.KeywordBonus(job, _ => false));
            // Once only — HasKeyword is boolean, not a count.
            var calls = 0;
            Assert.Equal(200, JobTerms.KeywordBonus(Synthetic("TRANSPORT +200"), kw =>
            {
                calls++;
                return string.Equals(kw, "TRANSPORT", StringComparison.OrdinalIgnoreCase);
            }));
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Work_mud_run_pays_transport_keyword_with_leader_marco()
        {
            // User: pay if any on-job entity has Transport — Leader Marco qualifies.
            var game = NewMudRunGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                LeaderCatalog.LoadDefault().Get("leader_marco"), out _));
            CompleteMudRun(game, out var done, out var error);
            Assert.True(error == null, error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(2600, done.Pay); // 2400 + 200
        }

        [Fact]
        public void Work_mud_run_pays_transport_keyword_with_disgruntled_marco()
        {
            // User: Disgruntled still counts for Transport keyword.
            var game = NewMudRunGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                LeaderCatalog.LoadDefault().Get("leader_marco"), out _));
            var marco = game.CurrentPlayer.Roster.Leader!;
            marco.Disgruntled = true;
            CompleteMudRun(game, out var done, out var error);
            Assert.True(error == null, error);
            Assert.Equal(2600, done!.Pay);
        }

        [Fact]
        public void Work_mud_run_pays_transport_keyword_with_gear()
        {
            // GF9 p.14: commit carried Gear while Working. Kernel HasTag uses owned gear
            // until a carriage/Onboard model exists (same as Misbehave Aces).
            var game = NewMudRunGame();
            game.Gear = GearIndex.LoadDefault();
            game.CurrentPlayer.Gear.Add("gear_4wd-mule");
            CompleteMudRun(game, out var done, out var error);
            Assert.True(error == null, error);
            Assert.Equal(2600, done!.Pay);
        }

        [Fact]
        public void Work_mud_run_pays_transport_once_with_marco_and_gear()
        {
            var game = NewMudRunGame();
            game.Gear = GearIndex.LoadDefault();
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                LeaderCatalog.LoadDefault().Get("leader_marco"), out _));
            game.CurrentPlayer.Gear.Add("gear_4wd-mule");
            CompleteMudRun(game, out var done, out var error);
            Assert.True(error == null, error);
            Assert.Equal(2600, done!.Pay); // +200 once, not stacked
        }

        [Fact]
        public void Work_mud_run_pays_base_only_without_transport_keyword()
        {
            var game = NewMudRunGame();
            CompleteMudRun(game, out var done, out var error);
            Assert.True(error == null, error);
            Assert.Equal(2400, done!.Pay);
        }

        private static GameState NewMudRunGame()
        {
            var game = NewGame(Harvest);
            game.CurrentPlayer.JobHand.Add(TransportBonusJob);
            return game;
        }

        private static void CompleteMudRun(GameState game, out WorkResult? done, out string? error)
        {
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", TransportBonusJob, out _, out error), error);
            Assert.Equal(5, game.CurrentPlayer.Cargo);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Aberdeen;
            Assert.True(work.TryWork(game, "p1", TransportBonusJob, out done, out error), error);
        }

        [Fact]
        public void Work_completion_pays_companion_cash_bonus_once()
        {
            // Director's Cut: Credits in the bonus tab are added to the Pay.
            var game = NewGame(Harvest);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_inara"), out _));
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_bridgit"), out _)); // second Companion
            game.CurrentPlayer.JobHand.Add(CompanionJob);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", CompanionJob, out _, out var error), error);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWork(game, "p1", CompanionJob, out var done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(2000, done.Pay); // 1500 + 500 once
            Assert.Equal(2000, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Work_completion_pays_soldier_300_on_smuggling_dropoff()
        {
            var game = NewGame(Santo);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_zoe"), out _));
            game.CurrentPlayer.JobHand.Add(Soldier300Job);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Soldier300Job, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(start.BecameActive);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var picked, out error), error);
            Assert.False(picked!.BecameActive);
            Assert.Equal(2, game.CurrentPlayer.Contraband);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Jiangyin;
            Assert.True(work.TryWork(game, "p1", Soldier300Job, out var done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(2300, done.Pay); // 2000 + 300
        }

        [Fact]
        public void Work_completion_pays_grifter_cash_bonus()
        {
            var game = NewGame(Bazaar);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_accountant_breakinatmo"), out _));
            game.CurrentPlayer.JobHand.Add(GrifterJob);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", GrifterJob, out _, out var error), error);
            Assert.Equal(2, game.CurrentPlayer.Contraband);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Aphrodite;
            Assert.True(work.TryWork(game, "p1", GrifterJob, out var start, out error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(2500, done.Pay); // 2000 + 500
            Assert.Equal(2500, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Work_completion_pays_medic_per_passenger_plus_bonus()
        {
            // PayRaw 200/P; unlimited Pass loads 1; Medic +400 once.
            var game = NewGame(Bazaar);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_doralee"), out _));
            game.CurrentPlayer.JobHand.Add(MedicJob);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", MedicJob, out _, out var error), error);
            Assert.Equal(1, game.CurrentPlayer.Passengers);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Ithaca;
            Assert.True(work.TryWork(game, "p1", MedicJob, out var done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(600, done.Pay); // 200*1 + 400
        }

        [Fact]
        public void Work_completion_grants_mechanic_two_parts()
        {
            // GF9 p.15: take the bonus listed (Parts, not cash).
            var game = NewGame(Persephone);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_kaylee"), out _));
            game.CurrentPlayer.JobHand.Add(Mechanic2Job);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Mechanic2Job, out _, out var error), error);
            Assert.Equal(2, game.CurrentPlayer.Passengers);
            game.EndTurn();
            game.CurrentPlayer.SectorId = CortexRelay2;
            game.CurrentPlayer.Parts = 0;
            Assert.True(work.TryWork(game, "p1", Mechanic2Job, out var done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(1500, done.Pay);
            Assert.Equal(2, game.CurrentPlayer.Parts);
        }

        [Fact]
        public void Work_completion_pays_soldier_500_crime_bonus()
        {
            var game = NewGame(Angel);
            Assert.True(game.CurrentPlayer.Roster.TryHire(Crew.Get("crew_zoe"), out _));
            game.CurrentPlayer.JobHand.Add(Soldier500Job);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Soldier500Job, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            WorkResult? done = null;
            for (var i = 0; i < 4; i++)
                Assert.True(work.TryProceedMisbehave(game, "p1", true, out done, out error), error);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.Equal(4500, done.Pay); // 4000 + 500
        }

        [Fact]
        public void No_matching_profession_pays_base_only()
        {
            var game = NewGame(Harvest);
            game.CurrentPlayer.JobHand.Add(CompanionJob);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", CompanionJob, out _, out var error), error);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWork(game, "p1", CompanionJob, out var done, out error), error);
            Assert.Equal(1500, done!.Pay);
        }
    }
}
