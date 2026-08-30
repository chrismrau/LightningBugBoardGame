using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class DiceRoll
    {
        public IReadOnlyList<int> Faces { get; }
        public int Sum { get; }

        public DiceRoll(IReadOnlyList<int> faces)
        {
            Faces = faces;
            var sum = 0;
            for (var i = 0; i < faces.Count; i++)
                sum += faces[i];
            Sum = sum;
        }
    }

    public static class Dice
    {
        public static int D6(IRng rng) => rng.Next(6) + 1;

        public static DiceRoll RollD6(int count, IRng rng)
        {
            if (count < 0)
                count = 0;
            var faces = new int[count];
            for (var i = 0; i < count; i++)
                faces[i] = D6(rng);
            return new DiceRoll(faces);
        }
    }

    public sealed class ScriptedRng : IRng
    {
        private readonly Queue<int> _nextExclusive;

        public ScriptedRng(params int[] zeroBasedDraws)
        {
            _nextExclusive = new Queue<int>(zeroBasedDraws);
        }

        public static ScriptedRng FromDieFaces(params int[] faces)
        {
            var draws = new int[faces.Length];
            for (var i = 0; i < faces.Length; i++)
                draws[i] = faces[i] - 1;
            return new ScriptedRng(draws);
        }

        public int Next(int maxExclusive)
        {
            if (_nextExclusive.Count == 0)
                return 0;
            var value = _nextExclusive.Dequeue();
            if (value < 0)
                return 0;
            if (value >= maxExclusive)
                return maxExclusive - 1;
            return value;
        }
    }
}
