using System.Collections.Generic;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    /// <summary>
    /// Shared Nav + Misbehave effect applicator (intersection vocabulary).
    /// Director's Cut p.14 / GF9: Skill Tests list results under the target.
    /// </summary>
    public class CardEffectApplicatorTests
    {
        private const string Persephone = "alliance-lux-r1-01";
        private const string Pelorum = "alliance-lux-r1-02";

        [Fact]
        public void Shared_applicator_issues_warrant_kills_and_takes_cash()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(1) });
            var player = game.Players[0];
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            player.Cash = 0;
            player.Warrants = 0;

            var effects = new List<CardEffect>
            {
                new CardEffect(CardEffectType.WarrantIssued),
                new CardEffect(CardEffectType.KillCrew, 1),
                new CardEffect(CardEffectType.TakeCash, 500)
            };
            var context = new CardEffectContext(CardEffectSource.Misbehave, new KillChoice
            {
                VictimCrewIds = new List<string> { "crew_jayne" }
            });

            Assert.True(CardEffectApplicator.TryApply(
                game, player, effects, ScriptedRng.FromDieFaces(1), context,
                out var result, out var error), error);
            Assert.Equal(1, result.WarrantsIssued);
            Assert.Equal(1, result.CrewKilled);
            Assert.Equal(500, result.CashGained);
            Assert.Equal(1, player.Warrants);
            Assert.Equal(500, player.Cash);
            Assert.Null(player.Roster.Find("crew_jayne"));
        }

        [Fact]
        public void Nav_and_Misbehave_share_CardEffectType_vocabulary()
        {
            var catalog = NavCatalog.LoadFromFile(GameData.NavCardsPath);
            var ghost = catalog.Get("nav_ghost-ship");
            var option = ghost.Options[1];
            Assert.True(option.HasStructuredBands);
            Assert.Equal(Skill.Fight, option.SkillCheck!.Skill);
            Assert.Equal(7, option.SkillCheck.Target);
            Assert.Equal(CardEffectType.KillCrew, option.Bands[0].Effects[0].Type);
            Assert.Equal(2, option.Bands[0].Effects[0].Count);
            Assert.Equal(CardEffectType.TakeCash, option.Bands[1].Effects[0].Type);
            Assert.Equal(1000, option.Bands[1].Effects[0].Count);

            var misbehave = MisbehaveCatalog.LoadDefault().Get("misbehave_keep-a-low-profile");
            var fight = misbehave.Options[0];
            Assert.True(fight.HasStructuredBands);
            Assert.True(fight.Bands[0].Effects[0].Is(CardEffectType.KillCrew));
            Assert.True(fight.Bands[0].Effects[1].Is(MisbehaveLocalEffectType.Botched));
        }

        [Fact]
        public void Ghost_Ship_structured_overlay_still_kills_via_shared_applicator()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(2) });
            game.PendingNavDraws.Add(new PendingNavDraw(Pelorum, NavRegion.Alliance));
            var player = game.CurrentPlayer;
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_jayne"), out _));
            Assert.True(player.Roster.TryHire(game.Crew.Get("crew_zoe"), out _));
            Assert.True(player.Roster.TryHire(game.Crew.Get("crew_kaylee"), out _));
            player.FightBonus = 0;
            game.Decks!.Alliance.PlaceOnTop(game.Decks.Catalog.Get("nav_ghost-ship"));
            var resolver = new NavResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game,
                1,
                out var resolution,
                out var error,
                ScriptedRng.FromDieFaces(1),
                new NavResolveChoice
                {
                    SkillCheck = new SkillCheckChoice { AcceptReroll = false },
                    Kill = new KillChoice
                    {
                        VictimCrewIds = new List<string> { "crew_jayne", "crew_kaylee" }
                    }
                }), error);
            Assert.True(resolution!.Option!.HasStructuredBands);
            Assert.Equal(2, resolution.CrewKilled);
            Assert.Equal(0, resolution.CashGained);
        }

        [Fact]
        public void Adrift_transport_option_effects_disgruntle_moral_via_shared_type()
        {
            var catalog = NavCatalog.LoadFromFile(GameData.NavCardsPath);
            var card = catalog.Get("nav_an-adrift-transport");
            Assert.True(card.Options[1].HasStructuredEffects);
            Assert.Equal(CardEffectType.DisgruntleMoral, card.Options[1].Effects[0].Type);
        }

        [Fact]
        public void ClearDisgruntledMoral_LoadParts_LoadGoods_are_shared_vocabulary()
        {
            Assert.True(CardEffectParsing.TryParseType("clearDisgruntledMoral", out var clear));
            Assert.Equal(CardEffectType.ClearDisgruntledMoral, clear);
            Assert.True(CardEffectParsing.TryParseType("loadParts", out var parts));
            Assert.Equal(CardEffectType.LoadParts, parts);
            Assert.True(CardEffectParsing.TryParseType("loadGoods", out var goods));
            Assert.Equal(CardEffectType.LoadGoods, goods);

            var misbehave = MisbehaveCatalog.LoadDefault();
            Assert.True(CardHasSharedEffect(
                misbehave.Get("misbehave_time-for-some-thrillin-heroics"),
                CardEffectType.ClearDisgruntledMoral));
            Assert.True(CardHasSharedEffect(
                misbehave.Get("misbehave_pushy-salesman"),
                CardEffectType.LoadParts,
                expectedCount: 2));
            Assert.True(CardHasSharedEffect(
                misbehave.Get("misbehave_larcenous-opportunity"),
                CardEffectType.LoadGoods,
                expectedCount: 4));
        }

        private static bool CardHasSharedEffect(
            MisbehaveCard card,
            CardEffectType type,
            int? expectedCount = null)
        {
            foreach (var option in card.Options)
            {
                if (HasShared(option.Effects, type, expectedCount))
                    return true;
                foreach (var band in option.Bands)
                {
                    if (HasShared(band.Effects, type, expectedCount))
                        return true;
                }
                foreach (var step in option.Steps)
                {
                    if (HasShared(step.Effects, type, expectedCount))
                        return true;
                    foreach (var band in step.Bands)
                    {
                        if (HasShared(band.Effects, type, expectedCount))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool HasShared(
            System.Collections.Generic.IReadOnlyList<MisbehaveEffect> effects,
            CardEffectType type,
            int? expectedCount)
        {
            foreach (var effect in effects)
            {
                if (!effect.Is(type))
                    continue;
                if (expectedCount == null || effect.Count == expectedCount.Value)
                    return true;
            }
            return false;
        }

        [Fact]
        public void Shared_applicator_clears_moral_loads_parts_and_goods_mix()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(3) });
            var player = game.Players[0];
            Assert.True(player.Roster.TryHire(game.Crew!.Get("crew_kaylee"), out _));
            player.Roster.Disgruntle(player.Roster.Find("crew_kaylee")!);
            player.Parts = 0;
            player.Fuel = 0;
            player.Cargo = 0;
            player.Contraband = 0;

            var effects = new List<CardEffect>
            {
                new CardEffect(CardEffectType.ClearDisgruntledMoral),
                new CardEffect(CardEffectType.LoadParts, 2),
                new CardEffect(CardEffectType.LoadGoods, 3)
            };
            var context = new CardEffectContext(
                CardEffectSource.Misbehave,
                loadGoodsFuel: 1,
                loadGoodsParts: 1,
                loadGoodsCargo: 1);

            Assert.True(CardEffectApplicator.TryApply(
                game, player, effects, ScriptedRng.FromDieFaces(1), context,
                out var result, out var error), error);
            Assert.Equal(1, result.DisgruntledCleared);
            Assert.False(player.Roster.Find("crew_kaylee")!.Disgruntled);
            Assert.Equal(3, result.PartsLoaded); // 2 LoadParts + 1 from Goods mix
            Assert.Equal(1, result.FuelLoaded);
            Assert.Equal(1, result.CargoLoaded);
            Assert.Equal(3, player.Parts);
            Assert.Equal(1, player.Fuel);
            Assert.Equal(1, player.Cargo);
        }

        [Fact]
        public void LoadParts_skip_if_no_space_preserves_Nav_Take_Parts_semantics()
        {
            var game = GameSetup.Create(
                new[] { new PlayerSeat("p1", "Mal", Persephone) },
                new GameSetupOptions { DealStartingJobs = false, Rng = new SystemRng(4) });
            var player = game.Players[0];
            // Fill every general hold so Parts cannot fit (GF9: cargo fills a whole space).
            player.Cargo = HoldSpace.GeneralSlots(player);
            player.Parts = 0;
            Assert.False(HoldSpace.Fits(player, addParts: 1));

            var effects = new List<CardEffect> { new CardEffect(CardEffectType.LoadParts, 2) };
            var skip = new CardEffectContext(CardEffectSource.Nav, skipLoadIfNoSpace: true);
            Assert.True(CardEffectApplicator.TryApply(
                game, player, effects, ScriptedRng.FromDieFaces(1), skip,
                out var skipped, out var skipErr), skipErr);
            Assert.Equal(0, skipped.PartsLoaded);
            Assert.Equal(0, player.Parts);

            var fail = new CardEffectContext(CardEffectSource.Misbehave);
            Assert.False(CardEffectApplicator.TryApply(
                game, player, effects, ScriptedRng.FromDieFaces(1), fail,
                out _, out var failErr));
            Assert.False(string.IsNullOrWhiteSpace(failErr));
        }
    }
}
