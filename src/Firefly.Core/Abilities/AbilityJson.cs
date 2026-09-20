using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Firefly.Core.Abilities
{
    /// <summary>Shared JSON → <see cref="AbilityDefinition"/> loader for crew/gear/leaders.</summary>
    public static class AbilityJson
    {
        public static IReadOnlyList<AbilityDefinition> Read(JsonElement parent)
        {
            if (!parent.TryGetProperty("abilities", out var array) || array.ValueKind != JsonValueKind.Array)
                return AbilityDefinition.Empty;

            var list = new List<AbilityDefinition>();
            foreach (var node in array.EnumerateArray())
            {
                if (!node.TryGetProperty("type", out var typeNode))
                    continue;
                var type = typeNode.GetString();
                if (string.IsNullOrWhiteSpace(type))
                    continue;

                var mandatory = true;
                if (node.TryGetProperty("mandatory", out var man) && man.ValueKind == JsonValueKind.False)
                    mandatory = false;

                var amount = 0;
                if (node.TryGetProperty("amount", out var amt) && amt.TryGetInt32(out var a))
                    amount = a;

                string? skill = null;
                if (node.TryGetProperty("skill", out var sk))
                    skill = sk.GetString();

                string? subject = null;
                if (node.TryGetProperty("subject", out var sub))
                    subject = sub.GetString();

                var jobOnly = false;
                if (node.TryGetProperty("jobOnly", out var jo) && jo.ValueKind == JsonValueKind.True)
                    jobOnly = true;

                string? location = null;
                if (node.TryGetProperty("location", out var loc))
                    location = loc.GetString();

                list.Add(new AbilityDefinition(type!, mandatory, amount, skill, subject, jobOnly, location));
            }
            return list;
        }

        /// <summary>
        /// Parse skill icons from gear/crew JSON. Accepts int or documented <c>2D</c>-style strings
        /// (numeric prefix only for addends; discard suffix is not applied here).
        /// </summary>
        public static int ReadSkill(JsonElement skills, string name)
        {
            if (skills.ValueKind != JsonValueKind.Object)
                return 0;
            if (!skills.TryGetProperty(name, out var node) || node.ValueKind == JsonValueKind.Null)
                return 0;
            if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var n))
                return n;
            if (node.ValueKind == JsonValueKind.String)
            {
                var text = node.GetString() ?? "";
                var digits = 0;
                foreach (var ch in text)
                {
                    if (ch < '0' || ch > '9')
                        break;
                    digits = digits * 10 + (ch - '0');
                }
                return digits;
            }
            return 0;
        }
    }
}
