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
    /// Nav/Fly polish: Dobson Mole + Emissions Recycler / Full Mess Deck / Long-Range Scanner.
    /// Printed: Supplies.tsv; Blue Sun (Scanner); Esmeralda (Full Mess).
    /// </summary>
    public class DobsonMidFlyPolishTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";
        private const string Londinium = "alliance-white-sun-r1-02";
        private const string Deadwood = "rim-blue-sun-r3-04";

        private static readonly CrewCatalog Crew = CrewCatalog.LoadDefault();
        private static readonly LeaderCatalog Leaders = LeaderCatalog.LoadDefault();
        private static readonly ShipUpgradeIndex Upgrades = ShipUpgradeIndex.LoadDefault();

        private static GameState NewGame(
            string sectorId = Persephone,
            MapTokens? tokens = null,
            bool useAlertTokens = false)
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", sectorId, cash: 1000, fuel: 5);
            var game = new GameState(map, new[] { player }, tokens ?? new MapTokens(Londinium))
            {
                Crew = Crew,
                Leaders = Leaders,
                ShipUpgradeCatalog = Upgrades,
                UseAlertTokens = useAlertTokens
            };
            Assert.True(player.Roster.TryHire(Leaders.Get("leader_malcolm"), out _));
            return game;
        }

        [Fact]
        public void Dobson_ability_is_typed_optional_moveCruiserAsFly()
        {
            var dobson = Crew.Get("crew_dobson_piratesbountyhunters");
            Assert.Contains(
                dobson.Abilities,
                a => a.Type == AbilityTypes.MoveCruiserAsFly && !a.Mandatory);
        }

        [Fact]
        public void Dobson_Fly_moves_Cruiser_to_player_sector_in_Alliance_Space()
        {
            var game = NewGame(Persephone, new MapTokens(Londinium));
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_dobson_piratesbountyhunters"), out _));
            var fly = new FlyAction(new MovementEngine(game.Map));

            Assert.True(fly.TryMoveCruiserWithDobson(game, "p1", out var error), error);
            Assert.Equal(Persephone, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal(Persephone, player.SectorId);
            Assert.True(game.ActionWasUsed(TurnAction.Fly));
            Assert.Null(game.PendingEncounter); // Legal ship — no Contact
        }

        [Fact]
        public void Dobson_Fly_queues_Contact_when_Outlaw()
        {
            var game = NewGame(Persephone, new MapTokens(Londinium));
            var player = game.CurrentPlayer;
            player.Warrants = 1;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_dobson_piratesbountyhunters"), out _));
            var fly = new FlyAction(new MovementEngine(game.Map));

            Assert.True(fly.TryMoveCruiserWithDobson(game, "p1", out var error), error);
            Assert.Equal(TokenKind.AllianceCruiser, game.PendingEncounter);
            Assert.Equal(Persephone, game.PendingEncounterSectorId);
        }

        [Fact]
        public void Dobson_Fly_rejected_outside_Alliance_Space()
        {
            var game = NewGame(Deadwood, new MapTokens(Londinium));
            Assert.True(game.CurrentPlayer.Roster.TryHire(
                Crew.Get("crew_dobson_piratesbountyhunters"), out _));
            var fly = new FlyAction(new MovementEngine(game.Map));

            Assert.False(fly.TryMoveCruiserWithDobson(game, "p1", out var error));
            Assert.Contains("Alliance Space", error);
            Assert.False(game.ActionWasUsed(TurnAction.Fly));
            Assert.Equal(Londinium, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Emissions_Recycler_adds_Full_Burn_range_and_typed_abilities()
        {
            Assert.True(Upgrades.TryGet("ship-upgrade_emissions-recycler_kalidasa", out var entry));
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.FullBurnRangeBonus && a.Amount == 1 && a.Mandatory);
            Assert.Contains(entry.Abilities, a =>
                a.Type == AbilityTypes.TakeFuelOnDoubleBigBlack && !a.Mandatory);

            var game = NewGame();
            var player = game.CurrentPlayer;
            player.DriveRange = 5;
            Assert.Equal(5, player.GetEffectiveDriveRange(game));
            player.ShipUpgrades.Add("ship-upgrade_emissions-recycler_kalidasa");
            Assert.Equal(6, player.GetEffectiveDriveRange(game));
        }

        [Fact]
        public void Emissions_Recycler_two_Big_Black_suspends_then_grants_Fuel()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            player.ShipUpgrades.Add("ship-upgrade_emissions-recycler_kalidasa");
            player.Fuel = 2;
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_the-big-black"));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_the-big-black"));
            game.Decks = decks;
            Assert.True(game.TryConsumeAction(TurnAction.Fly, out _));
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));

            var nav = new NavResolver();
            Assert.True(nav.TryAutoResolve(game, out _, out var error, rng: new SystemRng(1)), error);
            Assert.Equal(1, game.ConsecutiveBigBlackNavThisFly);
            Assert.Null(game.PendingChoice);

            Assert.False(nav.TryAutoResolve(game, out _, out error, rng: new SystemRng(1)));
            Assert.Equal(PendingChoiceKinds.EmissionsFuel, game.PendingChoice!.Kind);
            Assert.Equal(2, player.Fuel);

            Assert.True(FlyAction.TryResumeEmissionsFuel(
                game,
                new ChoiceSubmission { SelectedOptionId = EmissionsFuelOptions.TakeFuel },
                out var took,
                out error), error);
            Assert.True(took);
            Assert.Equal(3, player.Fuel);
            Assert.True(game.EmissionsFuelTakenThisFly);
            Assert.Equal(0, game.ConsecutiveBigBlackNavThisFly);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Emissions_Recycler_scripted_TakeEmissionsFuel_skips_PendingChoice()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            player.ShipUpgrades.Add("ship-upgrade_emissions-recycler_kalidasa");
            player.Fuel = 1;
            var decks = NavCatalog.BuildDecks(GameData.NavCardsPath, new SystemRng(1));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_the-big-black"));
            decks.Alliance.PlaceOnTop(decks.Catalog.Get("nav_the-big-black"));
            game.Decks = decks;
            Assert.True(game.TryConsumeAction(TurnAction.Fly, out _));
            game.PendingNavDraws.Add(new PendingNavDraw(Persephone, NavRegion.Alliance));
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));

            var nav = new NavResolver();
            var choice = new NavResolveChoice { TakeEmissionsFuel = true };
            Assert.True(nav.TryAutoResolve(game, out _, out _, rng: new SystemRng(1), choice: choice));
            Assert.True(nav.TryAutoResolve(
                game, out _, out var error, rng: new SystemRng(1), choice: choice), error);
            Assert.Equal(2, player.Fuel);
            Assert.True(game.EmissionsFuelTakenThisFly);
            Assert.Null(game.PendingChoice);
        }

        [Fact]
        public void Full_Mess_Deck_during_Fly_discards_Cargo_and_clears_Disgruntled()
        {
            var game = NewGame();
            var player = game.CurrentPlayer;
            player.ShipUpgrades.Add("ship-upgrade_full-mess-deck_esmeralda");
            player.Cargo = 2;
            Assert.True(player.Roster.TryHire(Crew.Get("crew_shepherd-book"), out _));
            var book = player.Roster.Find("crew_shepherd-book");
            Assert.NotNull(book);
            book!.Disgruntled = true;
            var fly = new FlyAction(new MovementEngine(game.Map));

            Assert.False(fly.TryFullMessDeck(
                game, "p1", FullMessDiscardKind.Cargo, out _, out var error));
            Assert.Contains("during a Fly Action", error);

            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out error), error);
            Assert.True(fly.TryFullMessDeck(
                game, "p1", FullMessDiscardKind.Cargo, out var cleared, out error), error);
            Assert.Equal(1, cleared);
            Assert.Equal(1, player.Cargo);
            Assert.False(book.Disgruntled);
        }

        [Fact]
        public void Long_Range_Scanner_resolves_adjacent_Alert_Tokens_during_Fly()
        {
            var game = NewGame(Persephone, new MapTokens(Londinium), useAlertTokens: true);
            var player = game.CurrentPlayer;
            player.ShipUpgrades.Add("ship-upgrade_long-range-scanner-array_bluesun");
            var fly = new FlyAction(new MovementEngine(game.Map));

            Assert.True(fly.TryMosey(game, "p1", Pelorum, out _, out var error), error);
            game.Tokens = game.Tokens.PlaceAlertToken(Persephone, AlertTokenKind.Alliance, 1);
            // Die face 6 (Next(6)=5 → D6=6) > token count 1 → ship does not arrive; tokens cleared.
            Assert.True(fly.TryLongRangeScanner(
                game,
                "p1",
                Persephone,
                new FixedRng(5),
                out var resolution,
                out error), error);
            Assert.NotNull(resolution);
            Assert.Equal(0, game.Tokens.RemovableAlertCount(Persephone, AlertTokenKind.Alliance));
            Assert.Equal(Pelorum, player.SectorId);
            Assert.True(game.ActionWasUsed(TurnAction.Fly));
        }

        private sealed class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int Next(int maxExclusive) => _value % maxExclusive;
        }
    }
}
