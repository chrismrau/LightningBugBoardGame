using System;
using System.Collections.Generic;
using Firefly.Core.Cards;

namespace Firefly.Core.State
{
    public enum CrewOutcome
    {
        None,
        Disgruntled,
        JumpedShip,
        Killed,
        LeaderFiredCrew
    }

    public sealed class CrewMember
    {
        public CrewCard Card { get; }
        public bool Disgruntled { get; set; }
        private bool _wanted;

        public CrewMember(CrewCard card, bool disgruntled = false)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Disgruntled = disgruntled;
            _wanted = card.Wanted;
        }

        public string Id => Card.Id;
        public string Name => Card.Name;
        public bool IsLeader => Card.IsLeader;
        public bool PrintedWanted => Card.Wanted;
        public bool Wanted => _wanted;
        public bool Moral => Card.Moral;
        public bool CanClearWanted => !PrintedWanted && _wanted;

        public bool MarkWanted()
        {
            _wanted = true;
            return true;
        }

        public bool TryClearWanted()
        {
            if (PrintedWanted)
                return false;
            _wanted = false;
            return true;
        }
    }

    public sealed class CrewRoster
    {
        private readonly List<CrewMember> _members = new List<CrewMember>();

        public int MaxCrew { get; set; }

        public CrewRoster(int maxCrew = 6)
        {
            MaxCrew = maxCrew;
        }

        public IReadOnlyList<CrewMember> Members => _members;
        public int Count => _members.Count;

        public int Fight => Sum(m => m.Card.Fight);
        public int Tech => Sum(m => m.Card.Tech);
        public int Talk => Sum(m => m.Card.Talk);
        public int WantedCount => CountWhere(m => m.Wanted);
        public int MoralCount => CountWhere(m => m.Moral);
        public int DisgruntledCount => CountWhere(m => m.Disgruntled);

        public CrewMember? Leader
        {
            get
            {
                foreach (var member in _members)
                {
                    if (member.IsLeader)
                        return member;
                }
                return null;
            }
        }

        public bool TryHire(CrewCard card, out string? error)
        {
            error = null;
            if (card == null)
            {
                error = "Crew card is required.";
                return false;
            }
            if (_members.Count >= MaxCrew)
            {
                error = $"Roster is full ({MaxCrew}).";
                return false;
            }
            _members.Add(new CrewMember(card));
            return true;
        }

        public bool Remove(string crewId)
        {
            var member = Find(crewId);
            if (member == null)
                return false;
            if (member.IsLeader)
                return false;
            return Drop(crewId);
        }

        public bool TryAccept(CrewMember member, out string? error)
        {
            error = null;
            if (member == null)
            {
                error = "Crew is required.";
                return false;
            }
            if (Find(member.Id) != null)
            {
                error = $"{member.Name} is already on this ship.";
                return false;
            }
            if (_members.Count >= MaxCrew)
            {
                error = $"Roster is full ({MaxCrew}).";
                return false;
            }
            _members.Add(member);
            return true;
        }

        public bool TryDismiss(string crewId, out string? error)
        {
            var member = Find(crewId);
            if (member == null)
            {
                error = "That crew is not on the ship.";
                return false;
            }
            if (member.IsLeader)
            {
                error = "Cannot dismiss your Leader.";
                return false;
            }
            Drop(crewId);
            error = null;
            return true;
        }

        public CrewOutcome Kill(CrewMember member)
        {
            if (member == null || Find(member.Id) == null)
                return CrewOutcome.None;
            if (member.IsLeader)
                return Disgruntle(member);
            Drop(member.Id);
            return CrewOutcome.Killed;
        }

        public CrewOutcome Disgruntle(CrewMember member)
        {
            if (member == null || Find(member.Id) == null)
                return CrewOutcome.None;

            if (member.IsLeader)
            {
                if (member.Disgruntled)
                {
                    FireAllExceptLeader();
                    member.Disgruntled = false;
                    return CrewOutcome.LeaderFiredCrew;
                }
                member.Disgruntled = true;
                return CrewOutcome.Disgruntled;
            }

            if (member.Disgruntled)
            {
                Drop(member.Id);
                return CrewOutcome.JumpedShip;
            }
            member.Disgruntled = true;
            return CrewOutcome.Disgruntled;
        }

        public int FireAllExceptLeader()
        {
            var n = 0;
            for (var i = _members.Count - 1; i >= 0; i--)
            {
                if (_members[i].IsLeader)
                    continue;
                _members.RemoveAt(i);
                n++;
            }
            return n;
        }

        public int KillUpTo(int count)
        {
            if (count <= 0)
                return 0;
            var killed = 0;
            for (var i = _members.Count - 1; i >= 0 && killed < count; i--)
            {
                if (Kill(_members[i]) == CrewOutcome.Killed)
                    killed++;
            }
            return killed;
        }

        public int KillAll()
        {
            var killed = 0;
            for (var i = _members.Count - 1; i >= 0; i--)
            {
                if (Kill(_members[i]) == CrewOutcome.Killed)
                    killed++;
            }
            return killed;
        }

        private bool Drop(string crewId)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].Id == crewId)
                {
                    _members.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public CrewMember? RemoveFirstWanted()
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].Wanted && !_members[i].IsLeader)
                {
                    var member = _members[i];
                    _members.RemoveAt(i);
                    return member;
                }
            }
            return null;
        }

        public IReadOnlyList<CrewMember> WantedMembers()
        {
            var list = new List<CrewMember>();
            foreach (var member in _members)
            {
                if (member.Wanted)
                    list.Add(member);
            }
            return list;
        }

        public CrewMember? Find(string crewId)
        {
            foreach (var member in _members)
            {
                if (member.Id == crewId)
                    return member;
            }
            return null;
        }

        public bool HasName(string name)
        {
            foreach (var member in _members)
            {
                if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool MarkWanted(string crewId)
        {
            var member = Find(crewId);
            if (member == null)
                return false;
            return member.MarkWanted();
        }

        public bool TryClearWanted(string crewId)
        {
            var member = Find(crewId);
            if (member == null)
                return false;
            return member.TryClearWanted();
        }

        public int ClearDisgruntled()
        {
            var n = 0;
            foreach (var member in _members)
            {
                if (!member.Disgruntled)
                    continue;
                member.Disgruntled = false;
                n++;
            }
            return n;
        }

        public int DisgruntleMoral() => DisgruntleWhere(m => m.Moral);

        public int DisgruntleWhere(Func<CrewMember, bool> predicate)
        {
            var count = 0;
            var snapshot = new List<CrewMember>(_members);
            foreach (var member in snapshot)
            {
                if (Find(member.Id) == null || !predicate(member))
                    continue;
                var outcome = Disgruntle(member);
                if (outcome != CrewOutcome.None)
                    count++;
            }
            return count;
        }

        public bool HasProfession(string profession)
        {
            foreach (var member in _members)
            {
                if (member.Card.HasProfession(profession))
                    return true;
            }
            return false;
        }

        private int Sum(Func<CrewMember, int> selector)
        {
            var total = 0;
            foreach (var member in _members)
                total += selector(member);
            return total;
        }

        private int CountWhere(Func<CrewMember, bool> predicate)
        {
            var total = 0;
            foreach (var member in _members)
            {
                if (predicate(member))
                    total++;
            }
            return total;
        }
    }
}
