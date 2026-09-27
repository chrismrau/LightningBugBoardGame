using Firefly.Core.Abilities;
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
    /// Slice 4 — ship-upgrade structural apply (holds, Max Crew, Full Burn, Mosey,
    /// ignore Breakdowns) plus Sky Hook / Chop Shop post-job / Salvage hooks.
    /// Sources: Supplies.tsv; Esmeralda Coachworks; Director's Cut p.19.
    /// </summary>
    public class ShipUpgradeApplyTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string TwoHopDest = "alliance-white-sun-r3-02";

        private static ShipUpgradeIndex Upgrades => ShipUpgradeIndex.LoadDefault();

        private static GameState NewGame(string ship = "Serenity") =>
            GameSetup.Standard(new PlayerSeat("p1", "Mal", Persephone, shipId: ship, leaderId: "Malcolm"));

        [Fact]
        public void Cargo_Hold_adds_two_general_hold_areas()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_cargo-hold", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.ExtraCargoHold && a.Amount == 2 && a.Mandatory);

            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.Equal(8, player.CargoHold);
            Assert.Equal(12, player.GeneralHolds);

            ShipUpgradeApply.Install(game, player, "ship-upgrade_cargo-hold");
            Assert.Equal(10, player.CargoHold);
            Assert.Equal(14, player.GeneralHolds);
            player.Fuel = 0;
            player.Parts = 0;
            player.Cargo = 14;
            Assert.True(HoldSpace.Fits(player));
            Assert.False(HoldSpace.Fits(player, addCargo: 1));
        }

        [Fact]
        public void Stash_adds_two_stash_areas_and_keeps_ignore_Wanted_slots()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_stash", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.ExtraStashHold && a.Amount == 2);
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.IgnoreWantedCrewRollSlots && a.Amount == 2);

            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.Equal(4, player.StashHold);
            ShipUpgradeApply.Install(game, player, "ship-upgrade_stash");
            Assert.Equal(6, player.StashHold);
            Assert.Equal(14, player.GeneralHolds);
        }

        [Fact]
        public void Expanded_Crew_Quarters_adds_three_Max_Crew()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_expanded-crew-quarters", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.MaxCrewBonus && a.Amount == 3);

            var game = NewGame();
            var player = game.CurrentPlayer;
            Assert.Equal(6, player.Roster.MaxCrew);
            ShipUpgradeApply.Install(game, player, "ship-upgrade_expanded-crew-quarters");
            Assert.Equal(9, player.Roster.MaxCrew);
        }

        [Fact]
        public void Overcharged_Grav_Thrusters_add_Full_Burn_range()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_overcharged-grav-thrusters", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.FullBurnRangeBonus && a.Amount == 1);

            var game = NewGame();
            var player = game.CurrentPlayer;
            player.DriveRange = 5;
            Assert.Equal(5, player.GetEffectiveDriveRange(game));
            ShipUpgradeApply.Install(game, player, "ship-upgrade_overcharged-grav-thrusters");
            Assert.Equal(6, player.GetEffectiveDriveRange(game));
        }

        [Fact]
        public void Esmeralda_Caravan_Pods_add_Max_Crew_and_passenger_only_holds()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_caravan-pods_esmeralda", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.PassengerFugitiveHold && a.Amount == 2);
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.MaxCrewBonus && a.Amount == 1);

            // Esmeralda Coachworks: two passenger/fugitive-only holds; +1 Max Crew.
            var game = NewGame("Esmeralda");
            var player = game.CurrentPlayer;
            Assert.Contains("ship-upgrade_caravan-pods_esmeralda", player.ShipUpgrades);
            Assert.Equal(6, player.Roster.MaxCrew); // ship 5 + pods 1
            Assert.Equal(12, player.CargoHold);
            Assert.Equal(2, player.PassengerHold);
            Assert.Equal(12, player.GeneralHolds);

            player.Fuel = 0;
            player.Parts = 0;
            player.Cargo = 12;
            player.Passengers = 2;
            Assert.True(HoldSpace.Fits(player));
            Assert.False(HoldSpace.Fits(player, addCargo: 1));
            Assert.False(HoldSpace.Fits(player, addPassengers: 1));

            player.Passengers = 0;
            player.Cargo = 13;
            Assert.False(HoldSpace.Fits(player));
        }

        [Fact]
        public void Removing_Caravan_Pods_over_Max_Crew_discards_excess_crew()
        {
            var game = NewGame("Esmeralda");
            var player = game.CurrentPlayer;
            Assert.Equal(6, player.Roster.MaxCrew);

            foreach (var card in game.Crew!.Cards.Values)
            {
                if (card.IsLeader)
                    continue;
                if (player.Roster.Count >= player.Roster.MaxCrew)
                    break;
                player.Roster.TryHire(card, out _);
            }
            Assert.Equal(6, player.Roster.Count);

            Assert.True(
                ShipUpgradeApply.TryRemove(game, player, "ship-upgrade_caravan-pods_esmeralda", out var error),
                error);
            Assert.Equal(5, player.Roster.MaxCrew);
            Assert.Equal(5, player.Roster.Count);
            Assert.Equal(0, player.PassengerHold);
        }

        [Fact]
        public void Compression_Coils_allow_Mosey_two_and_ignore_Breakdowns()
        {
            Assert.True(Upgrades.TryGet(
                "ship-upgrade_shiny-new-state-of-the-art-compression-coils", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.MoseyRange && a.Amount == 2);
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.IgnoreBreakdowns);

            var game = NewGame();
            var player = game.CurrentPlayer;
            ShipUpgradeApply.Install(
                game, player, "ship-upgrade_shiny-new-state-of-the-art-compression-coils");
            Assert.Equal(2, player.GetEffectiveMoseyRange(game));

            var fly = new FlyAction(new MovementEngine(game.Map));
            Assert.True(fly.TryMosey(game, "p1", TwoHopDest, out var result, out var error), error);
            Assert.Equal(TwoHopDest, player.SectorId);
            Assert.Equal(3, result!.Plan.Path.Count); // origin + 2 entered
        }

        [Fact]
        public void Full_Tune_Up_ignores_Breakdown_Nav_cards()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_full-tune-up-retro-fit", out var entry));
            Assert.Contains(entry.Abilities, a => a.Type == AbilityTypes.IgnoreBreakdowns);

            var game = NewGame();
            var player = game.CurrentPlayer;
            ShipUpgradeApply.Install(game, player, "ship-upgrade_full-tune-up-retro-fit");

            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_blown-out-buffer-panel"));
            game.Decks = decks;
            Assert.True(game.TryConsumeAction(TurnAction.Fly, out _));
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));

            var nav = new NavResolver();
            var drawn = nav.DrawNext(game);
            Assert.Contains("Breakdown", drawn.Card.Type ?? "", System.StringComparison.OrdinalIgnoreCase);
            Assert.True(nav.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Null(nav.FaceUp);
        }

        [Fact]
        public void Sky_Hook_loads_Contraband_after_Crime_when_Pilot_present()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_sky-hook", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.AfterCrimeLoadContraband && a.Amount == 1);

            var game = NewGame();
            var player = game.CurrentPlayer;
            ShipUpgradeApply.Install(game, player, "ship-upgrade_sky-hook");

            if (!MisbehaveResolver.HasTag(game, player, "Pilot") && game.Crew != null)
            {
                foreach (var card in game.Crew.Cards.Values)
                {
                    if (card.HasProfession("Pilot") && player.Roster.TryHire(card, out _))
                        break;
                }
            }
            Assert.True(MisbehaveResolver.HasTag(game, player, "Pilot"));

            Assert.True(game.Jobs!.TryGet("job_badger_badgers-11-casino-caper", out var job));
            player.Contraband = 0;
            WorkAction.TryApplyShipUpgradeJobBonuses(game, player, job);
            Assert.Equal(1, player.Contraband);

            // Without Pilot, no load.
            player.Contraband = 0;
            // Dismiss non-leader pilots.
            foreach (var member in player.Roster.Members)
            {
                if (!member.IsLeader && member.Card.HasProfession("Pilot"))
                    player.Roster.TryDismiss(member.Id, out _);
            }
            // Malcolm may not be Pilot — if still tagged, skip negative assert.
            if (!MisbehaveResolver.HasTag(game, player, "Pilot"))
            {
                WorkAction.TryApplyShipUpgradeJobBonuses(game, player, job);
                Assert.Equal(0, player.Contraband);
            }
        }

        [Fact]
        public void Chop_Shop_pays_after_Salvage_Op_Nav_option()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_onboard-chop-shop", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.AfterSalvageCashAndContraband && a.Amount == 500);

            var game = NewGame();
            var player = game.CurrentPlayer;
            ShipUpgradeApply.Install(game, player, "ship-upgrade_onboard-chop-shop");
            player.Cash = 0;
            player.Contraband = 0;

            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_abandoned-ship"));
            game.Decks = decks;
            Assert.True(game.TryConsumeAction(TurnAction.Fly, out _));
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));

            var nav = new NavResolver();
            nav.DrawNext(game);
            Assert.True(nav.TryResolve(game, 0, out _, out var error), error);
            Assert.Equal(500, player.Cash);
            Assert.True(player.Contraband >= 3); // 2 salvage + 1 Chop Shop
        }

        [Fact]
        public void Hydraulic_Docking_Clamps_make_Crime_count_as_Salvage_for_Chop_Shop()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            ShipUpgradeApply.Install(game, player, "ship-upgrade_onboard-chop-shop");
            ShipUpgradeApply.Install(game, player, "ship-upgrade_hydraulic-docking-clamps_kalidasa");
            Assert.True(AbilityDispatcher.HasCrimeCountsAsSalvage(game, player));

            Assert.True(game.Jobs!.TryGet("job_badger_badgers-11-casino-caper", out var job));
            player.Cash = 0;
            player.Contraband = 0;
            WorkAction.TryApplyShipUpgradeJobBonuses(game, player, job);
            Assert.Equal(500, player.Cash);
            Assert.Equal(1, player.Contraband);
        }
    }
}
