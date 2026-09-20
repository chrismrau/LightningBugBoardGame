using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class SetupAndScenarioTests
    {
        [Fact]
        public void Setup_catalog_has_eight_cards()
        {
            var catalog = SetupCatalog.LoadDefault();
            Assert.Equal(8, catalog.Cards.Count);
            var standard = catalog.Get("setup_standard");
            Assert.Equal(3000, standard.StartingCash);
            Assert.Equal(6, standard.StartingFuel);
            Assert.Equal(2, standard.StartingParts);
        }

        [Fact]
        public void Browncoat_way_starts_with_twelve_thousand_and_no_free_fuel()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_the-browncoat-way");
            Assert.Equal(12000, card.StartingCash);
            Assert.Equal(0, card.StartingFuel);
        }

        [Fact]
        public void Scenario_catalog_has_nineteen_cards()
        {
            var catalog = ScenarioCatalog.LoadDefault();
            Assert.Equal(19, catalog.Cards.Count);
            Assert.Equal("First Time in the Captain's Chair", catalog.Get("scenario_first-time-in-the-captains-chair").Name);
            Assert.Equal("firstToCompleteGoal", catalog.Get("scenario_first-time-in-the-captains-chair").WinType);
        }

        [Fact]
        public void Blitz_steps_are_numbered_one_through_eight()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(GameData.SetupCardsPath));
            var blitz = doc.RootElement.GetProperty("setupCards").EnumerateArray()
                .First(c => c.GetProperty("id").GetString() == "setup_the-blitz");
            var orders = blitz.GetProperty("steps").EnumerateArray()
                .Select(s => s.GetProperty("order").GetInt32()).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, orders);
        }

        [Fact]
        public void Scavengers_verse_is_deferred()
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(GameData.ScenarioCardsPath));
            var card = doc.RootElement.GetProperty("scenarioCards").EnumerateArray()
                .First(c => c.GetProperty("id").GetString() == "scenario_the-scavengers-verse");
            Assert.True(card.GetProperty("deferred").GetBoolean());
        }

        [Fact]
        public void Standard_setup_loads_nav_reshuffle_discard_at_three_players()
        {
            var standard = SetupCatalog.LoadDefault().Get("setup_standard");
            Assert.Equal(NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast, standard.NavReshuffleMode);
            Assert.Equal(3, standard.NavReshufflePlayerCountThreshold);
            Assert.False(standard.PlacesNavReshuffleInDiscard(1));
            Assert.False(standard.PlacesNavReshuffleInDiscard(2));
            Assert.True(standard.PlacesNavReshuffleInDiscard(3));
            Assert.True(standard.PlacesNavReshuffleInDiscard(4));
        }

        [Fact]
        public void Clearer_skies_keeps_reshuffle_in_deck_regardless_of_player_count()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_clearer-skies-better-days");
            Assert.Equal(NavReshuffleSetupMode.ShuffleIntoDecksRegardlessOfPlayerCount, card.NavReshuffleMode);
            Assert.False(card.PlacesNavReshuffleInDiscard(1));
            Assert.False(card.PlacesNavReshuffleInDiscard(4));
        }

        [Fact]
        public void Standard_one_player_keeps_reshuffle_in_nav_draw_piles()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(20) });

            AssertReshufflesStayInDraw(game);
        }

        [Fact]
        public void Standard_two_players_keeps_reshuffle_in_nav_draw_piles()
        {
            // GF9 p.4: discard step only applies with 3 or more players.
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            AssertReshufflesStayInDraw(game);
        }

        [Fact]
        public void Standard_three_players_places_reshuffle_in_nav_discard_piles()
        {
            // FAQ 4.1 p.1 / GF9 p.4: "In games with 3 or more players, the reshuffle cards
            // from both Nav Decks are placed in the discard pile at the start of the game"
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(21) });

            AssertReshufflesStartInDiscard(game);
        }

        [Fact]
        public void Standard_four_players_places_reshuffle_in_nav_discard_piles()
        {
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette),
                    new PlayerSeat("p4", "Kaylee", Regina)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(22) });

            AssertReshufflesStartInDiscard(game);
        }

        [Fact]
        public void Clearer_skies_three_players_still_shuffles_reshuffle_into_draw()
        {
            // SetupCards.json: "Shuffle the RESHUFFLE Nav Cards into the Nav Decks before the game begins."
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette)
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_clearer-skies-better-days",
                    DealStartingJobs = false,
                    Rng = new SystemRng(23)
                });

            AssertReshufflesStayInDraw(game);
        }

        [Fact]
        public void Setup_discarded_reshuffle_enters_draw_only_after_first_exhaustion()
        {
            // GF9 p.4 / Director's Cut p.12: "When either Nav Deck becomes exhausted for the
            // first time, reshuffle the discard pile - including the “RESHUFFLE” card."
            // Mid-game exhaustion always includes RESHUFFLE regardless of player count;
            // setup merely parks it in discard until that first reshuffle.
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo),
                    new PlayerSeat("p3", "Wash", Bernadette)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(24) });

            AssertReshufflesStartInDiscard(game);
            AssertMidgameExhaustionIncludesReshuffle(game.Decks!.Alliance, "nav_alliance-cruiser");
            AssertMidgameExhaustionIncludesReshuffle(game.Decks.Border, "nav_reaver-cutter");
            AssertMidgameExhaustionIncludesReshuffle(game.Decks.Rim, "nav_reaver-cutter");
        }

        [Fact]
        public void Standard_one_or_two_players_midgame_exhaustion_still_includes_reshuffle()
        {
            // 1–2 players: RESHUFFLE starts in the draw pile (GF9 p.4 discard step is 3+ only).
            // After it is resolved mid-game, later exhaustion of the draw pile still reshuffles
            // every discard card — including RESHUFFLE — back into the draw pile.
            var game = GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo)
                },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(25) });

            AssertReshufflesStayInDraw(game);
            AssertMidgameResolveThenExhaustionIncludesReshuffle(game.Decks!.Alliance, "nav_alliance-cruiser");
            AssertMidgameResolveThenExhaustionIncludesReshuffle(game.Decks.Border, "nav_reaver-cutter");
            AssertMidgameResolveThenExhaustionIncludesReshuffle(game.Decks.Rim, "nav_reaver-cutter");
        }

        private static void AssertReshufflesStayInDraw(GameState game)
        {
            Assert.NotNull(game.Decks);
            foreach (var deck in new[] { game.Decks!.Alliance, game.Decks.Border, game.Decks.Rim })
            {
                Assert.Equal(60, deck.DrawCount);
                Assert.Equal(0, deck.DiscardCount);
                Assert.True(deck.DrawContainsReshuffle());
            }
        }

        private static void AssertReshufflesStartInDiscard(GameState game)
        {
            Assert.NotNull(game.Decks);
            AssertRegionalReshuffleInDiscard(game.Decks!.Alliance, "nav_alliance-cruiser");
            AssertRegionalReshuffleInDiscard(game.Decks.Border, "nav_reaver-cutter");
            AssertRegionalReshuffleInDiscard(game.Decks.Rim, "nav_reaver-cutter");
        }

        private static void AssertRegionalReshuffleInDiscard(NavDeck deck, string expectedId)
        {
            Assert.Equal(59, deck.DrawCount);
            Assert.Equal(1, deck.DiscardCount);
            Assert.False(deck.DrawContainsReshuffle());
            Assert.True(deck.DiscardPile[0].IsReshuffle);
            Assert.Equal(expectedId, deck.DiscardPile[0].Id);
        }

        /// <summary>
        /// Drain draw into discard (RESHUFFLE already sitting in discard from Set Up), then
        /// draw once — empty-draw path must reshuffle discard including RESHUFFLE.
        /// </summary>
        private static void AssertMidgameExhaustionIncludesReshuffle(NavDeck deck, string reshuffleId)
        {
            Assert.Equal(1, deck.DiscardCount);
            Assert.True(deck.DiscardPile[0].IsReshuffle);
            Assert.Equal(reshuffleId, deck.DiscardPile[0].Id);
            Assert.False(deck.DrawContainsReshuffle());

            var drawBefore = deck.DrawCount;
            for (var i = 0; i < drawBefore; i++)
                deck.ResolveIntoDiscard(deck.Draw());

            Assert.Equal(0, deck.DrawCount);
            Assert.Equal(drawBefore + 1, deck.DiscardCount); // non-RESHUFFLE discards + RESHUFFLE
            var next = deck.Draw();
            Assert.Equal(drawBefore, deck.DrawCount); // 60 reshuffled, one drawn → 59
            Assert.Equal(0, deck.DiscardCount);
            Assert.False(string.IsNullOrWhiteSpace(next.Id));
            Assert.True(next.IsReshuffle || deck.DrawContainsReshuffle());
            Assert.Equal(60, deck.DrawCount + 1); // drawn card + remaining draw = full deck
        }

        /// <summary>
        /// 1–2 player path: pull RESHUFFLE to top, resolve (immediate reshuffle), drain the
        /// new draw pile into discard without drawing RESHUFFLE again by leaving it buried —
        /// then empty-draw reshuffle must still bring RESHUFFLE back.
        /// </summary>
        private static void AssertMidgameResolveThenExhaustionIncludesReshuffle(NavDeck deck, string reshuffleId)
        {
            Assert.True(deck.DrawContainsReshuffle());
            Assert.Equal(0, deck.DiscardCount);

            // Resolve RESHUFFLE mid-game: card enters discard then immediately reshuffles.
            deck.PlaceOnTop(FindAndRemoveReshuffle(deck, reshuffleId));
            var reshuffle = deck.Draw();
            Assert.Equal(reshuffleId, reshuffle.Id);
            Assert.True(reshuffle.IsReshuffle);
            deck.ResolveIntoDiscard(reshuffle);
            Assert.Equal(0, deck.DiscardCount);
            Assert.True(deck.DrawContainsReshuffle());

            // Park RESHUFFLE in discard without resolving (simulates it already being among
            // discards when the draw pile later empties — same inclusion rule as GF9 p.4).
            var parked = FindAndRemoveReshuffle(deck, reshuffleId);
            // Use ResolveIntoDiscard only for non-reshuffle; parking uses the public discard
            // path via MoveReshufflesToDiscardForSetup-equivalent: discard via exhaustion
            // after moving RESHUFFLE aside with PlaceOnTop of a known non-reshuffle drain.
            ParkReshuffleInDiscardWithoutResolving(deck, parked);

            Assert.False(deck.DrawContainsReshuffle());
            Assert.Equal(1, deck.DiscardCount);
            Assert.True(deck.DiscardPile[0].IsReshuffle);

            var drawBefore = deck.DrawCount;
            for (var i = 0; i < drawBefore; i++)
                deck.ResolveIntoDiscard(deck.Draw());

            var next = deck.Draw();
            Assert.Equal(0, deck.DiscardCount);
            Assert.True(next.IsReshuffle || deck.DrawContainsReshuffle());
            Assert.Equal(60, deck.DrawCount + 1);
        }

        private static NavCard FindAndRemoveReshuffle(NavDeck deck, string reshuffleId)
        {
            // Draw until we find it, holding others aside, then restore non-matches on top.
            var held = new List<NavCard>();
            NavCard? found = null;
            while (deck.DrawCount > 0)
            {
                var card = deck.Draw();
                if (found == null && card.Id == reshuffleId)
                {
                    found = card;
                    break;
                }
                held.Add(card);
            }
            Assert.NotNull(found);
            for (var i = held.Count - 1; i >= 0; i--)
                deck.PlaceOnTop(held[i]);
            return found!;
        }

        private static void ParkReshuffleInDiscardWithoutResolving(NavDeck deck, NavCard reshuffle)
        {
            // Mirror Set Up parking: MoveReshufflesToDiscardForSetup puts IsReshuffle in discard
            // without ResolveIntoDiscard. Re-place then move for an isolated mid-game park.
            deck.PlaceOnTop(reshuffle);
            Assert.Equal(1, deck.MoveReshufflesToDiscardForSetup());
        }

        private const string Persephone = "alliance-lux-r1-01";
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Bernadette = "alliance-white-sun-r1-01";
        private const string Regina = "border-georgia-r1-01";

        [Fact]
        public void Standard_setup_wires_misbehave_and_supply_decks()
        {
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            Assert.Equal("setup_standard", game.Setup!.Id);
            Assert.NotNull(game.Misbehave);
            Assert.NotNull(game.MisbehaveCatalog);
            Assert.True(game.Misbehave!.DrawCount >= 70);
            Assert.Equal(0, game.Misbehave.DiscardCount);

            Assert.NotNull(game.Supply);
            Assert.NotNull(game.SupplyDecks);
            foreach (var planet in GameSetup.CoreSupplyPlanets)
            {
                Assert.True(game.SupplyDecks!.TryGet(planet, out var market), planet);
                Assert.Equal(3, market.FaceUp.Count);
                Assert.True(market.Deck.Count > 0, planet);
            }

            Assert.NotNull(game.Decks);
            Assert.NotNull(game.Jobs);
            Assert.NotNull(game.ContactDecks);
            Assert.NotNull(game.Crew);
            Assert.NotNull(game.Leaders);
            Assert.NotNull(game.Ships);
            Assert.NotNull(game.Gear);
            Assert.Equal("ship_bonanza", game.GetPlayer("p1").ShipId);
            Assert.Equal("ship_bonnie-mae", game.GetPlayer("p2").ShipId);
        }

        [Fact]
        public void Starting_leader_is_hired_for_free()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(8) });

            var player = game.CurrentPlayer;
            Assert.Equal("leader_malcolm", player.LeaderId);
            Assert.Equal(1, player.Roster.Count);
            Assert.True(player.Roster.Members[0].IsLeader);
            Assert.Equal("Malcolm", player.Roster.Members[0].Name);
            Assert.Equal(2, player.Fight);
            Assert.Equal(1, player.Talk);
            Assert.True(player.Roster.HasProfession("Pilot"));
            Assert.Equal(3000, player.Cash);
        }

        [Fact]
        public void Leader_can_be_chosen_by_printed_name()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Nandi", Persephone, leaderId: "Nandi") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(9) });

            Assert.Equal("leader_nandi", game.CurrentPlayer.LeaderId);
            Assert.True(game.CurrentPlayer.Roster.HasProfession("Companion"));
        }

        [Fact]
        public void Two_players_cannot_share_a_leader()
        {
            Assert.Throws<ArgumentException>(() =>
                GameSetup.Standard(
                    new PlayerSeat("p1", "Mal", Persephone, leaderId: "leader_malcolm"),
                    new PlayerSeat("p2", "Also Mal", Santo, leaderId: "Malcolm")));
        }

        [Fact]
        public void Standard_setup_gives_printed_starting_supplies_and_a_job_hand()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { Rng = new SystemRng(4) });

            var player = game.CurrentPlayer;
            Assert.Equal(3000, player.Cash);
            Assert.Equal(6, player.Fuel);
            Assert.Equal(2, player.Parts);
            Assert.True(player.JobHand.Count > 0);
            Assert.True(player.JobHand.Count <= player.JobHandLimit);
            Assert.Equal(GameSetup.AllianceCruiserStartSectorId, game.Tokens.AllianceCruiserSectorId);
            Assert.Equal("alliance-white-sun-r1-02", game.Tokens.AllianceCruiserSectorId);
            Assert.True(game.Map.TryGet(game.Tokens.AllianceCruiserSectorId!, out var londinium));
            Assert.Equal("Londinium", londinium.Planet);
            Assert.Null(game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(new[] { GameSetup.CoreReaverCutterStartSectorId }, game.Tokens.ReaverCutterSectorIds);
            Assert.Equal("border-space-r2-06", game.Tokens.ReaverCutterSectorIds[0]);
        }

        [Fact]
        public void Operative_corvette_starts_at_cortex_relay_2_when_in_play()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UseOperativesCorvette = true,
                    Rng = new SystemRng(4)
                });

            Assert.Equal(GameSetup.OperativeCorvetteStartSectorId, game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal("rim-cortex-relay-2-r1-11", game.Tokens.OperativeCorvetteSectorId);
            Assert.True(game.Map.TryGet(game.Tokens.OperativeCorvetteSectorId!, out var relay));
            Assert.Equal("Cortex Relay 2", relay.Planet);
            Assert.Equal(GameSetup.AllianceCruiserStartSectorId, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Blue_sun_places_three_reaver_cutters_on_burnham()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    DealStartingJobs = false,
                    UseBlueSun = true,
                    Rng = new SystemRng(4)
                });

            Assert.Equal(GameSetup.BlueSunReaverCutterStartSectorIds, game.Tokens.ReaverCutterSectorIds);
            Assert.Equal(
                new[] { "rim-burnham-r2-01", "rim-burnham-r2-02", "rim-burnham-r2-03" },
                game.Tokens.ReaverCutterSectorIds);
            foreach (var id in game.Tokens.ReaverCutterSectorIds)
                Assert.True(game.Map.TryGet(id, out _));
            Assert.Null(game.Tokens.OperativeCorvetteSectorId);
            Assert.Equal(GameSetup.AllianceCruiserStartSectorId, game.Tokens.AllianceCruiserSectorId);
        }

        [Fact]
        public void Browncoat_setup_starts_rich_and_dry()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions
                {
                    SetupCardId = "setup_the-browncoat-way",
                    DealStartingJobs = false,
                    Rng = new SystemRng(5)
                });

            Assert.Equal(12000, game.CurrentPlayer.Cash);
            Assert.Equal(0, game.CurrentPlayer.Fuel);
            Assert.Equal(0, game.CurrentPlayer.Parts);
            Assert.Empty(game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Setup_game_can_buy_at_persephone()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(6) });

            Assert.True(game.SupplyDecks!.TryGet("Persephone", out var market));
            var cardId = market.FaceUp[0].Id;
            var buy = new BuyAction();
            Assert.True(buy.TryBuy(game, "p1", new BuyRequest { Fuel = 1, SupplyCardIds = { cardId } }, out var result, out var error), error);
            Assert.Equal(1, result!.FuelBought);
            Assert.Single(result.CardsBought);
            Assert.Equal(3, market.FaceUp.Count);
        }

        [Fact]
        public void Setup_game_can_draw_a_misbehave_card()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Santo) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(7) });

            game.CurrentPlayer.JobHand.Add("job_badger_badgers-11-casino-caper");
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", "job_badger_badgers-11-casino-caper", out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);

            var resolver = new MisbehaveResolver();
            var card = resolver.DrawNext(game);
            Assert.False(string.IsNullOrWhiteSpace(card.Id));
            Assert.NotNull(game.PendingMisbehave!.FaceUp);
        }

        [Fact]
        public void Starting_ship_is_assigned_by_id()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "ship_serenity") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(11) });

            var player = game.CurrentPlayer;
            Assert.Equal("ship_serenity", player.ShipId);
            Assert.Equal(6, player.Roster.MaxCrew);
            Assert.Equal(8, player.CargoHold);
            Assert.Equal(4, player.StashHold);
            Assert.Equal(3, player.UpgradeSlots);
        }

        [Fact]
        public void Starting_ship_can_be_chosen_by_printed_name()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Artful Dodger") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(12) });

            Assert.Equal("ship_artful-dodger", game.CurrentPlayer.ShipId);
            Assert.Equal(7, game.CurrentPlayer.Roster.MaxCrew);
            Assert.Equal(6, game.CurrentPlayer.CargoHold);
        }

        [Fact]
        public void Two_players_cannot_share_a_ship()
        {
            Assert.Throws<ArgumentException>(() =>
                GameSetup.Standard(
                    new PlayerSeat("p1", "Mal", Persephone, shipId: "Serenity"),
                    new PlayerSeat("p2", "Zoe", Santo, shipId: "ship_serenity")));
        }

        [Fact]
        public void Interceptor_starts_with_a_four_crew_limit()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, shipId: "Interceptor") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(13) });

            Assert.Equal("ship_interceptor", game.CurrentPlayer.ShipId);
            Assert.Equal(4, game.CurrentPlayer.Roster.MaxCrew);
            Assert.Equal(4, game.CurrentPlayer.CargoHold);
            Assert.Equal(2, game.CurrentPlayer.UpgradeSlots);
        }

        [Fact]
        public void Seating_leader_zoe_removes_her_crew_cards_from_supply()
        {
            var without = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone, leaderId: "Malcolm") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });
            var withZoe = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Zoe", Persephone, shipId: "Jetwash", leaderId: "Zoe") },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });

            Assert.True(CountCrewNamed(without.SupplyDecks!, "Zoe") >= 1);
            Assert.Equal(0, CountCrewNamed(withZoe.SupplyDecks!, "Zoe"));
            Assert.Equal("leader_zoe_jetwash", withZoe.CurrentPlayer.LeaderId);
        }

        [Fact]
        public void Times_not_catalog_exposes_twenty_game_length_tokens()
        {
            var card = SetupCatalog.LoadDefault().Get("setup_times-not-on-our-side");
            Assert.Equal(20, card.GameLengthTokenCount);
            Assert.Null(SetupCatalog.LoadDefault().Get("setup_standard").GameLengthTokenCount);
        }

        [Fact]
        public void Times_not_setup_starts_with_nineteen_after_opening_discard()
        {
            // SetupCards.json: "Give a pile of 20 Disgruntled Tokens to the player taking the
            // first turn… Each time that player takes a turn, discard one…"
            var game = CreateTimesNotTwoPlayers();

            Assert.Equal(19, game.GameLengthTokensRemaining);
            Assert.Equal(0, game.FirstPlayerIndex);
            Assert.False(game.GameLengthFinalRound);
            Assert.Equal("p1", game.CurrentPlayer.Id);
        }

        [Fact]
        public void Standard_setup_does_not_track_game_length_tokens()
        {
            var game = GameSetup.Standard(
                new PlayerSeat("p1", "Mal", Persephone),
                new PlayerSeat("p2", "Zoe", Santo));

            Assert.Null(game.GameLengthTokensRemaining);
            Assert.False(game.GameLengthFinalRound);
            game.EndTurn();
            Assert.Null(game.GameLengthTokensRemaining);
        }

        [Fact]
        public void Times_not_discards_only_when_first_player_begins_a_turn()
        {
            var game = CreateTimesNotTwoPlayers();
            Assert.Equal(19, game.GameLengthTokensRemaining);

            game.EndTurn(); // p2 begins — no discard
            Assert.Equal("p2", game.CurrentPlayer.Id);
            Assert.Equal(19, game.GameLengthTokensRemaining);

            game.EndTurn(); // p1 begins — discard
            Assert.Equal("p1", game.CurrentPlayer.Id);
            Assert.Equal(18, game.GameLengthTokensRemaining);
            Assert.False(game.GameLengthFinalRound);
        }

        [Fact]
        public void Times_not_final_token_starts_final_round_then_most_credits_wins()
        {
            // "When the final token is discarded, everyone gets one final turn, then the game
            // is over. If time runs out before the Story Card is completed, the player with
            // the most credits wins."
            var game = CreateTimesNotTwoPlayers(scenarioId: "scenario_first-time-in-the-captains-chair");
            game.GameLengthTokensRemaining = 1;
            game.GetPlayer("p1").Cash = 1000;
            game.GetPlayer("p2").Cash = 5000;

            Assert.Equal("p1", game.CurrentPlayer.Id);
            game.GameLengthTokensRemaining = 1;
            game.BeginOpeningTurn(); // first player begins turn → discard final token
            Assert.Equal(0, game.GameLengthTokensRemaining);
            Assert.True(game.GameLengthFinalRound);
            Assert.False(game.GameOver);

            game.EndTurn(); // p2's final turn
            Assert.Equal("p2", game.CurrentPlayer.Id);
            Assert.False(game.GameOver);

            game.EndTurn(); // wrap to p1 → time expired
            Assert.True(game.GameOver);
            Assert.Equal("p2", game.WinnerId);
            Assert.Contains("most credits", game.WinReason!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Times_not_story_win_during_final_round_is_not_overridden()
        {
            var game = CreateTimesNotTwoPlayers(scenarioId: "scenario_first-time-in-the-captains-chair");
            game.GameLengthTokensRemaining = 0;
            game.GameLengthFinalRound = true;
            game.GetPlayer("p1").Cash = 100;
            game.GetPlayer("p2").Cash = 9000;

            Assert.True(WinCheck.TryCompleteGoal(game, game.GetPlayer("p1"), 3, out var error), error);
            Assert.Equal("p1", game.WinnerId);

            game.EndTurn(); // would wrap toward timer end, but story already won
            Assert.Equal("p1", game.WinnerId);
        }

        private static GameState CreateTimesNotTwoPlayers(string? scenarioId = null)
        {
            return GameSetup.Create(
                new[]
                {
                    new PlayerSeat("p1", "Mal", Persephone),
                    new PlayerSeat("p2", "Zoe", Santo)
                },
                new GameSetupOptions
                {
                    SetupCardId = "setup_times-not-on-our-side",
                    ScenarioCardId = scenarioId,
                    DealStartingJobs = false,
                    Rng = new SystemRng(30)
                });
        }

        private static int CountCrewNamed(SupplyDecks decks, string name)
        {
            var n = 0;
            foreach (var market in decks.Markets)
            {
                foreach (var pile in new[] { market.Deck, market.FaceUp, market.Discard })
                {
                    foreach (var card in pile)
                    {
                        if (card.Kind == SupplyKind.Crew &&
                            string.Equals(card.Name, name, System.StringComparison.OrdinalIgnoreCase))
                            n++;
                    }
                }
            }
            return n;
        }
    }
}
