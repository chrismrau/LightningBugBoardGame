using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// Slice 4 — Contact Solid benefits beyond a tag.
    /// FAQ 4.1 Amnon; Harken card / Privilege Suspension; Blue Sun Mr. Universe &amp; Harrow;
    /// Kalidasa Higgins &amp; Fanty; Niska Pound of Flesh (FAQ 4.1).
    /// </summary>
    public class ContactSolidBenefitsTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Bazaar = "border-red-sun-r3-08";
        private const string Harvest = "border-red-sun-r2-02";
        private const string Albion = "alliance-white-sun-r4-11";
        private const string Highgate = "rim-blue-sun-r2-01";
        private const string Beaumonde = "rim-kalidasa-r4-14";
        private const string Osiris = "alliance-white-sun-r3-07";

        private const string Shipping = "job_amnon-duul_feeding-alliance-fat-cats";
        private const string Transport = "job_amnon-duul_homesteader-transport";
        private const string NiskaCrime = "job_niska_a-judge-in-niskas-pocket";

        private static GameState NewGame(string sectorId = Persephone, int cash = 5000)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: cash, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            game.Crew = CrewCatalog.LoadDefault();
            return game;
        }

        [Fact]
        public void Default_limits_are_three_active_and_three_hand()
        {
            var player = new PlayerState("p1", "Mal", Persephone);
            Assert.Equal(3, player.ActiveJobLimit);
            Assert.Equal(3, player.JobHandLimit);
        }

        [Fact]
        public void Solid_Amnon_Deal_loads_passengers_and_fugitives()
        {
            // FAQ 4.1 p.6: load Passengers and Fugitives as part of a Deal Action when Solid.
            var game = NewGame(Bazaar);
            var amnon = game.Contacts!.Cards["contact_amnon-duul"];
            game.CurrentPlayer.CargoHold = 8;
            game.CurrentPlayer.StashHold = 0;
            var deal = new DealAction();

            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Amnon Duul",
                LoadPassengers = 1
            }, out _, out var needSolid));
            Assert.Contains("Amnon", needSolid);

            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, amnon.Id);
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Amnon Duul",
                LoadPassengers = 2,
                LoadFugitives = 1
            }, out var result, out var error), error);
            Assert.Equal(2, result!.PassengersLoaded);
            Assert.Equal(1, result.FugitivesLoaded);
            Assert.Equal(2, game.CurrentPlayer.Passengers);
            Assert.Equal(1, game.CurrentPlayer.Fugitives);
        }

        [Fact]
        public void Solid_Harken_may_ignore_Customs_Inspection()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Pelorum, fuel: 3) { Contraband = 2, Fugitives = 2 };
            var game = new GameState(map, new[] { player });
            game.Contacts = ContactCatalog.LoadDefault();
            game.Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_customs-inspection"));
            var resolver = new NavResolver();
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game, 0, out _, out var needSolid,
                choice: new NavResolveChoice { IgnoreCustomsInspection = true }));
            Assert.Contains("Harken", needSolid);

            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.True(resolver.TryResolve(
                game, 0, out var ignored, out var error,
                choice: new NavResolveChoice { IgnoreCustomsInspection = true }), error);
            Assert.Equal(FlightOutcome.KeepFlying, ignored!.Outcome);
            Assert.False(ignored.Stopped);
            Assert.Equal(2, player.Contraband);
            Assert.Equal(2, player.Fugitives);
            Assert.Equal(0, ignored.ContrabandSeized);
        }

        [Fact]
        public void Privilege_Suspension_blocks_Harken_Customs_ignore()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Pelorum, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Contacts = ContactCatalog.LoadDefault();
            ForcePrivilegeSuspension(game);
            ContactSolidBenefits.BecomeSolid(game, player, "contact_harken");
            Assert.False(ContactSolidBenefits.IsSolidHarken(game, player));

            game.Decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_customs-inspection"));
            var resolver = new NavResolver();
            resolver.DrawNext(game);
            Assert.False(resolver.TryResolve(
                game, 0, out _, out var error,
                choice: new NavResolveChoice { IgnoreCustomsInspection = true }));
            Assert.Contains("Harken", error);
        }

        [Fact]
        public void Solid_Mr_Universe_raises_hand_size_by_two_and_discards_down_on_loss()
        {
            // Blue Sun p.11–12: Max Hand +2 when Solid; lose Solid → discard down.
            var game = NewGame();
            var universe = game.Contacts!.Cards["contact_mr-universe"];
            var player = game.CurrentPlayer;
            player.JobHand.Add("j1");
            player.JobHand.Add("j2");
            player.JobHand.Add("j3");
            player.JobHand.Add("j4");
            player.JobHand.Add("j5");

            ContactSolidBenefits.BecomeSolid(game, player, universe.Id);
            Assert.Equal(5, player.JobHandLimit);
            Assert.Equal(5, player.JobHand.Count);

            Assert.False(ContactSolidBenefits.TryLoseSolid(
                game, player, universe.Id, new SolidRepChoice(), out var needDiscard));
            Assert.Contains("discard", needDiscard, System.StringComparison.OrdinalIgnoreCase);

            Assert.True(ContactSolidBenefits.TryLoseSolid(
                game, player, universe.Id,
                new SolidRepChoice { DiscardJobHandIds = { "j4", "j5" } },
                out var error), error);
            Assert.Equal(3, player.JobHandLimit);
            Assert.Equal(3, player.JobHand.Count);
            Assert.False(player.IsSolidWith(universe.Id));
        }

        [Fact]
        public void Solid_Higgins_raises_active_job_limit_and_discards_on_loss()
        {
            // Kalidasa p.9: Solid Higgins → one additional Active Job (up to 4).
            var game = NewGame(Harvest);
            var higgins = game.Contacts!.Cards["contact_magistrate-higgins"];
            var player = game.CurrentPlayer;
            ContactSolidBenefits.BecomeSolid(game, player, higgins.Id);
            Assert.Equal(4, player.ActiveJobLimit);

            player.ActiveJobs.Add(new ActiveJob(Shipping));
            player.ActiveJobs.Add(new ActiveJob(Transport));
            player.ActiveJobs.Add(new ActiveJob("job_x"));
            player.ActiveJobs.Add(new ActiveJob("job_y"));

            Assert.True(ContactSolidBenefits.TryLoseSolid(
                game, player, higgins.Id,
                new SolidRepChoice { DiscardActiveJobId = "job_y" },
                out var error), error);
            Assert.Equal(3, player.ActiveJobLimit);
            Assert.Equal(3, player.ActiveJobs.Count);
            Assert.Null(player.FindActive("job_y"));
        }

        [Fact]
        public void Solid_Fanty_pays_500_on_Transport_completion()
        {
            // Kalidasa p.9–10: When Solid with Fanty and Mingo, $500 on Transport Jobs.
            var game = NewGame(Bazaar);
            var fanty = game.Contacts!.Cards["contact_fanty-and-mingo"];
            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, fanty.Id);
            Assert.Equal(0, ContactSolidBenefits.CompletionBonus(game, game.CurrentPlayer,
                new JobCard("t", "t", "x", "Crime", true, false, null, null, null, null, 0, null, null, null, null)));
            Assert.Equal(500, ContactSolidBenefits.CompletionBonus(game, game.CurrentPlayer,
                new JobCard("t", "t", "x", "Transport", true, false, null, null, null, null, 0, null, null, null, null)));
            Assert.Equal(500, ContactSolidBenefits.CompletionBonus(game, game.CurrentPlayer,
                new JobCard("t", "t", "x", "Transport / Smuggling", true, false, null, null, null, null, 0, null, null, null, null)));
        }

        [Fact]
        public void Solid_Harrow_pays_500_on_Smuggling_or_Shipping_completion()
        {
            // Blue Sun p.9: When Solid with Lord Harrow, +$500 on Smuggling or Shipping.
            var game = NewGame(Harvest);
            var harrow = game.Contacts!.Cards["contact_lord-harrow"];
            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, harrow.Id);
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out _, out _));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWork(game, "p1", Shipping, out var done, out var error), error);
            // Base 1500 + Companion may apply if roster has one; Completeness: Harrow +500 always.
            Assert.True(done!.Pay >= 2000, $"expected at least 2000 (1500+500), got {done.Pay}");
            Assert.Equal(1500 + 500, done.Pay); // no Companion in roster
        }

        [Fact]
        public void Solid_Fanty_may_buy_Contraband_on_Deal()
        {
            var game = NewGame(Beaumonde, cash: 2000);
            var fanty = game.Contacts!.Cards["contact_fanty-and-mingo"];
            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, fanty.Id);
            game.CurrentPlayer.CargoHold = 6;
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Fanty & Mingo",
                BuyContraband = 2
            }, out var result, out var error), error);
            Assert.Equal(2, result!.ContrabandBought);
            Assert.Equal(800, result.CashSpentBuying);
            Assert.Equal(2, game.CurrentPlayer.Contraband);
            Assert.Equal(1200, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Solid_Harken_may_buy_Fuel_on_Deal_at_100_each()
        {
            // FAQ 4.1: "When you're Solid with Harken, the Alliance Cruiser becomes a
            // refueling station. You may purchase as much Fuel as you'd like from Harken
            // for $100 each, when Dealing with Harken."
            var game = NewGame(Persephone, cash: 500);
            game.Tokens = new MapTokens(allianceCruiserSectorId: Persephone);
            game.CurrentPlayer.CargoHold = 8;
            game.CurrentPlayer.Fuel = 0;
            var deal = new DealAction();

            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Harken",
                BuyFuel = 2
            }, out _, out var needSolid));
            Assert.Contains("Harken", needSolid);

            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, "contact_harken");
            Assert.Equal(100, game.Contacts!.Cards["contact_harken"].BuyPrices!.Fuel);

            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Harken",
                BuyFuel = 2
            }, out var result, out var error), error);
            Assert.Equal(2, result!.FuelBought);
            Assert.Equal(200, result.CashSpentBuying);
            Assert.Equal(2, game.CurrentPlayer.Fuel);
            Assert.Equal(300, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Privilege_Suspension_blocks_Harken_Fuel_buy()
        {
            var game = NewGame(Persephone, cash: 500);
            game.Tokens = new MapTokens(allianceCruiserSectorId: Persephone);
            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, "contact_harken");
            ForcePrivilegeSuspension(game);
            Assert.False(ContactSolidBenefits.IsSolidHarken(game, game.CurrentPlayer));

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Harken",
                BuyFuel = 1
            }, out _, out var error));
            Assert.Contains("Harken", error);
        }

        [Fact]
        public void Contacts_json_places_Coyotes_and_Protect_My_Property_under_solidAbility()
        {
            var contacts = ContactCatalog.LoadDefault();
            var fanty = contacts.Cards["contact_fanty-and-mingo"];
            Assert.Equal("Coyotes", fanty.SolidAbilityName);
            Assert.Contains("Solid", fanty.SolidAbilityText, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Transport", fanty.SolidAbilityText, System.StringComparison.OrdinalIgnoreCase);

            var harrow = contacts.Cards["contact_lord-harrow"];
            Assert.Equal("Protect My Property", harrow.SolidAbilityName);
            Assert.Contains("Solid", harrow.SolidAbilityText, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Solid_Harrow_may_buy_Cargo_on_Deal()
        {
            var game = NewGame(Highgate, cash: 1000);
            var harrow = game.Contacts!.Cards["contact_lord-harrow"];
            ContactSolidBenefits.BecomeSolid(game, game.CurrentPlayer, harrow.Id);
            game.CurrentPlayer.CargoHold = 6;
            var deal = new DealAction();
            Assert.True(deal.TryDeal(game, "p1", new DealRequest
            {
                ContactName = "Lord Harrow",
                BuyCargo = 2
            }, out var result, out var error), error);
            Assert.Equal(2, result!.CargoBought);
            Assert.Equal(600, result.CashSpentBuying);
            Assert.Equal(2, game.CurrentPlayer.Cargo);
            Assert.Equal(400, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void Niska_Pound_of_Flesh_kills_a_crew_when_Warrant_issued_while_Working()
        {
            // Contacts.json + FAQ 4.1: Warrant while working a Niska Job → Kill a Crew; Job discarded.
            var game = NewGame(Osiris, cash: 500);
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            Assert.True(player.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            player.JobHand.Add(NiskaCrime);
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);

            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", NiskaCrime, out var start, out var err), err);
            Assert.True(start!.AwaitingMisbehave);

            game.Misbehave.PlaceOnTop(catalog.Get("misbehave_keep-a-low-profile"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 1,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 0 },
                    Kill = new KillChoice { VictimCrewIds = new List<string> { "crew_kaylee" } }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);

            Assert.Equal(1, resolution!.WarrantsIssued);
            Assert.Equal(1, player.Warrants);
            Assert.True(resolution.CrewKilled >= 1);
            Assert.DoesNotContain(NiskaCrime, player.JobHand);
            Assert.Null(player.FindActive(NiskaCrime));
            Assert.Null(game.PendingMisbehave);
        }

        [Fact]
        public void firstSolidWithDistinctContacts_uses_CountedSolidCount()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Persephone);
            var game = new GameState(map, new[] { player })
            {
                Scenario = ScenarioCatalog.LoadDefault().Get("scenario_down-and-out"),
                Contacts = ContactCatalog.LoadDefault()
            };
            ForcePrivilegeSuspension(game);

            for (var i = 1; i <= 4; i++)
                player.BecomeSolid(i.ToString());
            player.BecomeSolid("contact_harken");
            // Raw SolidCount is 5, but CountedSolidCount excludes Harken under Privilege Suspension.
            Assert.Equal(5, player.SolidCount);
            Assert.Equal(4, ActiveAlertRules.CountedSolidCount(game, player));
            Assert.Null(WinCheck.Refresh(game));
            player.BecomeSolid("contact_amnon-duul");
            Assert.NotNull(WinCheck.Refresh(game));
        }

        private static void ForcePrivilegeSuspension(GameState game)
        {
            var catalog = AllianceAlertCatalog.LoadDefault();
            Assert.True(catalog.TryResolve("Privilege Suspension", out var card));
            game.AllianceAlerts = catalog;
            game.AllianceAlertDeck = new AllianceAlertDeck(new[] { card }, new SystemRng(1), catalog);
            game.AllianceAlertDeck.DrawAndActivate();
        }
    }
}
