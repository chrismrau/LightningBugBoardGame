using System;

namespace Firefly.Core.State
{
    /// <summary>
    /// Bridgit, Saffron, and Yolonda (printed Yolanda on one card): Deceptive.
    /// If any one of them is hired by anyone, the other two are Removed from Play.
    /// </summary>
    public static class DeceptiveCrew
    {
        public static bool IsDeceptive(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return SamePerson(name, "Bridgit")
                || SamePerson(name, "Saffron")
                || SamePerson(name, "Yolonda");
        }

        public static bool SamePerson(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;
            return IsYolonda(a) && IsYolonda(b);
        }

        public static bool IsYolonda(string? name) =>
            string.Equals(name, "Yolonda", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Yolanda", StringComparison.OrdinalIgnoreCase);

        public static int AfterHired(GameState game, string hiredName)
        {
            if (game == null || !IsDeceptive(hiredName))
                return 0;

            var removed = 0;
            foreach (var player in game.Players)
            {
                removed += player.Roster.RemoveNamedIf(member =>
                    IsDeceptive(member.Name) && !SamePerson(member.Name, hiredName));
            }
            return removed;
        }
    }
}
