using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class AllianceAlertTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Harvest = "border-red-sun-r2-02";
        private const string Albion = "alliance-white-sun-r4-11";
        private const string Shipping = "job_amnon-duul_feeding-alliance-fat-cats";

        [Fact]
        public void Catalog_loads_ten_alerts()
        {
            var catalog = AllianceAlertCatalog.LoadDefault();
            Assert.Equal(10, catalog.Cards.Count);
            Assert.True(catalog.TryResolve("Persons of Interest", out var persons));
            Assert.Equal("Persons of Interest", persons.Name);
            Assert.True(persons.SystemWide);
            Assert.True(catalog.TryResolve("Privilege Suspension", out var privilege));
            Assert.Equal("Privilege Suspension", privilege.Name);
            Assert.True(catalog.TryResolve("Alliance Audit", out var audit));
            Assert.False(audit.SystemWide);
            Assert.True(catalog.TryResolve("Enhanced Inspection", out var inspect));
            Assert.False(inspect.SystemWide);
        }

        [Fact]
        public void Catalog_has_every_printed_name()
        {
            var catalog = AllianceAlertCatalog.LoadDefault();
            string[] names =
            {
                "Persons of Interest",
                "Alliance Audit",
                "Rapid Response",
                "Criminal Sighting",
                "Privilege Suspension",
                "Incarceration Order",
                "White Sun Commerce",
                "Criminal Activity",
                "Background Checks",
                "Enhanced Inspection"
            };
            foreach (var name in names)
                Assert.True(catalog.TryResolve(name, out _), name);
        }

        [Fact]
        public void Standard_setup_wires_a_shuffled_deck_with_no_active_alert()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });

            Assert.NotNull(game.AllianceAlerts);
            Assert.NotNull(game.AllianceAlertDeck);
            Assert.Equal(10, game.AllianceAlerts!.Cards.Count);
            Assert.Equal(10, game.AllianceAlertDeck!.DrawCount);
            Assert.Null(game.AllianceAlertDeck.Active);
            Assert.False(game.AllianceAlertDeck.IsParked);
        }

        [Fact]
        public void Alliance_high_alert_starts_with_one_active()
        {
            var setup = SetupCatalog.LoadDefault().Get("setup_alliance-high-alert");
            Assert.True(setup.StartingAlertCard);

            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    SetupCardId = "setup_alliance-high-alert",
                    DealStartingJobs = false,
                    Rng = new SystemRng(11)
                });

            Assert.NotNull(game.AllianceAlertDeck!.Active);
            Assert.Equal(9, game.AllianceAlertDeck.DrawCount);
            Assert.False(game.AllianceAlertDeck.IsParked);
        }

        [Fact]
        public void Smugglers_blues_starts_with_one_active()
        {
            var scenario = ScenarioCatalog.LoadDefault().Get("scenario_smugglers-blues");
            Assert.True(scenario.StartingAlertCard);

            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    ScenarioCardId = "scenario_smugglers-blues",
                    DealStartingJobs = false,
                    Rng = new SystemRng(12)
                });

            Assert.NotNull(game.AllianceAlertDeck!.Active);
            Assert.Equal(9, game.AllianceAlertDeck.DrawCount);
        }

        [Fact]
        public void DrawAndActivate_buries_the_previous_active_on_the_bottom()
        {
            var catalog = AllianceAlertCatalog.LoadDefault();
            Assert.True(catalog.TryResolve("Persons of Interest", out var first));
            Assert.True(catalog.TryResolve("Alliance Audit", out var second));
            var deck = new AllianceAlertDeck(new[] { first, second }, new SystemRng(1), catalog);

            var shown = deck.DrawAndActivate();
            Assert.NotNull(shown);
            Assert.Equal(1, deck.DrawCount);

            var next = deck.DrawAndActivate();
            Assert.NotNull(next);
            Assert.NotEqual(shown!.Id, next!.Id);
            Assert.Same(next, deck.Active);
            Assert.Equal(1, deck.DrawCount);

            deck.BuryActive();
            Assert.Null(deck.Active);
            Assert.Equal(2, deck.DrawCount);
        }

        [Fact]
        public void Park_lifts_and_is_cleared_when_the_active_is_buried()
        {
            var catalog = AllianceAlertCatalog.LoadDefault();
            var deck = AllianceAlertDeck.FromCatalog(catalog, new SystemRng(4));
            deck.DrawAndActivate();
            Assert.NotNull(deck.Active);

            deck.ParkOnContact("Harken");
            Assert.True(deck.IsParked);
            Assert.True(deck.IsParkedOnContact("harken"));
            Assert.False(deck.IsParkedOnSupply("Persephone"));

            deck.LiftParked();
            Assert.False(deck.IsParked);
            Assert.NotNull(deck.Active);

            deck.ParkOnSupply("Osiris");
            Assert.True(deck.IsParkedOnSupply("Osiris"));

            var parked = deck.Active;
            deck.DrawAndActivate();
            Assert.NotSame(parked, deck.Active);
            Assert.False(deck.IsParked);
            Assert.Equal(9, deck.DrawCount);
        }

        [Fact]
        public void Alliance_Cruiser_Nav_cycles_the_active_alert_and_the_wanted_list()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    SetupCardId = "setup_alliance-high-alert",
                    DealStartingJobs = false,
                    Rng = new SystemRng(11)
                });

            var first = game.AllianceAlertDeck!.Active;
            Assert.NotNull(first);
            game.AllianceAlertDeck.ParkOnContact("Harken");

            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            var resolver = new NavResolver();
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error), error);
            Assert.True(resolution!.Stopped);
            Assert.NotNull(game.AllianceAlertDeck.Active);
            Assert.NotEqual(first!.Id, game.AllianceAlertDeck.Active!.Id);
            Assert.False(game.AllianceAlertDeck.IsParked);
            Assert.Equal(9, game.AllianceAlertDeck.DrawCount);
            Assert.Equal(3, game.BountyDeck.FaceUp.Count);
        }

        [Fact]
        public void Alliance_Alert_Misbehave_replaces_the_active_alert()
        {
            var game = CrimeGameWithAlertDeck();
            StartCrime(game);
            game.AllianceAlertDeck!.DrawAndActivate();
            var first = game.AllianceAlertDeck.Active;
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-alert"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6)), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.NotNull(game.AllianceAlertDeck.Active);
            Assert.NotEqual(first!.Id, game.AllianceAlertDeck.Active!.Id);
            Assert.Equal(9, game.AllianceAlertDeck.DrawCount);
        }

        [Fact]
        public void Alliance_Alert_Misbehave_botches_when_the_die_is_at_most_warrants()
        {
            var game = CrimeGameWithAlertDeck();
            game.CurrentPlayer.Warrants = 3;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-alert_2"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(3)), error);

            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.NotNull(game.AllianceAlertDeck!.Active);
            Assert.Contains("job_badger_badgers-11-casino-caper", game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Alliance_Alert_Misbehave_proceeds_with_zero_warrants()
        {
            var game = CrimeGameWithAlertDeck();
            Assert.Equal(0, game.CurrentPlayer.Warrants);
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_alliance-alert"));

            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);

            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.NotNull(game.AllianceAlertDeck!.Active);
        }

        [Fact]
        public void Privilege_Suspension_stops_Harken_from_counting_as_Solid()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            game.Scenario = ScenarioCatalog.LoadDefault().Get("scenario_first-time-in-the-captains-chair");
            var player = game.CurrentPlayer;
            player.BecomeSolid("contact_harken");
            player.BecomeSolid("contact_amnon-duul");
            ForceActive(game, "Privilege Suspension");

            Assert.True(player.IsSolidWith("contact_harken"));
            Assert.False(ActiveAlertRules.CountsAsSolidWith(game, player, "contact_harken"));
            Assert.Equal(1, ActiveAlertRules.CountedSolidCount(game, player));
            WinCheck.Refresh(game);
            Assert.DoesNotContain(1, player.CompletedGoals);
        }

        [Fact]
        public void White_Sun_Commerce_blocks_Buy_and_Shore_Leave_at_Persephone_with_warrants()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            game.CurrentPlayer.Warrants = 1;
            ForceActive(game, "White Sun Commerce");

            var buy = new BuyAction();
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var buyError));
            Assert.Contains("White Sun Commerce", buyError);

            var shore = new ShoreLeaveAction();
            Assert.False(shore.TryShoreLeave(game, "p1", out _, out var shoreError));
            Assert.Contains("White Sun Commerce", shoreError);
        }

        [Fact]
        public void Enhanced_Inspection_blocks_selling_cargo_but_not_contraband()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            game.CurrentPlayer.Cargo = 2;
            game.CurrentPlayer.Contraband = 1;
            ForceActive(game, "Enhanced Inspection");

            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger", SellCargo = 1 }, out _, out var error));
            Assert.Contains("Enhanced Inspection", error);

            Assert.True(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Badger", SellContraband = 1 }, out var sold, out var contraError), contraError);
            Assert.Equal(1, sold!.ContrabandSold);
            Assert.Equal(0, game.CurrentPlayer.Contraband);
        }

        [Fact]
        public void Criminal_Activity_adds_one_Misbehave_on_an_illegal_job_with_warrants()
        {
            var game = CrimeGameWithAlertDeck();
            game.CurrentPlayer.Warrants = 1;
            ForceActive(game, "Criminal Activity");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
            Assert.Equal(4, game.PendingMisbehave!.Remaining);
        }

        [Fact]
        public void Alliance_Audit_parks_on_the_job_contact_and_blocks_Deal()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Harvest) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Alliance Audit");
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out _, out var pickupError), pickupError);
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWork(game, "p1", Shipping, out var done, out var dropError), dropError);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.True(game.AllianceAlertDeck!.IsParkedOnContact("Amnon Duul"));

            game.EndTurn();
            var deal = new DealAction();
            Assert.False(deal.TryDeal(game, "p1", new DealRequest { ContactName = "Amnon Duul", ConsiderCount = 1 }, out _, out var blocked));
            Assert.Contains("Alliance Audit", blocked);
        }

        [Fact]
        public void Criminal_Sighting_parks_on_the_supply_deck_after_hiring_Wanted_crew()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Criminal Sighting");
            var river = game.Crew!.Get("crew_river-tam");
            Assert.True(river.Wanted);
            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            market.FaceUp.Clear();
            market.FaceUp.Add(new SupplyCard(
                river.Id, river.Name, river.Cost, SupplyKind.Crew,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Persephone"] = 1 }));

            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { SupplyCardIds = { river.Id } }, out _, out var error), error);
            Assert.True(game.AllianceAlertDeck!.IsParkedOnSupply("Persephone"));
            Assert.True(game.CurrentPlayer.Roster.HasName("River Tam"));

            game.EndTurn();
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var blocked));
            Assert.Contains("Criminal Sighting", blocked);
        }

        [Fact]
        public void Background_Checks_requires_rolling_higher_than_warrants_to_dodge()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            var player = game.CurrentPlayer;
            player.Warrants = 2;
            player.Cash = 2500;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            ForceActive(game, "Background Checks");
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Persephone;

            Assert.True(CruiserBoarding.TryResolve(game, ScriptedRng.FromDieFaces(2, 3), out var result, out var error), error);
            Assert.Equal(2000, result!.FinePaid);
            Assert.Equal(1, result.WantedRemoved);
            Assert.False(player.Roster.HasName("Jayne"));
            Assert.True(player.Roster.HasName("Zoe"));
        }

        [Fact]
        public void Incarceration_Order_removes_one_Wanted_instead_of_one_warrant_fine()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            var player = game.CurrentPlayer;
            player.Warrants = 2;
            player.Cash = 2500;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            ForceActive(game, "Incarceration Order");
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Persephone;

            Assert.True(CruiserBoarding.TryResolve(
                game,
                ScriptedRng.FromDieFaces(6),
                out var result,
                out var error,
                new CruiserBoardingChoice { UseIncarcerationOrder = true, RemoveWantedCrewId = "crew_jayne" }), error);
            Assert.Equal(1000, result!.FineAssessed);
            Assert.Equal(1000, result.FinePaid);
            Assert.Equal(1500, player.Cash);
            Assert.Equal(0, player.Warrants);
            Assert.False(player.Roster.HasName("Jayne"));
            Assert.Contains("crew_jayne", game.RemovedFromPlay);
            Assert.Equal(0, result.WantedRemoved);
        }

        [Fact]
        public void Persons_of_Interest_disgruntles_the_whole_crew_on_a_job_botch()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo, leaderId: "leader_malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Persons of Interest");
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var startError), startError);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(work.TryProceedMisbehave(game, "p1", proceed: false, out _, out var botchError), botchError);
            Assert.True(game.CurrentPlayer.Roster.Count >= 2);
            Assert.Equal(game.CurrentPlayer.Roster.Count, game.CurrentPlayer.Roster.DisgruntledCount);
        }

        [Fact]
        public void White_Sun_Commerce_does_not_block_Buy_without_warrants_or_off_White_Sun()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "White Sun Commerce");
            Assert.Equal(0, game.CurrentPlayer.Warrants);
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var error), error);

            game.EndTurn();
            game.CurrentPlayer.Warrants = 2;
            game.CurrentPlayer.SectorId = Santo;
            Assert.False(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var santo));
            Assert.Contains("Supply planet", santo);
        }

        [Fact]
        public void Criminal_Activity_does_not_add_Misbehave_on_a_legal_job()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Harvest) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            game.CurrentPlayer.Warrants = 3;
            ForceActive(game, "Criminal Activity");
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out var result, out var error), error);
            Assert.False(result!.AwaitingMisbehave);
            Assert.Null(game.PendingMisbehave);
        }

        [Fact]
        public void Criminal_Activity_does_not_add_Misbehave_without_warrants()
        {
            var game = CrimeGameWithAlertDeck();
            Assert.Equal(0, game.CurrentPlayer.Warrants);
            ForceActive(game, "Criminal Activity");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var error), error);
            Assert.Equal(3, game.PendingMisbehave!.Remaining);
        }

        [Fact]
        public void Criminal_Sighting_ignores_non_Wanted_hires()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Criminal Sighting");
            var kaylee = game.Crew!.Get("crew_kaylee");
            Assert.False(kaylee.Wanted);
            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            market.FaceUp.Clear();
            market.FaceUp.Add(new SupplyCard(
                kaylee.Id, kaylee.Name, kaylee.Cost, SupplyKind.Crew,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Persephone"] = 1 }));

            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { SupplyCardIds = { kaylee.Id } }, out _, out var error), error);
            Assert.False(game.AllianceAlertDeck!.IsParked);
            game.EndTurn();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1 }, out _, out var fuelError), fuelError);
        }

        [Fact]
        public void Alliance_Audit_does_not_park_on_pickup()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Harvest) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Alliance Audit");
            game.CurrentPlayer.JobHand.Add(Shipping);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out _, out var error), error);
            Assert.False(game.AllianceAlertDeck!.IsParked);
        }

        [Fact]
        public void Alliance_Audit_moves_when_a_different_contact_is_completed()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Harvest) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Alliance Audit");
            game.CurrentPlayer.JobHand.Add(Shipping);
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Shipping, out _, out _));
            game.EndTurn();
            game.CurrentPlayer.SectorId = Albion;
            Assert.True(work.TryWork(game, "p1", Shipping, out _, out var dropError), dropError);
            Assert.True(game.AllianceAlertDeck!.IsParkedOnContact("Amnon Duul"));

            game.EndTurn();
            game.CurrentPlayer.SectorId = Santo;
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var crimeError), crimeError);
            Assert.True(start!.AwaitingMisbehave);
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", true, out var done, out var last), last);
            Assert.Equal(WorkKind.Complete, done!.Kind);
            Assert.True(game.AllianceAlertDeck.IsParkedOnContact("Badger"));
            Assert.False(game.AllianceAlertDeck.IsParkedOnContact("Amnon Duul"));
        }

        [Fact]
        public void Incarceration_Order_is_refused_when_the_card_is_not_active()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            var player = game.CurrentPlayer;
            player.Warrants = 1;
            player.Cash = 2000;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            ForceActive(game, "Rapid Response");
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Persephone;

            Assert.False(CruiserBoarding.TryResolve(
                game,
                ScriptedRng.FromDieFaces(6),
                out _,
                out var error,
                new CruiserBoardingChoice { UseIncarcerationOrder = true, RemoveWantedCrewId = "crew_jayne" }));
            Assert.Contains("Incarceration Order", error);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(1, player.Warrants);
            Assert.True(player.Roster.HasName("Jayne"));
        }

        [Fact]
        public void Background_Checks_with_zero_warrants_uses_the_default_dodge()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            var player = game.CurrentPlayer;
            player.Warrants = 0;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            ForceActive(game, "Background Checks");
            game.PendingEncounter = TokenKind.AllianceCruiser;
            game.PendingEncounterSectorId = Persephone;

            Assert.True(CruiserBoarding.TryResolve(game, ScriptedRng.FromDieFaces(2), out var result, out var error), error);
            Assert.Equal(0, result!.WantedRemoved);
            Assert.True(player.Roster.HasName("Jayne"));
        }

        [Fact]
        public void Persons_of_Interest_does_not_fire_when_another_alert_is_active()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo, leaderId: "leader_malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Rapid Response");
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            game.CurrentPlayer.JobHand.Add(Crime);
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out _, out _));
            Assert.True(work.TryProceedMisbehave(game, "p1", proceed: false, out _, out _));
            Assert.Equal(0, game.CurrentPlayer.Roster.DisgruntledCount);
        }

        [Fact]
        public void Privilege_Suspension_does_not_block_other_Solid_goals()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            game.Scenario = ScenarioCatalog.LoadDefault().Get("scenario_first-time-in-the-captains-chair");
            var player = game.CurrentPlayer;
            player.BecomeSolid("contact_badger");
            player.BecomeSolid("contact_amnon-duul");
            ForceActive(game, "Privilege Suspension");
            WinCheck.Refresh(game);
            Assert.Contains(1, player.CompletedGoals);
        }

        [Fact]
        public void Rapid_Response_does_not_apply_to_Alliance_Cruiser_Nav_snap()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            ForceActive(game, "Rapid Response");
            Assert.Equal(1, ActiveAlertRules.ExtraAllianceShipMove(game));
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));
            var resolver = new NavResolver();
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error), error);
            Assert.Equal(Persephone, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
        }

        [Fact]
        public void Rapid_Response_grants_one_extra_Alliance_ship_sector()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            Assert.Equal(0, ActiveAlertRules.ExtraAllianceShipMove(game));
            ForceActive(game, "Rapid Response");
            Assert.Equal(1, ActiveAlertRules.ExtraAllianceShipMove(game));
        }

        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        private static void ForceActive(GameState game, string name)
        {
            var catalog = game.AllianceAlerts ?? AllianceAlertCatalog.LoadDefault();
            game.AllianceAlerts = catalog;
            Assert.True(catalog.TryResolve(name, out var card), name);
            game.AllianceAlertDeck = new AllianceAlertDeck(new[] { card }, new SystemRng(1), catalog);
            game.AllianceAlertDeck.DrawAndActivate();
        }

        private static GameState CrimeGameWithAlertDeck()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 500, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            game.Crew = CrewCatalog.LoadDefault();
            game.Gear = GearIndex.LoadDefault();
            var misbehave = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(misbehave.Cards.Values, new SystemRng(2), misbehave);
            game.AllianceAlerts = AllianceAlertCatalog.LoadDefault();
            game.AllianceAlertDeck = AllianceAlertDeck.FromCatalog(game.AllianceAlerts, new SystemRng(5));
            player.JobHand.Add(Crime);
            return game;
        }

        private static void StartCrime(GameState game)
        {
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
        }
    }
}
