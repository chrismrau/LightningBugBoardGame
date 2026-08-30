using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public interface IRng
    {
        int Next(int maxExclusive);
    }

    public sealed class SystemRng : IRng
    {
        private readonly Random _random;

        public SystemRng(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        public static void Shuffle<T>(IList<T> items, IRng rng)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
