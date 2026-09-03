using System;
using System.Collections.Generic;

namespace Firefly.Core.Cards
{
    public sealed class CrewCard
    {
        public string Id { get; }
        public string Name { get; }
        public int Fight { get; }
        public int Tech { get; }
        public int Talk { get; }
        public bool Moral { get; }
        public bool Wanted { get; }
        public int Cost { get; }
        public IReadOnlyList<string> Professions { get; }
        public IReadOnlyList<string> Keywords { get; }
        public string? Description { get; }

        public CrewCard(
            string id, string name, int fight, int tech, int talk,
            bool moral, bool wanted, int cost,
            IReadOnlyList<string> professions, string? description,
            IReadOnlyList<string>? keywords = null)
        {
            Id = id; Name = name; Fight = fight; Tech = tech; Talk = talk;
            Moral = moral; Wanted = wanted; Cost = cost;
            Professions = professions ?? Array.Empty<string>();
            Keywords = keywords ?? Array.Empty<string>();
            Description = description;
        }

        public bool HasProfession(string profession)
        {
            foreach (var item in Professions)
            {
                if (string.Equals(item, profession, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
