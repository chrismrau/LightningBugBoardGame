using System;
using System.Collections.Generic;

namespace Firefly.Core.State
{
    /// <summary>
    /// Shared player-decision wait state on <see cref="GameState"/>.
    /// At most one pending at a time (v1). Actions may suspend mid-resolve by
    /// setting this; play resumes via <see cref="GameState.TrySubmitChoice"/>.
    /// Consumers migrate later — auto-resolve paths stay until each site is wired.
    /// </summary>
    public sealed class PendingChoice
    {
        public string PlayerId { get; }
        /// <summary>
        /// Stable kind id for the waiting decision (see <see cref="PendingChoiceKinds"/>).
        /// </summary>
        public string Kind { get; }
        /// <summary>Optional opaque resume token (card id, job id, sector, etc.).</summary>
        public string? ContextId { get; }
        /// <summary>Legal option ids when the prompt is discrete; null means unconstrained.</summary>
        public IReadOnlyList<string>? Options { get; }
        /// <summary>Optional prompt for tests / future UI.</summary>
        public string? Prompt { get; }

        public PendingChoice(
            string playerId,
            string kind,
            string? contextId = null,
            IReadOnlyList<string>? options = null,
            string? prompt = null)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player id is required.", nameof(playerId));
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Kind is required.", nameof(kind));
            PlayerId = playerId;
            Kind = kind;
            ContextId = contextId;
            Options = options;
            Prompt = prompt;
        }
    }

    /// <summary>
    /// Answer payload for <see cref="GameState.TrySubmitChoice"/>.
    /// Fields are optional; each consumer kind reads what it needs.
    /// </summary>
    public sealed class ChoiceSubmission
    {
        /// <summary>Selected discrete option id when <see cref="PendingChoice.Options"/> is set.</summary>
        public string? SelectedOptionId { get; set; }
        /// <summary>Free-form single value (sector id, crew id, etc.).</summary>
        public string? Value { get; set; }
        /// <summary>Multi-value payload (victim crew ids, goods mix, etc.).</summary>
        public IList<string>? Values { get; set; }
        /// <summary>Numeric amount (bribe dollars, pay cost, fuel count, etc.).</summary>
        public int? Amount { get; set; }
        /// <summary>Binary accept/decline (pay vs Full Stop, Medic Foam, etc.).</summary>
        public bool? Accepted { get; set; }
    }

    /// <summary>
    /// Reserved kind ids for future consumer migrations. Not wired yet.
    /// </summary>
    public static class PendingChoiceKinds
    {
        public const string NavPayOrDecline = "nav-pay-or-decline";
        public const string KillVictim = "kill-victim";
        public const string BribeOrMedFoam = "bribe-or-med-foam";
        public const string MisbehaveOption = "misbehave-option";
        public const string HavenOrRivalSector = "haven-or-rival-sector";
    }
}
