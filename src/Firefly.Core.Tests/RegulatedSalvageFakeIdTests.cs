using Firefly.Core.Abilities;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// Regulated Salvage (Kalidasa / Alliance Nav): printed
    /// "If you have FAKE ID, you may: Load 3 Cargo and Full Stop. Otherwise: Load 3 Contraband. Warrant Issued. Full Stop."
    /// </summary>
    public class RegulatedSalvageFakeIdTests
    {
        private const string Pelorum = "alliance-lux-r1-02";
        private const string RegulatedSalvageId = "nav_regulated-salvage";

        [Fact]
        public void Without_Fake_ID_loads_contraband_and_issues_warrant()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            player.Cargo = 0;
            player.Contraband = 0;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
            Assert.True(resolution.Stopped);
            Assert.Equal(3, resolution.GoodsLoaded);
            Assert.Equal(0, player.Cargo);
            Assert.Equal(3, player.Contraband);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Equal(1, player.Warrants);
        }

        [Fact]
        public void With_Fake_ID_crew_suspends_PendingChoice()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_shepherd-book"), out _));
            Assert.True(MisbehaveResolver.HasTag(game, player, "Fake ID"));
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 0, out var resolution, out var error));
            Assert.Null(resolution);
            Assert.Contains("FAKE ID", error);
            Assert.NotNull(game.PendingChoice);
            Assert.Equal(PendingChoiceKinds.NavFakeIdSalvage, game.PendingChoice!.Kind);
            Assert.Equal(
                new[] { NavFakeIdSalvageOptions.UseFakeId, NavFakeIdSalvageOptions.Otherwise },
                game.PendingChoice.Options);
            Assert.NotNull(resolver.FaceUp);
            Assert.Equal(0, player.Cargo);
            Assert.Equal(0, player.Contraband);
            Assert.Equal(0, player.Warrants);
        }

        [Fact]
        public void Resume_UseFakeId_loads_cargo_without_warrant()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_shepherd-book"), out _));
            player.Cargo = 0;
            player.Contraband = 0;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 0, out _, out _));
            Assert.True(
                resolver.TryResumeFakeIdSalvage(
                    game,
                    new ChoiceSubmission { SelectedOptionId = NavFakeIdSalvageOptions.UseFakeId },
                    out var resolution,
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
            Assert.Equal(3, resolution.GoodsLoaded);
            Assert.Equal(3, player.Cargo);
            Assert.Equal(0, player.Contraband);
            Assert.Equal(0, resolution.WarrantsIssued);
            Assert.Equal(0, player.Warrants);
        }

        [Fact]
        public void Resume_Otherwise_loads_contraband_and_issues_warrant()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_shepherd-book"), out _));
            player.Cargo = 0;
            player.Contraband = 0;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.False(resolver.TryResolve(game, 0, out _, out _));
            Assert.True(
                resolver.TryResumeFakeIdSalvage(
                    game,
                    new ChoiceSubmission { SelectedOptionId = NavFakeIdSalvageOptions.Otherwise },
                    out var resolution,
                    out var error),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(FlightOutcome.FullStop, resolution!.Outcome);
            Assert.Equal(3, resolution.GoodsLoaded);
            Assert.Equal(0, player.Cargo);
            Assert.Equal(3, player.Contraband);
            Assert.Equal(1, resolution.WarrantsIssued);
            Assert.Equal(1, player.Warrants);
        }

        [Fact]
        public void Thin_hook_UseFakeIdSalvage_true_skips_suspend()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_shepherd-book"), out _));
            player.Cargo = 0;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.True(
                resolver.TryResolve(
                    game,
                    0,
                    out var resolution,
                    out var error,
                    choice: new NavResolveChoice { UseFakeIdSalvage = true }),
                error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(3, player.Cargo);
            Assert.Equal(0, resolution!.WarrantsIssued);
        }

        [Fact]
        public void Carried_Fake_ID_gear_counts_for_may_branch()
        {
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Gear = GearIndex.LoadDefault();
            Assert.True(player.Roster.TryHire(CrewCatalog.LoadDefault().Get("crew_kaylee"), out _));
            player.Gear.Add("gear_bona-fide-credentials");
            Assert.True(GearCarriage.TryAssign(
                game, player, "gear_bona-fide-credentials", "crew_kaylee", out var assignError), assignError);
            Assert.True(MisbehaveResolver.HasTag(game, player, "Fake ID"));

            player.Cargo = 0;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.True(
                resolver.TryResolve(
                    game,
                    0,
                    out var resolution,
                    out var error,
                    choice: new NavResolveChoice { UseFakeIdSalvage = true }),
                error);
            Assert.Equal(3, player.Cargo);
            Assert.Equal(0, resolution!.WarrantsIssued);
        }

        [Fact]
        public void Onboard_Fake_ID_gear_does_not_count()
        {
            // FAQ 4.1 p.2: Onboard Ship Gear may not be used in any way.
            var (game, resolver, player) = GameWithQueuedDraws(1);
            game.Gear = GearIndex.LoadDefault();
            player.Gear.Add("gear_bona-fide-credentials"); // not carried
            Assert.False(MisbehaveResolver.HasTag(game, player, "Fake ID"));

            player.Contraband = 0;
            player.Warrants = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 0, out var resolution, out var error), error);
            Assert.Null(game.PendingChoice);
            Assert.Equal(3, player.Contraband);
            Assert.Equal(1, resolution!.WarrantsIssued);
        }

        [Fact]
        public void More_Trouble_Keep_Flying_option_unchanged()
        {
            var (game, resolver, player) = GameWithQueuedDraws(2);
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get(RegulatedSalvageId));
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(game, 1, out var resolution, out var error), error);
            Assert.Equal(FlightOutcome.KeepFlying, resolution!.Outcome);
            Assert.Equal("More Trouble Than it's Worth", resolution.Option.Name);
            Assert.Equal(0, resolution.GoodsLoaded);
            Assert.Equal(0, resolution.WarrantsIssued);
            Assert.Single(game.PendingNavDraws);
            Assert.Equal(Pelorum, player.SectorId);
        }

        private static (GameState Game, NavResolver Resolver, PlayerState Player) GameWithQueuedDraws(int draws)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(3));
            var player = new PlayerState("p1", "Mal", Pelorum);
            var game = new GameState(map, new[] { player }, decks: decks);
            for (var i = 0; i < draws; i++)
                game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            return (game, new NavResolver(), player);
        }
    }
}
