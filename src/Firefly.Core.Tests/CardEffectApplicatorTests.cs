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
    }
}
