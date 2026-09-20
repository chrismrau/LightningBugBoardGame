using System;
using System.Collections.Generic;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.Core.Abilities
{
    /// <summary>
    /// FAQ 4.1 p.2 / GF9 p.14: each Crew/Leader carries 1 Gear by default; unassigned Gear is
    /// Onboard Ship and may not be used. Switching is forbidden during a Work Action.
    /// </summary>
    public static class GearCarriage
    {
        public static bool IsCarried(PlayerState player, string gearId) =>
            player.GearCarriers.TryGetValue(gearId, out var carrier)
            && !string.IsNullOrWhiteSpace(carrier);

        public static string? CarrierOf(PlayerState player, string gearId) =>
            player.GearCarriers.TryGetValue(gearId, out var carrier) ? carrier : null;

        public static IReadOnlyList<string> CarriedBy(PlayerState player, string crewId)
        {
            var list = new List<string>();
            foreach (var pair in player.GearCarriers)
            {
                if (string.Equals(pair.Value, crewId, StringComparison.Ordinal))
                    list.Add(pair.Key);
            }
            return list;
        }

        public static int CountedTowardLimit(GameState game, PlayerState player, string crewId)
        {
            var n = 0;
            foreach (var gearId in CarriedBy(player, crewId))
            {
                if (game.Gear != null && game.Gear.TryGet(gearId, out var gear)
                    && AbilityDispatcher.GearExemptFromLimit(gear))
                    continue;
                n++;
            }
            return n;
        }

        public static bool TryDiscardGear(
            PlayerState player,
            string gearId,
            out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(gearId))
            {
                error = "A gear id is required to discard.";
                return false;
            }
            var removed = false;
            for (var i = 0; i < player.Gear.Count; i++)
            {
                if (string.Equals(player.Gear[i], gearId, StringComparison.OrdinalIgnoreCase))
                {
                    player.Gear.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
            if (!removed)
            {
                error = $"Gear '{gearId}' is not on the ship.";
                return false;
            }
            player.GearCarriers.Remove(gearId);
            return true;
        }

        public static bool TryAssign(
            GameState game,
            PlayerState player,
            string gearId,
            string crewId,
            out string? error)
        {
            error = null;
            if (game.WorkGearLocked)
            {
                // FAQ 4.1 p.2: "The only time you may not switch Gear is during a Work Action."
                error = "Cannot switch Gear during a Work Action.";
                return false;
            }
            if (!player.Gear.Contains(gearId))
            {
                error = "Ship does not own that Gear.";
                return false;
            }
            var member = player.Roster.Find(crewId);
            if (member == null)
            {
                error = "That crew is not on the ship.";
                return false;
            }

            // Move off a previous carrier first (same gear reassignment).
            player.GearCarriers.TryGetValue(gearId, out var previous);
            if (string.Equals(previous, crewId, StringComparison.Ordinal))
                return true;

            var limit = AbilityDispatcher.GearCarryLimit(member);
            var counted = CountedTowardLimit(game, player, crewId);
            var exempt = game.Gear != null
                && game.Gear.TryGet(gearId, out var gear)
                && AbilityDispatcher.GearExemptFromLimit(gear);
            if (!exempt && counted >= limit)
            {
                error = $"{member.Name} already carries the maximum Gear ({limit}).";
                return false;
            }

            player.GearCarriers[gearId] = crewId;
            RefreshSkillBonuses(game, player);
            return true;
        }

        public static bool TryUnassign(
            GameState game,
            PlayerState player,
            string gearId,
            out string? error)
        {
            error = null;
            if (game.WorkGearLocked)
            {
                error = "Cannot switch Gear during a Work Action.";
                return false;
            }
            if (!player.GearCarriers.Remove(gearId))
            {
                error = "That Gear is already Onboard Ship.";
                return false;
            }
            RefreshSkillBonuses(game, player);
            return true;
        }

        /// <summary>
        /// Sync <see cref="PlayerState.FightBonus"/> / Tech / Talk from carried gear only.
        /// Onboard Gear does not contribute (FAQ 4.1 p.2).
        /// </summary>
        public static void RefreshSkillBonuses(GameState game, PlayerState player)
        {
            player.FightBonus = AbilityDispatcher.CarriedSkillAddend(game, player, Skill.Fight);
            player.TechBonus = AbilityDispatcher.CarriedSkillAddend(game, player, Skill.Tech);
            player.TalkBonus = AbilityDispatcher.CarriedSkillAddend(game, player, Skill.Talk);
        }

        /// <summary>
        /// HasTag / Work keyword path: during Work, only carried Gear counts; otherwise still
        /// only carried Gear is usable (Onboard may not be used in any way).
        /// </summary>
        public static bool HasUsableGearTag(GameState game, PlayerState player, string tag)
        {
            if (game.Gear == null || string.IsNullOrWhiteSpace(tag))
                return false;
            foreach (var id in player.Gear)
            {
                if (!IsCarried(player, id))
                    continue;
                if (!game.Gear.TryGet(id, out var gear))
                    continue;
                if (NamesMatch(gear.Name, tag))
                    return true;
                foreach (var keyword in gear.Keywords)
                {
                    if (NamesMatch(keyword, tag))
                        return true;
                }
            }
            return false;
        }

        private static bool NamesMatch(string? value, string tag)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return value.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0
                || tag.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
