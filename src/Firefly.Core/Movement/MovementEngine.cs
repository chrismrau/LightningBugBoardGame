using System;
using System.Collections.Generic;
using Firefly.Core.Map;

namespace Firefly.Core.Movement
{
    /// <summary>
    /// Firefly fly rules:
    /// Mosey — one adjacent sector, no fuel, no Nav card.
    /// Full Burn — up to drive-core range, 1 fuel, one Nav card per sector entered.
    /// The player chooses the path; shortest path is only a helper.
    /// </summary>
    public sealed class MovementEngine
    {
        private readonly SectorMap _map;
        private readonly Pathfinder _paths;

        public MovementEngine(SectorMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _paths = new Pathfinder(map);
        }

        public Pathfinder Pathfinder => _paths;

        public bool TryMosey(string fromId, string toId, MapTokens? tokens, out MovementPlan? plan, out string? error)
        {
            plan = null;
            error = null;

            if (!_map.TryGet(fromId, out _))
            {
                error = $"Unknown origin '{fromId}'.";
                return false;
            }
            if (!_map.TryGet(toId, out _))
            {
                error = $"Unknown destination '{toId}'.";
                return false;
            }
            if (fromId == toId)
            {
                error = "Mosey must enter a different sector.";
                return false;
            }
            if (!_paths.CanMosey(fromId, toId))
            {
                error = $"'{toId}' is not adjacent to '{fromId}'.";
                return false;
            }

            plan = BuildPlan(MovementKind.Mosey, new[] { fromId, toId }, tokens ?? MapTokens.None, fuelCost: 0, drawNav: false);
            return true;
        }

        public bool TryFullBurn(
            IReadOnlyList<string> path,
            int driveRange,
            MapTokens? tokens,
            out MovementPlan? plan,
            out string? error)
        {
            plan = null;
            error = null;

            if (driveRange < 1)
            {
                error = "Drive range must be at least 1.";
                return false;
            }
            if (path == null || path.Count < 2)
            {
                error = "Full Burn path must include origin and at least one entered sector.";
                return false;
            }

            for (var i = 0; i < path.Count; i++)
            {
                if (!_map.TryGet(path[i], out _))
                {
                    error = $"Unknown sector '{path[i]}'.";
                    return false;
                }
            }

            var hops = path.Count - 1;
            if (hops > driveRange)
            {
                error = $"Path length {hops} exceeds drive range {driveRange}.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { path[0] };
            for (var i = 1; i < path.Count; i++)
            {
                if (path[i] == path[i - 1])
                {
                    error = "Path cannot stay in the same sector.";
                    return false;
                }
                if (!IsAdjacent(path[i - 1], path[i]))
                {
                    error = $"'{path[i - 1]}' is not adjacent to '{path[i]}'.";
                    return false;
                }
                if (!seen.Add(path[i]))
                {
                    error = $"Path revisits '{path[i]}'.";
                    return false;
                }
            }

            plan = BuildPlan(MovementKind.FullBurn, path, tokens ?? MapTokens.None, fuelCost: 1, drawNav: true);
            return true;
        }

        public bool TryFullBurnTo(
            string fromId,
            string toId,
            int driveRange,
            MapTokens? tokens,
            out MovementPlan? plan,
            out string? error)
        {
            plan = null;
            var shortest = _paths.ShortestPath(fromId, toId);
            if (shortest == null)
            {
                error = $"No path from '{fromId}' to '{toId}'.";
                return false;
            }
            return TryFullBurn(shortest, driveRange, tokens, out plan, out error);
        }

        public IReadOnlyList<IReadOnlyList<string>> PathsWithinRange(string fromId, string toId, int driveRange, int maxPaths = 32)
        {
            var results = new List<IReadOnlyList<string>>();
            if (!_map.TryGet(fromId, out _) || !_map.TryGet(toId, out _) || driveRange < 1)
                return results;
            if (fromId == toId)
                return results;

            var current = new List<string> { fromId };
            var visiting = new HashSet<string>(StringComparer.Ordinal) { fromId };
            Search(fromId, toId, driveRange, current, visiting, results, maxPaths);
            return results;
        }

        public IReadOnlyCollection<string> MoseyDestinations(string fromId)
        {
            return _map.Neighbors(fromId);
        }

        public IReadOnlyCollection<string> FullBurnDestinations(string fromId, int driveRange)
        {
            var reachable = _paths.Reachable(fromId, driveRange);
            var set = new HashSet<string>(reachable, StringComparer.Ordinal);
            set.Remove(fromId);
            return set;
        }

        private void Search(
            string current,
            string goal,
            int remaining,
            List<string> path,
            HashSet<string> visiting,
            List<IReadOnlyList<string>> results,
            int maxPaths)
        {
            if (results.Count >= maxPaths || remaining == 0)
                return;

            foreach (var next in _map.Neighbors(current))
            {
                if (visiting.Contains(next))
                    continue;
                path.Add(next);
                if (next == goal)
                    results.Add(path.ToArray());
                else
                {
                    visiting.Add(next);
                    Search(next, goal, remaining - 1, path, visiting, results, maxPaths);
                    visiting.Remove(next);
                }
                path.RemoveAt(path.Count - 1);
                if (results.Count >= maxPaths)
                    return;
            }
        }

        private bool IsAdjacent(string a, string b)
        {
            foreach (var n in _map.Neighbors(a))
            {
                if (n == b)
                    return true;
            }
            return false;
        }

        private MovementPlan BuildPlan(
            MovementKind kind,
            IReadOnlyList<string> path,
            MapTokens tokens,
            int fuelCost,
            bool drawNav)
        {
            var steps = new List<MovementStep>(path.Count - 1);
            for (var i = 1; i < path.Count; i++)
            {
                var sector = _map.Get(path[i]);
                steps.Add(new MovementStep(
                    sector.Id,
                    sector.NavRegion,
                    drawNav,
                    tokens.EncounterAt(sector.Id)));
            }

            return new MovementPlan(kind, path[0], path[path.Count - 1], path, steps, fuelCost);
        }
    }
}
