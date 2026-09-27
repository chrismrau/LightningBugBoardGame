using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Firefly.Core.Data;

namespace Firefly.Core.Cards
{
    public sealed class ScenarioGoal
    {
        public int Number { get; }
        public string Name { get; }
        public string? Type { get; }
        public int Count { get; }
        public int Cash { get; }
        public int GoalTokens { get; }
        public int Pay { get; }
        public bool GrantsGoalToken { get; }
        public string? Location { get; }
        public string? System { get; }
        public IReadOnlyList<string> Contacts { get; }
        public IReadOnlyList<string> RequiresSolidWith { get; }
        /// <summary>Printed Misbehave count before Goal instructions (0 = none).</summary>
        public int Misbehave { get; }
        /// <summary>Printed Skill for the Goal Skill Test, when present.</summary>
        public Skill? Skill { get; }
        public int Target { get; }
        public string? Fail { get; }
        public string? Success { get; }
        public IReadOnlyList<ScenarioGoalBand> Bands { get; }
        public ScenarioGoalBoarding? Boarding { get; }

        public ScenarioGoal(
            int number,
            string name,
            string? type,
            int count = 0,
            int cash = 0,
            int goalTokens = 0,
            int pay = 0,
            bool grantsGoalToken = false,
            string? location = null,
            IReadOnlyList<string>? contacts = null,
            string? system = null,
            int misbehave = 0,
            Skill? skill = null,
            int target = 0,
            string? fail = null,
            string? success = null,
            IReadOnlyList<ScenarioGoalBand>? bands = null,
            IReadOnlyList<string>? requiresSolidWith = null,
            ScenarioGoalBoarding? boarding = null)
        {
            Number = number;
            Name = name;
            Type = type;
            Count = count;
            Cash = cash;
            GoalTokens = goalTokens;
            Pay = pay;
            GrantsGoalToken = grantsGoalToken;
            Location = location;
            System = system;
            Contacts = contacts ?? Array.Empty<string>();
            RequiresSolidWith = requiresSolidWith ?? Array.Empty<string>();
            Misbehave = misbehave;
            Skill = skill;
            Target = target;
            Fail = fail;
            Success = success;
            Bands = bands ?? Array.Empty<ScenarioGoalBand>();
            Boarding = boarding;
        }

        public bool IsWorkable => ScenarioGoalWork.IsWorkable(this);
    }

    /// <summary>
    /// Story-card Choose Havens constraints (Blue Sun / ScenarioCards.json).
    /// </summary>
    public sealed class ChooseHavensSetup
    {
        public bool Required { get; }
        /// <summary>Region name from ScenarioCards.json, e.g. Alliance / Border.</summary>
        public string? Space { get; }
        public IReadOnlyList<string> ExcludeLocations { get; }

        public ChooseHavensSetup(bool required, string? space, IReadOnlyList<string>? excludeLocations = null)
        {
            Required = required;
            Space = space;
            ExcludeLocations = excludeLocations ?? Array.Empty<string>();
        }
    }

    public sealed class ScenarioCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Duration { get; }
        public string? Audience { get; }
        public string? WinType { get; }
        public int WinGoal { get; }
        public int WinCash { get; }
        public int WinCount { get; }
        public int WinGoalTokens { get; }
        public IReadOnlyList<ScenarioGoal> Goals { get; }
        public bool StartingAlertCard { get; }
        public int StartingWarrants { get; }
        public ChooseHavensSetup? ChooseHavens { get; }
        /// <summary>
        /// Blue Sun physical Alliance Alert Tokens on non-Haven Alliance planets
        /// (not the C&amp;P Alliance Alert deck).
        /// </summary>
        public bool AllianceAlertTokensOnNonHavenAlliancePlanets { get; }
        /// <summary>Any Port: Illegal Job completion → Warrant.</summary>
        public bool IncreasedEnforcement { get; }
        /// <summary>Any Port: Cruiser may not move onto a Haven.</summary>
        public bool SafeHarbor { get; }
        /// <summary>Any Port: Haven Buy may combine Fuel + Shore Leave; own Haven freebies.</summary>
        public bool FriendsInLowPlaces { get; }
        /// <summary>
        /// Patience's War: Must be Solid with Patience and Mr. Universe to Work Goals.
        /// </summary>
        public bool GoalWorkRequiresSolidPatienceAndMrUniverse { get; }
        /// <summary>
        /// Patience's War: Warrants from Goals do not drop Solid with Patience / Mr. Universe.
        /// </summary>
        public bool GoalWarrantsPreserveSolidPatienceAndMrUniverse { get; }

        public ScenarioCard(
            string id,
            string name,
            string? duration,
            string? audience,
            string? winType,
            int winGoal = 0,
            int winCash = 0,
            int winCount = 0,
            int winGoalTokens = 0,
            IReadOnlyList<ScenarioGoal>? goals = null,
            bool startingAlertCard = false,
            int startingWarrants = 0,
            ChooseHavensSetup? chooseHavens = null,
            bool allianceAlertTokensOnNonHavenAlliancePlanets = false,
            bool increasedEnforcement = false,
            bool safeHarbor = false,
            bool friendsInLowPlaces = false,
            bool goalWorkRequiresSolidPatienceAndMrUniverse = false,
            bool goalWarrantsPreserveSolidPatienceAndMrUniverse = false)
        {
            Id = id;
            Name = name;
            Duration = duration;
            Audience = audience;
            WinType = winType;
            WinGoal = winGoal;
            WinCash = winCash;
            WinCount = winCount;
            WinGoalTokens = winGoalTokens;
            Goals = goals ?? Array.Empty<ScenarioGoal>();
            StartingAlertCard = startingAlertCard;
            StartingWarrants = startingWarrants;
            ChooseHavens = chooseHavens;
            AllianceAlertTokensOnNonHavenAlliancePlanets = allianceAlertTokensOnNonHavenAlliancePlanets;
            IncreasedEnforcement = increasedEnforcement;
            SafeHarbor = safeHarbor;
            FriendsInLowPlaces = friendsInLowPlaces;
            GoalWorkRequiresSolidPatienceAndMrUniverse = goalWorkRequiresSolidPatienceAndMrUniverse;
            GoalWarrantsPreserveSolidPatienceAndMrUniverse = goalWarrantsPreserveSolidPatienceAndMrUniverse;
        }

        public ScenarioGoal? Goal(int number)
        {
            foreach (var goal in Goals)
            {
                if (goal.Number == number)
                    return goal;
            }
            return null;
        }
    }

    public sealed class ScenarioCatalog
    {
        private readonly Dictionary<string, ScenarioCard> _byId;

        public IReadOnlyDictionary<string, ScenarioCard> Cards => _byId;

        public ScenarioCatalog(IEnumerable<ScenarioCard> cards)
        {
            _byId = new Dictionary<string, ScenarioCard>(StringComparer.Ordinal);
            foreach (var card in cards)
                _byId[card.Id] = card;
        }

        public ScenarioCard Get(string id) => _byId[id];

        public static ScenarioCatalog LoadDefault() => LoadFromFile(GameData.ScenarioCardsPath);

        public static ScenarioCatalog LoadFromFile(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<ScenarioCard>();
            foreach (var card in doc.RootElement.GetProperty("scenarioCards").EnumerateArray())
            {
                string? winType = null;
                var winGoal = 0;
                var winCash = 0;
                var winCount = 0;
                var winTokens = 0;
                if (card.TryGetProperty("win", out var win))
                {
                    if (win.TryGetProperty("type", out var type))
                        winType = type.GetString();
                    winGoal = IntProp(win, "goal");
                    winCash = IntProp(win, "cash");
                    winCount = IntProp(win, "count");
                    winTokens = IntProp(win, "goalTokens");
                }

                var goals = new List<ScenarioGoal>();
                if (card.TryGetProperty("goals", out var goalArr) && goalArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in goalArr.EnumerateArray())
                    {
                        var contacts = new List<string>();
                        if (g.TryGetProperty("contacts", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in cArr.EnumerateArray())
                            {
                                var name = c.GetString();
                                if (!string.IsNullOrWhiteSpace(name))
                                    contacts.Add(name);
                            }
                        }
                        var requiresSolid = new List<string>();
                        if (g.TryGetProperty("requiresSolidWith", out var solidArr)
                            && solidArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var s in solidArr.EnumerateArray())
                            {
                                var name = s.GetString();
                                if (!string.IsNullOrWhiteSpace(name))
                                    requiresSolid.Add(name!);
                            }
                        }
                        var bands = new List<ScenarioGoalBand>();
                        if (g.TryGetProperty("bands", out var bandArr) && bandArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var b in bandArr.EnumerateArray())
                            {
                                bands.Add(new ScenarioGoalBand(
                                    b.TryGetProperty("range", out var rng) ? rng.GetString() ?? "" : "",
                                    b.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : ""));
                            }
                        }
                        Skill? skill = null;
                        if (g.TryGetProperty("skill", out var skEl))
                        {
                            var skName = skEl.GetString();
                            if (ScenarioGoalWork.TryParseSkill(skName, out var parsed))
                                skill = parsed;
                        }
                        ScenarioGoalBoarding? boarding = null;
                        if (g.TryGetProperty("boardingTest", out var boardEl)
                            && boardEl.ValueKind == JsonValueKind.Object)
                        {
                            Skill? boardSkill = null;
                            if (g.TryGetProperty("boardingTestSkill", out var bsEl)
                                && ScenarioGoalWork.TryParseSkill(bsEl.GetString(), out var bsParsed))
                                boardSkill = bsParsed;
                            boarding = new ScenarioGoalBoarding(
                                IntProp(boardEl, "die"),
                                boardEl.TryGetProperty("botched", out var bot) ? bot.GetString() ?? "" : "",
                                boardEl.TryGetProperty("success", out var suc) ? suc.GetString() ?? "" : "",
                                boardSkill);
                        }
                        goals.Add(new ScenarioGoal(
                            IntProp(g, "number"),
                            g.TryGetProperty("name", out var gn) ? gn.GetString() ?? "" : "",
                            g.TryGetProperty("type", out var gt) ? gt.GetString() : null,
                            IntProp(g, "count"),
                            IntProp(g, "cash"),
                            IntProp(g, "goalTokens"),
                            IntProp(g, "pay"),
                            g.TryGetProperty("grantsGoalToken", out var grant) && grant.ValueKind == JsonValueKind.True,
                            g.TryGetProperty("location", out var loc) ? loc.GetString() : null,
                            contacts,
                            g.TryGetProperty("system", out var sys) ? sys.GetString() : null,
                            IntProp(g, "misbehave"),
                            skill,
                            IntProp(g, "target"),
                            g.TryGetProperty("fail", out var fail) ? fail.GetString() : null,
                            g.TryGetProperty("success", out var success) ? success.GetString() : null,
                            bands,
                            requiresSolid,
                            boarding));
                    }
                }

                var startingAlert = false;
                var startingWarrants = 0;
                ChooseHavensSetup? chooseHavens = null;
                var alertTokensOnNonHaven = false;
                if (card.TryGetProperty("setup", out var setupEl))
                {
                    if (setupEl.TryGetProperty("startingAlertCard", out var alertEl)
                        && alertEl.ValueKind == JsonValueKind.True)
                        startingAlert = true;
                    startingWarrants = IntProp(setupEl, "startingWarrants");
                    if (setupEl.TryGetProperty("allianceAlertTokensOnNonHavenAlliancePlanets", out var tok)
                        && tok.ValueKind == JsonValueKind.True)
                        alertTokensOnNonHaven = true;
                    if (setupEl.TryGetProperty("chooseHavens", out var havensEl)
                        && havensEl.ValueKind == JsonValueKind.Object)
                    {
                        var required = havensEl.TryGetProperty("required", out var req)
                            && req.ValueKind == JsonValueKind.True;
                        string? space = havensEl.TryGetProperty("space", out var sp)
                            ? sp.GetString()
                            : null;
                        var excludes = new List<string>();
                        if (havensEl.TryGetProperty("excludeLocations", out var ex)
                            && ex.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var e in ex.EnumerateArray())
                            {
                                var name = e.GetString();
                                if (!string.IsNullOrWhiteSpace(name))
                                    excludes.Add(name!);
                            }
                        }
                        chooseHavens = new ChooseHavensSetup(required, space, excludes);
                    }
                }

                var increasedEnforcement = false;
                var safeHarbor = false;
                var friendsInLowPlaces = false;
                var goalSolidGate = false;
                var goalWarrantPreserve = false;
                if (card.TryGetProperty("specialRules", out var rules)
                    && rules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rules.EnumerateArray())
                    {
                        var ruleName = rule.TryGetProperty("name", out var rn) ? rn.GetString() : null;
                        if (string.Equals(ruleName, "Increased Enforcement", StringComparison.OrdinalIgnoreCase))
                            increasedEnforcement = true;
                        else if (string.Equals(ruleName, "Safe Harbor", StringComparison.OrdinalIgnoreCase))
                            safeHarbor = true;
                        else if (string.Equals(ruleName, "Friends in Low Places", StringComparison.OrdinalIgnoreCase))
                            friendsInLowPlaces = true;
                        else if (string.Equals(ruleName, "Proving Your Worth", StringComparison.OrdinalIgnoreCase))
                        {
                            goalSolidGate = true;
                            goalWarrantPreserve = true;
                        }
                    }
                }

                list.Add(new ScenarioCard(
                    card.GetProperty("id").GetString() ?? "",
                    card.GetProperty("name").GetString() ?? "",
                    card.TryGetProperty("duration", out var d) ? d.GetString() : null,
                    card.TryGetProperty("audience", out var a) ? a.GetString() : null,
                    winType,
                    winGoal,
                    winCash,
                    winCount,
                    winTokens,
                    goals,
                    startingAlert,
                    startingWarrants,
                    chooseHavens,
                    alertTokensOnNonHaven,
                    increasedEnforcement,
                    safeHarbor,
                    friendsInLowPlaces,
                    goalSolidGate,
                    goalWarrantPreserve));
            }
            return new ScenarioCatalog(list);
        }

        private static int IntProp(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var p))
                return 0;
            return p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
        }
    }
}
