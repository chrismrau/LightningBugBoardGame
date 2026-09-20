using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Firefly.Core.Cards
{
    /// <summary>
    /// One FIRST / NEXT (or single) resolution step on a Misbehave option.
    /// Structured fields preferred when present; else prose <see cref="Details"/>.
    /// </summary>
    public sealed class MisbehaveStep
    {
        public string Name { get; }
        public string Details { get; }
        public MisbehaveSkillCheckSpec? SkillCheck { get; }
        public IReadOnlyList<MisbehaveBand> Bands { get; }
        public IReadOnlyList<MisbehaveEffect> Effects { get; }

        public bool HasStructuredBands => Bands.Count > 0;
        public bool HasStructuredEffects => Effects.Count > 0;

        public MisbehaveStep(
            string name,
            string details,
            MisbehaveSkillCheckSpec? skillCheck = null,
            IReadOnlyList<MisbehaveBand>? bands = null,
            IReadOnlyList<MisbehaveEffect>? effects = null)
        {
            Name = name ?? "";
            Details = details ?? "";
            SkillCheck = skillCheck;
            Bands = bands ?? Array.Empty<MisbehaveBand>();
            Effects = effects ?? Array.Empty<MisbehaveEffect>();
        }

        /// <summary>
        /// View this step as an option overlay so the resolver can reuse skill/band paths.
        /// Parent <paramref name="proceedIfTag"/> applies only on the first step.
        /// </summary>
        public MisbehaveOption AsOption(string? proceedIfTag = null) =>
            new MisbehaveOption(Name, Details, SkillCheck, Bands, Effects, proceedIfTag);
    }

    /// <summary>
    /// Split printed FIRST–NEXT Misbehave options into sequential steps.
    /// C&amp;P "2 Steps" cards; prose fallback when JSON has no structured <c>steps</c>.
    /// </summary>
    public static class MisbehaveSteps
    {
        private static readonly Regex StepMarker = new Regex(
            @"\b(?<marker>FIRST|NEXT|First|Next)\b\s*[,:]?\s*",
            RegexOptions.Compiled);

        private static readonly Regex NamedStep = new Regex(
            @"^(?<name>[^:;]+?)\s*:\s*(?<body>.*)$",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Structured steps when present; else parse FIRST/NEXT from <paramref name="option"/> details.
        /// Single-step options return one entry (no mid-card suspend).
        /// </summary>
        public static IReadOnlyList<MisbehaveStep> ForOption(MisbehaveOption option)
        {
            if (option == null)
                return Array.Empty<MisbehaveStep>();
            if (option.Steps.Count > 0)
                return option.Steps;
            return ParseFromDetails(option.Name, option.Details);
        }

        public static IReadOnlyList<MisbehaveStep> ParseFromDetails(string? optionName, string? details)
        {
            details = details ?? "";
            if (string.IsNullOrWhiteSpace(details))
                return new[] { new MisbehaveStep(optionName ?? "", details) };

            var markers = StepMarker.Matches(details);
            if (markers.Count == 0)
                return new[] { new MisbehaveStep(optionName ?? "", details) };

            // Require a leading FIRST/First (or NEXT after a prior step). Lone "Next," mid-sentence
            // without FIRST is treated as single-step prose.
            var firstIsFirst = markers[0].Groups["marker"].Value.StartsWith("First", StringComparison.OrdinalIgnoreCase)
                || markers[0].Groups["marker"].Value.StartsWith("FIRST", StringComparison.OrdinalIgnoreCase);
            if (!firstIsFirst && markers.Count == 1)
                return new[] { new MisbehaveStep(optionName ?? "", details) };

            var steps = new List<MisbehaveStep>(markers.Count);
            for (var i = 0; i < markers.Count; i++)
            {
                var start = markers[i].Index + markers[i].Length;
                var end = i + 1 < markers.Count ? markers[i + 1].Index : details.Length;
                var body = details.Substring(start, end - start).Trim().TrimEnd('.');
                SplitName(body, out var name, out var stepDetails);
                if (string.IsNullOrWhiteSpace(name))
                    name = markers[i].Groups["marker"].Value;
                steps.Add(new MisbehaveStep(name.Trim(), stepDetails.Trim()));
            }

            return steps.Count > 0
                ? steps
                : new[] { new MisbehaveStep(optionName ?? "", details) };
        }

        private static void SplitName(string body, out string name, out string stepDetails)
        {
            var named = NamedStep.Match(body.Trim());
            if (named.Success)
            {
                var candidate = named.Groups["name"].Value.Trim();
                var rest = named.Groups["body"].Value.Trim();
                // Name before skill test (e.g. "Get Past the Front Desk: Negotiate 9…")
                if (SkillCheck.TryParse(rest, out _) || rest.IndexOf("Proceed", StringComparison.OrdinalIgnoreCase) >= 0
                    || rest.IndexOf("Attempt Botched", StringComparison.OrdinalIgnoreCase) >= 0
                    || rest.IndexOf("Continue", StringComparison.OrdinalIgnoreCase) >= 0
                    || rest.IndexOf("Pay $", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    name = candidate;
                    stepDetails = rest;
                    return;
                }
            }

            name = "";
            stepDetails = body;
        }

        /// <summary>
        /// Mid-card FIRST success: band says Continue, or Proceed while more steps remain.
        /// </summary>
        public static bool IsContinueToNext(
            string? effectBand,
            int stepIndex,
            int stepCount)
        {
            if (stepCount <= 1 || stepIndex < 0 || stepIndex >= stepCount - 1)
                return false;
            if (string.IsNullOrWhiteSpace(effectBand))
                return false;
            if (Contains(effectBand, "Attempt Botched"))
                return false;
            return Contains(effectBand, "Continue") || Contains(effectBand, "Proceed");
        }

        public static string StepOptionId(int stepIndex) => "step:" + stepIndex;

        public static bool TryParseStepOptionId(string? id, out int stepIndex)
        {
            stepIndex = 0;
            if (string.IsNullOrWhiteSpace(id))
                return false;
            if (!id.StartsWith("step:", StringComparison.OrdinalIgnoreCase))
                return false;
            return int.TryParse(id.Substring("step:".Length), out stepIndex);
        }

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
