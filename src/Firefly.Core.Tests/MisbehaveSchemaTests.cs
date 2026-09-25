using System;
using System.Collections.Generic;
using System.IO;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.Data;
using Firefly.Core.Map;
using Firefly.Core.State;
using Xunit;

namespace Firefly.Core.Tests
{
    public class MisbehaveSchemaTests
    {
        private const string Santo = "alliance-qin-shi-huang-r1-01";
        private const string Crime = "job_badger_badgers-11-casino-caper";

        [Fact]
        public void Catalog_loads_other_and_skill_thresholds_without_stripping_prose()
        {
            var catalog = MisbehaveCatalog.LoadDefault();
            Assert.True(catalog.TryGet("misbehave_a-formal-affair", out var affair));
            Assert.Equal(5, affair.SkillThresholds!.Talk);
            Assert.Null(affair.Other);
            Assert.Equal("Negotiate 6; 1-5 Attempt Botched. 6+ Proceed.", affair.Options[1].Details);
            // Batch 1 migration: Work the Crowd now has structured skillCheck / bands.
            Assert.True(affair.Options[1].HasStructuredBands);

            Assert.True(catalog.TryGet("misbehave_a-vote-of-no-confidence", out var vote));
            Assert.Equal("No Disgruntled", vote.Other);

            Assert.True(catalog.TryGet("misbehave_an-interesting-day", out var bribesCard));
            Assert.Equal("Bribes", bribesCard.Other);
            Assert.True(bribesCard.Bribes);
            Assert.Contains("Bribes", bribesCard.Options[0].Details, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Catalog_loads_optional_structured_bands_and_effects_from_json()
        {
            var path = Path.Combine(Path.GetTempPath(), "misbehave-schema-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, SampleStructuredJson());
            try
            {
                var catalog = MisbehaveCatalog.LoadFromFile(path);
                var card = catalog.Get("misbehave_schema_sample");
                Assert.Equal("Bribes", card.Other);
                Assert.True(card.Bribes);
                Assert.Equal(5, card.SkillThresholds!.Talk);

                var option = card.Options[0];
                Assert.Equal("Negotiate 6 Bribes; 1-5 Attempt Botched. 6+ Proceed.", option.Details);
                Assert.NotNull(option.SkillCheck);
                Assert.Equal(Skill.Talk, option.SkillCheck!.Skill);
                Assert.Equal(6, option.SkillCheck.Target);
                Assert.True(option.SkillCheck.BribesAllowed);
                Assert.True(option.HasStructuredBands);
                Assert.Equal(2, option.Bands.Count);
                Assert.Equal(MisbehaveLocalEffectType.Botched, option.Bands[0].Effects[0].Local);
                Assert.Equal(MisbehaveLocalEffectType.Proceed, option.Bands[1].Effects[0].Local);
                Assert.Equal("1-5 Attempt Botched", option.Bands[0].Text);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Structured_bands_are_preferred_over_prose_band_text()
        {
            // Prose says botch on 1-5; structured bands botch only on 1-2 and proceed on 3+.
            var card = new MisbehaveCard(
                "misbehave_structured_prefer",
                "Structured Prefer",
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Work the Crowd",
                        "Negotiate 6; 1-5 Attempt Botched. 6+ Proceed.",
                        new MisbehaveSkillCheckSpec(Skill.Talk, 6),
                        new[]
                        {
                            new MisbehaveBand(1, 2, "1-2 Attempt Botched", new[]
                            {
                                MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched)
                            }),
                            new MisbehaveBand(3, null, "3+ Proceed", new[]
                            {
                                MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed)
                            })
                        })
                });

            var game = NewCrimeGame();
            game.CurrentPlayer.TalkBonus = 2;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(card);
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            // Total 4 (dice 2+2) — prose would botch (1-5); structured proceeds (3+).
            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(2, 2)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(4, resolution.SkillCheck!.Total);
            Assert.False(resolution.SkillCheck.Success);
            Assert.NotNull(game.PendingMisbehave);
        }

        [Fact]
        public void Prose_only_cards_still_resolve_via_shared_SkillCheck_BandText()
        {
            // ambush still prose-only after batch 1 (inverted "8+ Fight" syntax deferred).
            var game = NewCrimeGame();
            game.CurrentPlayer.FightBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(game.Misbehave.Catalog.Get("misbehave_ambush"));
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(6, 6)), error);
            Assert.Equal(MisbehaveOutcome.Botched, resolution!.Outcome);
            Assert.False(resolution.Option!.HasStructuredBands);
        }

        [Fact]
        public void Structured_kill_and_warrant_effects_apply_without_prose_match()
        {
            // Details omit Kill/Warrant wording; structured effects alone drive the outcome.
            var card = new MisbehaveCard(
                "misbehave_structured_kill",
                "Structured Kill",
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Ambush Overlay",
                        "Fight 8; structured overlay only.",
                        new MisbehaveSkillCheckSpec(Skill.Fight, 8),
                        new[]
                        {
                            new MisbehaveBand(1, 7, null, new[]
                            {
                                MisbehaveEffect.Of(CardEffectType.KillCrew, 1),
                                MisbehaveEffect.Of(CardEffectType.WarrantIssued),
                                MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched)
                            }),
                            new MisbehaveBand(8, null, null, new[]
                            {
                                MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed)
                            })
                        })
                });

            var game = NewCrimeGame();
            Assert.True(game.CurrentPlayer.Roster.TryHire(game.Crew!.FindByName("Kaylee")!, out _));
            game.CurrentPlayer.FightBonus = 1;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(card);
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice { OptionIndex = 0 },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(1, resolution!.WarrantsIssued);
            Assert.Equal(1, resolution.CrewKilled);
            Assert.Null(game.PendingMisbehave);
            Assert.DoesNotContain(Crime, game.CurrentPlayer.JobHand);
        }

        [Fact]
        public void Card_level_bribes_flag_overlays_structured_skill_check()
        {
            var card = new MisbehaveCard(
                "misbehave_flag_bribes",
                "Flag Bribes",
                "Clubs",
                null,
                null,
                false,
                new[]
                {
                    new MisbehaveOption(
                        "Pay Off",
                        "Negotiate 6; 1-5 Attempt Botched. 6+ Proceed.",
                        new MisbehaveSkillCheckSpec(Skill.Talk, 6),
                        new[]
                        {
                            new MisbehaveBand(1, 5, null, new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched) }),
                            new MisbehaveBand(6, null, null, new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed) })
                        })
                },
                other: "Bribes");

            Assert.True(card.Bribes);
            var game = NewCrimeGame();
            game.CurrentPlayer.Cash = 1000;
            game.CurrentPlayer.TalkBonus = 0;
            StartCrime(game);
            game.Misbehave!.PlaceOnTop(card);
            var resolver = new MisbehaveResolver();
            resolver.DrawNext(game);

            Assert.True(resolver.TryResolve(
                game, "p1",
                new MisbehaveChoice
                {
                    OptionIndex = 0,
                    SkillCheck = new SkillCheckChoice { BribeDollars = 600 }
                },
                out var resolution, out var error,
                ScriptedRng.FromDieFaces(1)), error);
            Assert.Equal(MisbehaveOutcome.Proceed, resolution!.Outcome);
            Assert.Equal(600, resolution.SkillCheck!.BribeDollarsPaid);
            Assert.Equal(400, game.CurrentPlayer.Cash);
        }

        [Fact]
        public void MisbehaveBand_Pick_selects_matching_range()
        {
            var bands = new List<MisbehaveBand>
            {
                new MisbehaveBand(1, 5, "fail", new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Botched) }),
                new MisbehaveBand(6, null, "ok", new[] { MisbehaveEffect.Of(MisbehaveLocalEffectType.Proceed) })
            };
            Assert.Equal("fail", MisbehaveBand.Pick(bands, 5)!.Text);
            Assert.Equal("ok", MisbehaveBand.Pick(bands, 6)!.Text);
            Assert.Equal("ok", MisbehaveBand.Pick(bands, 99)!.Text);
        }

        private static GameState NewCrimeGame()
        {
            var map = SectorMap.LoadFromDirectory(GameData.MapDirectory);
            var player = new PlayerState("p1", "Mal", Santo, cash: 500, fuel: 3);
            var game = new GameState(map, new[] { player });
            game.Jobs = JobCatalog.LoadDefault();
            game.Contacts = ContactCatalog.LoadDefault();
            game.ContactDecks = new ContactDecks(game.Jobs, new SystemRng(1));
            game.Crew = CrewCatalog.LoadDefault();
            game.Gear = GearIndex.LoadDefault();
            var catalog = MisbehaveCatalog.LoadDefault();
            game.Misbehave = new MisbehaveDeck(catalog.Cards.Values, new SystemRng(2), catalog);
            player.JobHand.Add(Crime);
            return game;
        }

        private static void StartCrime(GameState game)
        {
            var work = new WorkAction();
            Assert.True(work.TryWork(game, "p1", Crime, out var start, out var error), error);
            Assert.True(start!.AwaitingMisbehave);
        }

        private static string SampleStructuredJson() =>
            @"{
  ""misbehaveCards"": [{
    ""id"": ""misbehave_schema_sample"",
    ""name"": ""Schema Sample"",
    ""suit"": ""Clubs"",
    ""ace"": null,
    ""options"": [{
      ""name"": ""Work the Crowd"",
      ""details"": ""Negotiate 6 Bribes; 1-5 Attempt Botched. 6+ Proceed."",
      ""skillCheck"": { ""skill"": ""negotiate"", ""target"": 6, ""bribes"": true },
      ""bands"": [
        { ""range"": ""1-5"", ""text"": ""1-5 Attempt Botched"", ""effects"": [{ ""type"": ""botched"" }] },
        { ""min"": 6, ""max"": null, ""text"": ""6+ Proceed"", ""effects"": [{ ""type"": ""proceed"" }] }
      ]
    }],
    ""isReshuffle"": false,
    ""skillThresholds"": { ""fight"": null, ""tech"": null, ""talk"": 5 },
    ""keyword"": null,
    ""other"": ""Bribes""
  }]
}";
    }
}
