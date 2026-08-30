using System;
using System.Collections.Generic;

namespace Firefly.Core.Map
{
    public sealed class Pathfinder
    {
        private readonly SectorMap _map;

        public Pathfinder(SectorMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public IReadOnlyList<string> Neighbors(string sectorId) =>
            new List<string>(_map.Neighbors(sectorId));

        public IReadOnlyList<string>? ShortestPath(string fromId, string toId)
        {
            if (fromId == toId)
                return new[] { fromId };

            var queue = new Queue<string>();
            var prev = new Dictionary<string, string?>(StringComparer.Ordinal);
            queue.Enqueue(fromId);
            prev[fromId] = null;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in _map.Neighbors(current))
                {
                    if (prev.ContainsKey(next))
                        continue;
                    prev[next] = current;
                    if (next == toId)
                        return Reconstruct(prev, toId);
                    queue.Enqueue(next);
                }
            }

            return null;
        }

        public int? Distance(string fromId, string toId)
        {
            var path = ShortestPath(fromId, toId);
            return path == null ? (int?)null : path.Count - 1;
        }

        public IReadOnlyCollection<string> Reachable(string fromId, int range)
        {
            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range));

            var found = new Dictionary<string, int>(StringComparer.Ordinal) { [fromId] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(fromId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var depth = found[current];
                if (depth == range)
                    continue;
                foreach (var next in _map.Neighbors(current))
                {
                    if (found.ContainsKey(next))
                        continue;
                    found[next] = depth + 1;
                    queue.Enqueue(next);
                }
            }

            return found.Keys;
        }

        public bool CanMosey(string fromId, string toId)
        {
            if (fromId == toId)
                return false;
            foreach (var neighbor in _map.Neighbors(fromId))
            {
                if (neighbor == toId)
                    return true;
            }
            return false;
        }

        public bool CanFullBurn(string fromId, string toId, int driveRange)
        {
            var distance = Distance(fromId, toId);
            return distance.HasValue && distance.Value <= driveRange;
        }

        private static List<string> Reconstruct(Dictionary<string, string?> prev, string end)
        {
            var path = new List<string>();
            string? cursor = end;
            while (cursor != null)
            {
                path.Add(cursor);
                cursor = prev[cursor];
            }
            path.Reverse();
            return path;
        }
    }
}
