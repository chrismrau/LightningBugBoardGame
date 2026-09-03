using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class MisbehaveOption
    {
        public string Name { get; }
        public string Details { get; }

        public MisbehaveOption(string name, string details)
        {
            Name = name ?? "";
            Details = details ?? "";
        }
    }

    public sealed class MisbehaveCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Suit { get; }
        public string? Ace { get; }
        public string? Keyword { get; }
        public bool IsReshuffle { get; }
        public IReadOnlyList<MisbehaveOption> Options { get; }

        public MisbehaveCard(
            string id,
            string name,
            string? suit,
            string? ace,
            string? keyword,
            bool isReshuffle,
            IReadOnlyList<MisbehaveOption> options)
        {
            Id = id;
            Name = name;
            Suit = suit;
            Ace = ace;
            Keyword = keyword;
            IsReshuffle = isReshuffle;
            Options = options ?? new List<MisbehaveOption>();
        }
    }
}
