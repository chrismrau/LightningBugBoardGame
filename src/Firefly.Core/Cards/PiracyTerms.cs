using System;
using System.Text.RegularExpressions;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// Printed Piracy Job showdown / pay terms from pickup details + pay raw.
    /// Success / fail are split on '/' (e.g. "6 Goods/Kill 1, Botched").
    /// </summary>
    public sealed class PiracyTerms
    {
        public int MaxStealJobs { get; }
        public int MaxStealGoods { get; }
        public bool StealAllGoods { get; }
        public int KillOnLoss { get; }
        public bool PrintedWarrantOnLoss { get; }
        public bool BotchedOnLoss { get; }
        public int PayPerJob { get; }
        public int PayPerGood { get; }
        public int BoardingTarget { get; }

        public bool StealsJobs => MaxStealJobs > 0;
        public bool StealsGoods => StealAllGoods || MaxStealGoods > 0;

        public PiracyTerms(
            int maxStealJobs,
            int maxStealGoods,
            bool stealAllGoods,
            int killOnLoss,
            bool printedWarrantOnLoss,
            bool botchedOnLoss,
            int payPerJob,
            int payPerGood,
            int boardingTarget)
        {
            MaxStealJobs = maxStealJobs;
            MaxStealGoods = maxStealGoods;
            StealAllGoods = stealAllGoods;
            KillOnLoss = killOnLoss;
            PrintedWarrantOnLoss = printedWarrantOnLoss;
            BotchedOnLoss = botchedOnLoss;
            PayPerJob = payPerJob;
            PayPerGood = payPerGood;
            BoardingTarget = boardingTarget;
        }

        public static PiracyTerms FromJob(JobCard job)
        {
            var details = job.PickupDetails ?? "";
            var slash = details.IndexOf('/');
            var success = slash < 0 ? details : details.Substring(0, slash);
            var failure = slash < 0 ? "" : details.Substring(slash + 1);

            var maxJobs = 0;
            var maxGoods = 0;
            var allGoods = false;
            var jobsMatch = Regex.Match(success, @"(\d+)\s+Inactive\s+Jobs?", RegexOptions.IgnoreCase);
            if (jobsMatch.Success)
                maxJobs = int.Parse(jobsMatch.Groups[1].Value);
            else if (Regex.IsMatch(success, @"All\s+Goods", RegexOptions.IgnoreCase))
                allGoods = true;
            else
            {
                var goodsMatch = Regex.Match(success, @"(\d+)\s+Goods?", RegexOptions.IgnoreCase);
                if (goodsMatch.Success)
                    maxGoods = int.Parse(goodsMatch.Groups[1].Value);
            }

            var kill = 0;
            var killMatch = Regex.Match(failure, @"Kill\s+(\d+)", RegexOptions.IgnoreCase);
            if (killMatch.Success)
                kill = int.Parse(killMatch.Groups[1].Value);
            var warrant = failure.IndexOf("Warrant", StringComparison.OrdinalIgnoreCase) >= 0;
            var botched = failure.IndexOf("Botched", StringComparison.OrdinalIgnoreCase) >= 0;

            var payJob = 0;
            var payGood = 0;
            var raw = job.PayRaw ?? "";
            var perJob = Regex.Match(raw, @"(\d+)\s*/\s*J", RegexOptions.IgnoreCase);
            if (perJob.Success)
                payJob = int.Parse(perJob.Groups[1].Value);
            var perGood = Regex.Match(raw, @"(\d+)\s*/\s*G", RegexOptions.IgnoreCase);
            if (perGood.Success)
                payGood = int.Parse(perGood.Groups[1].Value);

            var boarding = 6;
            if (!string.IsNullOrWhiteSpace(job.Description)
                && SkillCheck.TryParse(job.Description, out var check))
                boarding = check.Target;

            return new PiracyTerms(
                maxJobs, maxGoods, allGoods, kill, warrant, botched, payJob, payGood, boarding);
        }
    }
}
