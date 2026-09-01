using System;
using System.Text.RegularExpressions;

namespace Firefly.Core.Cards
{
    public sealed class JobSiteTerms
    {
        public string? Location { get; set; }
        public int Cargo { get; set; }
        public int Contraband { get; set; }
        public int Fugitives { get; set; }
        public int Passengers { get; set; }
        public int Parts { get; set; }
        public bool PassengersUnlimited { get; set; }
        public bool FugitivesUnlimited { get; set; }
        public int Misbehave { get; set; }

        public bool HasGoods =>
            Cargo > 0 || Contraband > 0 || Fugitives > 0 || Passengers > 0 || Parts > 0
            || PassengersUnlimited || FugitivesUnlimited;

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Location) && !HasGoods && Misbehave == 0;
    }

    public static class JobTerms
    {
        private static readonly Regex CountWord = new Regex(
            @"(\d+)\s*(Contra|Cargo|Pass|Fugi|Parts?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string PlaceName(string? location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return string.Empty;
            var comma = location.IndexOf(',');
            var name = comma < 0 ? location.Trim() : location.Substring(0, comma).Trim();
            var slash = name.IndexOf('/');
            if (slash > 0)
                name = name.Substring(slash + 1).Trim();
            return name;
        }

        public static JobSiteTerms ParseSite(string? location, string? details)
        {
            var terms = new JobSiteTerms { Location = location };
            if (string.IsNullOrWhiteSpace(details))
                return terms;

            var text = details;
            var mb = Regex.Match(text, @"Misbehave\s+(\d+)", RegexOptions.IgnoreCase);
            if (mb.Success)
                terms.Misbehave = int.Parse(mb.Groups[1].Value);

            if (Regex.IsMatch(text, @"Pass,\s*no limit", RegexOptions.IgnoreCase))
                terms.PassengersUnlimited = true;
            if (Regex.IsMatch(text, @"Fugi,\s*no limit", RegexOptions.IgnoreCase))
                terms.FugitivesUnlimited = true;

            foreach (Match match in CountWord.Matches(text))
            {
                var n = int.Parse(match.Groups[1].Value);
                switch (match.Groups[2].Value.ToLowerInvariant())
                {
                    case "contra": terms.Contraband = n; break;
                    case "cargo": terms.Cargo = n; break;
                    case "pass": terms.Passengers = n; break;
                    case "fugi": terms.Fugitives = n; break;
                    case "part":
                    case "parts": terms.Parts = n; break;
                }
            }

            return terms;
        }

        public static JobSiteTerms Pickup(JobCard job) => ParseSite(job.PickupLocation, job.PickupDetails);
        public static JobSiteTerms Dropoff(JobCard job) => ParseSite(job.DropoffLocation, job.DropoffDetails);
        public static bool HasDropoff(JobCard job) => !string.IsNullOrWhiteSpace(PlaceName(job.DropoffLocation));
        public static bool PayPerPassenger(JobCard job) =>
            job.PayRaw != null && job.PayRaw.IndexOf("/P", StringComparison.OrdinalIgnoreCase) >= 0;

        public static int ProfessionBonus(JobCard job, Func<string, bool> hasProfession)
        {
            if (string.IsNullOrWhiteSpace(job.Bonus))
                return 0;
            var match = Regex.Match(job.Bonus, @"([A-Za-z][A-Za-z ]+)\s*\+(\d+)");
            if (!match.Success)
                return 0;
            return hasProfession(match.Groups[1].Value.Trim()) ? int.Parse(match.Groups[2].Value) : 0;
        }

        public static bool LocationIsSpecialCase(string? location)
        {
            var name = PlaceName(location);
            return name.Equals("Various", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Any Rival", StringComparison.OrdinalIgnoreCase);
        }
    }
}
