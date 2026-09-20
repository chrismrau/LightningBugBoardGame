using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// GF9 p.4 / Director's Cut p.12 / FAQ 4.1 p.1: Standard Set Up places Alliance Cruiser
    /// and Reaver Cutter (RESHUFFLE) into discard for 3+ players. Some Set Up cards keep them
    /// shuffled into the draw piles regardless of player count.
    /// </summary>
    public enum NavReshuffleSetupMode
    {
        DiscardIfPlayerCountAtLeast,
        ShuffleIntoDecksRegardlessOfPlayerCount
    }

    public sealed class SetupCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Audience { get; }
        public string? TimeModifier { get; }
        public int? StartingCash { get; }
        public int? StartingFuel { get; }
        public int? StartingParts { get; }
        public bool StartingAlertCard { get; }
        public NavReshuffleSetupMode NavReshuffleMode { get; }
        /// <summary>
        /// Used when <see cref="NavReshuffleMode"/> is <see cref="NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast"/>.
        /// Standard Set Up: 3.
        /// </summary>
        public int NavReshufflePlayerCountThreshold { get; }
        /// <summary>
        /// Time's Not on Our Side: pile of Disgruntled Tokens used as Game Length Tokens
        /// (SetupCards.json <c>gameLengthTokens.count</c>). Null when the Setup has no timer.
        /// </summary>
        public int? GameLengthTokenCount { get; }

        public SetupCard(
            string id,
            string name,
            string? audience,
            string? timeModifier,
            int? cash,
            int? fuel,
            int? parts,
            bool startingAlertCard = false,
            NavReshuffleSetupMode navReshuffleMode = NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast,
            int navReshufflePlayerCountThreshold = 3,
            int? gameLengthTokenCount = null)
        {
            Id = id;
            Name = name;
            Audience = audience;
            TimeModifier = timeModifier;
            StartingCash = cash;
            StartingFuel = fuel;
            StartingParts = parts;
            StartingAlertCard = startingAlertCard;
            NavReshuffleMode = navReshuffleMode;
            NavReshufflePlayerCountThreshold = navReshufflePlayerCountThreshold;
            GameLengthTokenCount = gameLengthTokenCount;
        }

        /// <summary>
        /// Whether RESHUFFLE Nav cards start in discard (not resolved / not reshuffled yet).
        /// </summary>
        public bool PlacesNavReshuffleInDiscard(int playerCount) =>
            NavReshuffleMode == NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast
            && playerCount >= NavReshufflePlayerCountThreshold;
    }

    public sealed class SetupCatalog
    {
        private readonly Dictionary<string, SetupCard> _byId;

        public IReadOnlyDictionary<string, SetupCard> Cards => _byId;

        public SetupCatalog(IEnumerable<SetupCard> cards)
        {
            _byId = new Dictionary<string, SetupCard>(StringComparer.Ordinal);
            foreach (var card in cards)
                _byId[card.Id] = card;
        }

        public SetupCard Get(string id) => _byId[id];

        public static SetupCatalog LoadDefault() => LoadFromFile(GameData.SetupCardsPath);

        public static SetupCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<SetupCard>();
            foreach (var card in doc.RootElement.GetProperty("setupCards").EnumerateArray())
            {
                int? cash = null, fuel = null, parts = null;
                if (card.TryGetProperty("startingSupplies", out var supplies))
                {
                    if (supplies.TryGetProperty("cash", out var c)) cash = c.GetInt32();
                    if (supplies.TryGetProperty("fuel", out var f)) fuel = f.GetInt32();
                    if (supplies.TryGetProperty("parts", out var p)) parts = p.GetInt32();
                }
                var startingAlert = card.TryGetProperty("startingAlertCard", out var sa)
                    && sa.ValueKind == JsonValueKind.True;
                ParseNavReshuffle(card, out var reshuffleMode, out var reshuffleThreshold);
                list.Add(new SetupCard(
                    card.GetProperty("id").GetString() ?? "",
                    card.GetProperty("name").GetString() ?? "",
                    card.TryGetProperty("audience", out var a) ? a.GetString() : null,
                    card.TryGetProperty("timeModifier", out var t) ? t.GetString() : null,
                    cash, fuel, parts,
                    startingAlert,
                    reshuffleMode,
                    reshuffleThreshold,
                    ParseGameLengthTokenCount(card)));
            }
            return new SetupCatalog(list);
        }

        private static int? ParseGameLengthTokenCount(JsonElement card)
        {
            if (!card.TryGetProperty("gameLengthTokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
                return null;
            if (!tokens.TryGetProperty("count", out var count) || !count.TryGetInt32(out var n) || n <= 0)
                return null;
            return n;
        }

        private static void ParseNavReshuffle(
            JsonElement card,
            out NavReshuffleSetupMode mode,
            out int playerCountThreshold)
        {
            // Standard / missing: FAQ 4.1 p.1 + GF9 p.4 — discard for 3+ players.
            mode = NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast;
            playerCountThreshold = 3;
            if (!card.TryGetProperty("navReshuffle", out var nav) || nav.ValueKind != JsonValueKind.Object)
                return;

            var modeText = nav.TryGetProperty("mode", out var m) ? m.GetString() : null;
            if (string.Equals(modeText, "shuffleIntoDecksRegardlessOfPlayerCount", StringComparison.Ordinal)
                || string.Equals(modeText, "shuffleCruiserAndCutterRegardlessOfPlayerCount", StringComparison.Ordinal))
            {
                mode = NavReshuffleSetupMode.ShuffleIntoDecksRegardlessOfPlayerCount;
                return;
            }

            if (string.Equals(modeText, "discardIfPlayerCountAtLeast", StringComparison.Ordinal)
                && nav.TryGetProperty("playerCount", out var pc)
                && pc.TryGetInt32(out var threshold)
                && threshold > 0)
            {
                mode = NavReshuffleSetupMode.DiscardIfPlayerCountAtLeast;
                playerCountThreshold = threshold;
            }
        }
    }
}
