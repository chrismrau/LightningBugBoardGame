using Firefly.Core.Cards;
using Firefly.Core.Data;
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
    }
}
