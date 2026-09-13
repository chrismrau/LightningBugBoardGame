using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class NavTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";

        private static string NavPath => GameData.NavCardsPath;
        private static string MapDir => GameData.MapDirectory;

        [Fact]
        public void Regional_decks_each_have_sixty_cards()
        {
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(1));
            Assert.Equal(60, decks.Alliance.DrawCount);
            Assert.Equal(60, decks.Border.DrawCount);
            Assert.Equal(60, decks.Rim.DrawCount);
        }

        [Fact]
        public void Big_Black_is_keep_flying()
        {
            var catalog = NavCatalog.LoadFromFile(NavPath);
            var card = catalog.Get("nav_the-big-black");
            Assert.Equal(FlightOutcome.KeepFlying, card.Options[0].Outcome);
            Assert.False(card.IsReshuffle);
        }

        [Fact]
        public void Alliance_Cruiser_is_full_stop_reshuffle()
        {
            var catalog = NavCatalog.LoadFromFile(NavPath);
            var card = catalog.Get("nav_alliance-cruiser");
            Assert.True(card.IsReshuffle);
            Assert.Equal(FlightOutcome.FullStop, card.Options[0].Outcome);
        }

        [Fact]
        public void Keep_Flying_consumes_one_draw_and_leaves_the_ship()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_the-big-black"));

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error));
            Assert.Null(error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Single(game.PendingNavDraws);
        }

        [Fact]
        public void Full_Stop_rewinds_to_that_sector_and_cancels_remaining_draws()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            player.SectorId = "rim-blue-sun-r3-01";
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out _));
            Assert.True(resolution!.Stopped);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Empty(game.PendingNavDraws);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Reshuffle_card_returns_discard_to_the_draw_pile()
        {
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(2));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_alliance-cruiser"));
            var afterPlace = decks.Alliance.DrawCount;
            var card = decks.Alliance.Draw();
            Assert.Equal("nav_alliance-cruiser", card.Id);
            decks.Alliance.ResolveIntoDiscard(card);
            Assert.Equal(0, decks.Alliance.DiscardCount);
            Assert.Equal(afterPlace, decks.Alliance.DrawCount);
        }

        [Fact]
        public void Multi_option_card_stays_face_up_until_chosen()
        {
            var (game, resolver, _) = GameWithQueuedDraws(1);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ifn-the-coil-busts-were-driftin"));

            Assert.False(resolver.TryAutoResolve(game, out _, out var error));
            Assert.Contains("option choice", error);
            Assert.NotNull(resolver.FaceUp);
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out _));
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal("Replace the Whole Damn Mess", resolution.Option.Name);
        }

        [Fact]
        public void Cruiser_Patrol_moves_one_Alliance_Sector_Keep_Flying_no_Contact()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            var choice = new NavResolveChoice { AllianceCruiserToSectorId = Bernadette };
            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(Bernadette, game.Tokens.AllianceCruiserSectorId);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Single(game.PendingNavDraws);
        }

        [Fact]
        public void Cruiser_Patrol_rejects_non_adjacent_or_non_Alliance_destination()
        {
            var (game, resolver, _) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_cruiser-patrol"));

            Assert.False(resolver.TryAutoResolve(
                game,
                out _,
                out var farError,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }));
            Assert.Contains("1 Sector", farError);

            Assert.False(resolver.TryAutoResolve(
                game,
                out _,
                out var borderError,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = BorderNearPersephone }));
            Assert.Contains("Alliance Space", borderError);
        }

        [Fact]
        public void Alliance_Entanglements_Wild_Gosling_moves_to_empty_Alliance_Sector_no_Contact()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));

            var drawn = resolver.DrawNext(game);
            Assert.Equal("nav_alliance-entanglements", drawn.Card.Id);
            Assert.True(resolver.TryResolve(
                game,
                0,
                out var resolution,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = EmptyAllianceNearPelorum }), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(EmptyAllianceNearPelorum, game.Tokens.AllianceCruiserSectorId);
            Assert.Null(game.PendingEncounter);
            Assert.Equal(Pelorum, player.SectorId);
        }

        [Fact]
        public void Alliance_Entanglements_Wild_Gosling_rejects_Firefly_occupied_Sector()
        {
            var (game, resolver, _) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game,
                0,
                out _,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }));
            Assert.Contains("Firefly", error);
        }

        [Fact]
        public void Alliance_Entanglements_Legitimate_Tip_requires_Solid_Harken_pays_500()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            game.Contacts = ContactCatalog.LoadDefault();
            player.Warrants = 1; // Outlaw in Pelorum (destination gate runs before Requires)
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game,
                1,
                out _,
                out var needSolid,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }));
            Assert.Contains("Solid Harken", needSolid);
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);

            player.BecomeSolid("contact_harken");
            Assert.True(resolver.TryResolve(
                game,
                1,
                out var resolution,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = Pelorum }), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(500, player.Cash);
            Assert.Null(game.PendingEncounter);
        }

        [Fact]
        public void Alliance_Entanglements_Legitimate_Tip_rejects_Sector_without_Outlaw()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            player.BecomeSolid("contact_harken");
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-entanglements"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(
                game,
                1,
                out _,
                out var error,
                choice: new NavResolveChoice { AllianceCruiserToSectorId = EmptyAllianceNearPelorum }));
            Assert.Contains("Outlaw", error);
        }

        [Fact]
        public void Named_Alliance_Cruiser_still_snaps_and_queues_Contact()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Tokens = game.Tokens.WithAllianceCruiser(Londinium);
            player.SectorId = "rim-blue-sun-r3-01";
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-cruiser"));

            Assert.True(resolver.TryAutoResolve(game, out var resolution, out var error), error);
            Assert.True(resolution!.Stopped);
            Assert.Equal(Pelorum, player.SectorId);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Pelorum, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Spend_2_Parts_Keep_Flying_deducts_parts()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            player.Parts = 2;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ifn-the-coil-busts-were-driftin"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.False(resolution.Stopped);
            Assert.Equal(0, player.Parts);
            Assert.Single(game.PendingNavDraws);
        }

        [Fact]
        public void Spend_2_Parts_unavailable_without_enough_parts()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.Parts = 1;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ifn-the-coil-busts-were-driftin"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 1, out _, out var error));
            Assert.Contains("Parts", error);
            Assert.Equal(1, player.Parts);
            Assert.NotNull(resolver.FaceUp);
        }

        [Fact]
        public void Spend_1_Part_to_Keep_Flying_otherwise_Full_Stop()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            player.Parts = 1;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_first-rule-of-flying"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var kept, out var keepError), keepError);
            Assert.Equal(FlightOutcome.KeepFlying, kept!.Outcome);
            Assert.Equal(0, player.Parts);
            Assert.Single(game.PendingNavDraws);

            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_first-rule-of-flying"));
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 1, out var stopped, out var stopError), stopError);
            Assert.Equal(FlightOutcome.FullStop, stopped!.Outcome);
            Assert.True(stopped.Stopped);
            Assert.Equal(0, player.Parts);
            Assert.Empty(game.PendingNavDraws);
        }

        [Fact]
        public void Requires_Mechanic_and_Spend_1_Part()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.Parts = 2;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_shes-tore-up-plenty"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 1, out _, out var error));
            Assert.Contains("Mechanic", error);
            Assert.Equal(2, player.Parts);

            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_kaylee"), out _));
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var ok), ok);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal(1, player.Parts);
        }

        [Fact]
        public void Requires_Solid_with_Badger_gates_option()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Contacts = ContactCatalog.LoadDefault();
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_badgers-boys"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 0, out _, out var needSolid));
            Assert.Contains("Badger", needSolid);

            player.BecomeSolid("contact_badger");
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
        }

        [Fact]
        public void Requires_Soldier_gates_without_profession()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_failure-to-communicate"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 0, out _, out var error));
            Assert.Contains("Soldier", error);

            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_zoe"), out _));
            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var ok), ok);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
        }

        [Fact]
        public void Nav_Hazard_Pilot_keeps_flying_free_else_spends_fuel()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            player.Fuel = 2;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_nav-hazard-asteroid"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 0, out var noPilot, out var err1), err1);
            Assert.Equal(FlightOutcome.KeepFlying, noPilot!.Outcome);
            Assert.Equal(1, player.Fuel);

            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_wash"), out _));
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_nav-hazard-asteroid"));
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 0, out var withPilot, out var err2), err2);
            Assert.Equal(FlightOutcome.KeepFlying, withPilot!.Outcome);
            Assert.Equal(1, player.Fuel);
        }

        [Fact]
        public void Crazy_Ivan_still_requires_Pilot_Mechanic_and_fuel()
        {
            var (game, resolver, player) = BorderGameForCrazyIvan();
            game.Decks!.Border.PlaceOnTop(game.Decks.Catalog.Get("nav_reaver-cutter"));
            player.Fuel = 1;
            resolver.DrawNext(game);
            var choice = new NavResolveChoice { EvadeToSectorId = CutterAdjacent };

            Assert.False(resolver.TryResolve(game, 1, out _, out var needCrew, choice: choice));
            Assert.Contains("Pilot and Mechanic", needCrew);
            Assert.Equal(1, player.Fuel);

            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_wash"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_kaylee"), out _));
            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, choice: choice), error);
            Assert.Equal(FlightOutcome.Evade, resolution!.Outcome);
            Assert.Equal(0, player.Fuel);
        }

        [Fact]
        public void Punctured_Fuel_Lines_fail_band_loses_fuel()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.Fuel = 4;
            player.TechBonus = 1;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_punctured-fuel-lines"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error, ScriptedRng.FromDieFaces(2)), error);
            Assert.False(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.FullStop, resolution.Outcome);
            Assert.Equal(2, resolution.FuelLost);
            Assert.Equal(2, player.Fuel);
            Assert.Equal(Pelorum, player.SectorId);
        }

        [Fact]
        public void Punctured_Fuel_Lines_success_keeps_flying_without_fuel_loss()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            player.Fuel = 4;
            player.TechBonus = 2;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_punctured-fuel-lines"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error, ScriptedRng.FromDieFaces(5, 5)), error);
            Assert.True(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.KeepFlying, resolution.Outcome);
            Assert.Equal(0, resolution.FuelLost);
            Assert.Equal(4, player.Fuel);
            Assert.Single(game.PendingNavDraws);
        }

        [Fact]
        public void Ghost_Ship_fail_band_kills_crew()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_zoe"), out _));
            Assert.True(player.Roster.TryHire(catalog.Get("crew_kaylee"), out _));
            player.FightBonus = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ghost-ship"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, ScriptedRng.FromDieFaces(1)), error);
            Assert.False(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.FullStop, resolution.Outcome);
            Assert.Equal(2, resolution.CrewKilled);
            Assert.Equal(1, player.Roster.Count);
            Assert.Equal(0, resolution.CashGained);
            Assert.Equal(0, player.Cash);
        }

        [Fact]
        public void Ghost_Ship_success_band_takes_cash()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.FightBonus = 2;
            player.Cash = 100;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ghost-ship"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, ScriptedRng.FromDieFaces(6, 6)), error);
            Assert.True(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.FullStop, resolution.Outcome);
            Assert.Equal(0, resolution.CrewKilled);
            Assert.Equal(1000, resolution.CashGained);
            Assert.Equal(1100, player.Cash);
        }

        [Fact]
        public void Scrapper_Ambush_fail_kills_a_crew_success_takes_cash_and_parts()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            player.FightBonus = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_scrapper-ambush"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var fail, out var failErr, ScriptedRng.FromDieFaces(1)), failErr);
            Assert.False(fail!.SkillCheck!.Success);
            Assert.Equal(1, fail.CrewKilled);
            Assert.Equal(0, player.Roster.Count);
            Assert.Equal(0, fail.CashGained);

            player.FightBonus = 3;
            player.Parts = 1;
            player.Cash = 0;
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            game.Decks.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_scrapper-ambush"));
            resolver.DrawNext(game);
            Assert.True(resolver.TryResolve(game, 1, out var win, out var winErr, ScriptedRng.FromDieFaces(6, 6, 6)), winErr);
            Assert.True(win!.SkillCheck!.Success);
            Assert.Equal(500, win.CashGained);
            Assert.Equal(500, player.Cash);
            Assert.Equal(3, player.Parts);
        }

        [Fact]
        public void Alliance_Checkpoint_fail_band_issues_warrant()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.TalkBonus = 1;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-checkpoint"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, ScriptedRng.FromDieFaces(2)), error);
            Assert.False(resolution!.SkillCheck!.Success);
            Assert.Equal(FlightOutcome.FullStop, resolution.Outcome);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Equal(1, player.Warrants);
        }

        [Fact]
        public void Locking_Horns_loads_goods_from_skill_band_with_choice()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.TalkBonus = 2;
            player.Cargo = 0;
            player.Fuel = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_locking-horns-over-scraps"));
            resolver.DrawNext(game);
            var choice = new NavResolveChoice
            {
                LoadGoodsCargo = 2,
                LoadGoodsFuel = 1
            };

            Assert.True(resolver.TryResolve(
                game,
                0,
                out var resolution,
                out var error,
                ScriptedRng.FromDieFaces(6, 6),
                choice), error);
            Assert.True(resolution!.SkillCheck!.Success);
            Assert.Equal(3, resolution.GoodsLoaded);
            Assert.Equal(2, player.Cargo);
            Assert.Equal(1, player.Fuel);
            Assert.Equal(FlightOutcome.FullStop, resolution.Outcome);
        }

        [Fact]
        public void Locking_Horns_load_goods_requires_composition_choice()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.TalkBonus = 1;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_locking-horns-over-scraps"));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 0, out _, out var error, ScriptedRng.FromDieFaces(1)));
            Assert.Contains("Goods composition", error);
            Assert.Equal(0, player.Cargo);
            Assert.NotNull(resolver.FaceUp);
        }

        [Fact]
        public void Nested_Fight_band_does_not_apply_kill_without_nested_roll()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            var catalog = CrewCatalog.LoadDefault();
            Assert.True(player.Roster.TryHire(catalog.Get("crew_jayne"), out _));
            player.TalkBonus = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_alliance-interrogation"));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error, ScriptedRng.FromDieFaces(1)), error);
            Assert.False(resolution!.SkillCheck!.Success);
            Assert.Equal(0, resolution.CrewKilled);
            Assert.Equal(0, resolution.WarrantsIssued);
            Assert.Equal(1, player.Roster.Count);
            Assert.Equal(0, player.Warrants);
        }

        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string EmptyAllianceNearPelorum = "alliance-white-sun-r3-02";
        private const string BorderNearPersephone = "border-space-r1-04";
        private const string CutterStart = "border-space-r2-06";
        private const string CutterAdjacent = "border-space-r2-05";

        private static (GameState Game, NavResolver Resolver, PlayerState Player) GameWithQueuedDraws(int draws)
        {
            var map = SectorMap.LoadFromDirectory(MapDir);
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Pelorum);
            var game = new GameState(map, new[] { player }, decks: decks);
            for (var i = 0; i < draws; i++)
                game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            return (game, new NavResolver(), player);
        }

        private static (GameState Game, NavResolver Resolver, PlayerState Player) BorderGameForCrazyIvan()
        {
            var map = SectorMap.LoadFromDirectory(MapDir);
            var decks = NavCatalog.BuildDecks(NavPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", CutterStart, fuel: 3);
            var tokens = new MapTokens(reaverCutterSectorIds: new[] { "rim-blue-sun-r3-01" });
            var game = new GameState(map, new[] { player }, tokens, decks);
            game.PendingNavDraws.Add(new PendingNavDraw(CutterStart, NavRegion.Border));
            return (game, new NavResolver(), player);
        }
    }
}
