using System;

namespace Firefly.Core.Cards
{
    public static class ContactNames
    {
        public static string Normalize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var trimmed = name.Trim().Replace("&", "and");
            while (trimmed.Contains("  "))
                trimmed = trimmed.Replace("  ", " ");
            return trimmed.ToLowerInvariant();
        }

        public static bool EqualsName(string? a, string? b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
    }
}
