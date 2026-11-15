using Firefly.Core.Abilities;
using Firefly.Core.Actions;
using Firefly.Core.Cards;
using Firefly.Core.State;

namespace Firefly.UI.Services;

/// <summary>Hotseat Work chrome modes (P7).</summary>
public enum WorkUiMode
{
    None,
    Pick
}

public enum WorkAttemptKind
{
    Job,
    MakeWork,
    Datascope,
    Goal,
    /// <summary>PBH Confrontation — board rival then showdown (Most Wanted).</summary>
    BountyConfront,
    /// <summary>PBH Lone Target — showdown vs Supply / Last Seen crew.</summary>
    BountyLone,
    /// <summary>PBH Betrayal — nab own wanted crew (no roll).</summary>
    BountyBetray,
    /// <summary>PBH Deliver bound bounty at printed dropoff.</summary>
    BountyDeliver,
    /// <summary>PBH Jump — board rival transporting a bound bounty.</summary>
    BountyJump
}

/// <summary>One Work attempt the current seat can consider (legal or blocked with kernel-mirrored reason).</summary>
public sealed record WorkAttemptOption(
    WorkAttemptKind Kind,
    string Id,
    string Title,
    string Subtitle,
    bool Enabled,
    string? BlockReason,
    string? PayLabel,
    bool Legal,
    bool Immoral,
    bool IsActive,
    bool PickedUp,
    int? MisbehaveCount,
    WorkSite? Site,
    int? GoalNumber,
    string? RivalId = null,
    string? CrewId = null,
    bool Rescue = false);

public sealed record WorkChromeView(
    WorkUiMode Mode,
    IReadOnlyList<WorkAttemptOption> Attempts,
    string? StatusHint);

/// <summary>Queued / face-up Misbehave for the PendingHost reveal/resolve happy path (P7).</summary>
public sealed record MisbehaveRevealView(
    int Remaining,
    bool HasFaceUp,
    bool IsGoalWork,
    string? JobId,
    int? GoalNumber,
    WorkSite? Site,
    string? CardId,
    string? CardName,
    string? Suit,
    string? Ace,
    string? Keyword,
    string? Other,
    IReadOnlyList<MisbehaveOptionView> Options)
{
    public bool NeedsReveal => !HasFaceUp;
    public bool NeedsResolve => HasFaceUp;
}

public sealed record MisbehaveOptionView(
    int Index,
    string Name,
    string Details);

/// <summary>
/// Builds Work chrome from kernel state — mirrors <see cref="WorkAction.TryWork"/> /
/// Make-Work / Datascope / Goal gates without inventing rules.
/// FAQ 4.1 p.5: Job becomes Active on first Work; bounties do not use Contact Job Work.
/// </summary>
public static class WorkChromeBuilder
{
    public static WorkChromeView Build(GameState game)
    {
        var attempts = ListAttempts(game);
        var enabled = attempts.Count(a => a.Enabled);
        var hint = !game.CanTakeAction(TurnAction.Work, out var workErr)
            ? workErr ?? "Work is not available."
            : game.PendingMisbehave != null
                ? "Finish the pending Misbehave before working again."
                : enabled == 0
                    ? "No legal Work attempts at this sector."
                    : game.BountyDeck != null
                        ? "Pick a legal Job, bounty (Most Wanted), Make-Work, Goal, or Datascope."
                        : "Pick a legal Job, Make-Work, Goal, or Datascope.";

        return new WorkChromeView(WorkUiMode.Pick, attempts, hint);
    }

    public static MisbehaveRevealView FromPending(GameState game)
    {
        var pending = game.PendingMisbehave;
        if (pending == null)
        {
            return new MisbehaveRevealView(
                0, false, false, null, null, null,
                null, null, null, null, null, null,
                Array.Empty<MisbehaveOptionView>());
        }

        var face = pending.FaceUp;
        IReadOnlyList<MisbehaveOptionView> options = Array.Empty<MisbehaveOptionView>();
        if (face != null)
        {
            options = face.Options
                .Select((o, i) => new MisbehaveOptionView(i, o.Name, o.Details))
                .ToArray();
        }

        return new MisbehaveRevealView(
            pending.Remaining,
            face != null,
            pending.IsGoalWork,
            string.IsNullOrEmpty(pending.JobId) ? null : pending.JobId,
            pending.GoalNumber,
            pending.IsGoalWork ? null : pending.Site,
            face?.Id,
            face?.Name,
            face?.Suit,
            face?.Ace,
            face?.Keyword,
            face?.Other,
            options);
    }

    public static IReadOnlyList<WorkAttemptOption> ListAttempts(GameState game)
    {
        var list = new List<WorkAttemptOption>();
        var player = game.CurrentPlayer;
        var canWork = game.CanTakeAction(TurnAction.Work, out var workErr);
        if (game.PendingMisbehave != null)
        {
            canWork = false;
            workErr = "Finish the pending Misbehave before working again.";
        }

        if (game.Jobs != null)
        {
            foreach (var jobId in player.JobHand)
                TryAddJob(game, player, jobId, active: null, canWork, workErr, list);

            foreach (var active in player.ActiveJobs)
                TryAddJob(game, player, active.JobId, active, canWork, workErr, list);
        }

        list.Add(BuildMakeWork(game, player, canWork, workErr));
        list.Add(BuildDatascope(game, player, canWork, workErr));

        if (game.Scenario != null)
        {
            foreach (var goal in game.Scenario.Goals)
            {
                if (!goal.IsWorkable)
                    continue;
                list.Add(BuildGoal(game, player, goal, canWork, workErr));
            }
        }

        if (game.BountyDeck != null)
            AddBountyAttempts(game, player, canWork, workErr, list);

        return list
            .OrderByDescending(a => a.Enabled)
            .ThenBy(a => a.Kind)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// PBH pp.8–12: face-up Most Wanted + own bound deliveries + jumps of rival-bound stacks.
    /// Skills are chosen at submit time (best Fight/Tech/Talk; boarding Tech/Talk).
    /// </summary>
    private static void AddBountyAttempts(
        GameState game,
        PlayerState player,
        bool canWork,
        string? workErr,
        List<WorkAttemptOption> list)
    {
        foreach (var bound in player.BoundBounties)
        {
            if (game.Bounties == null || !game.Bounties.TryResolve(bound.BountyId, out var bounty))
                continue;

            string? block = null;
            var enabled = canWork;
            if (!canWork)
                block = workErr;
            else if (string.IsNullOrWhiteSpace(bounty.DropoffPlanet))
            {
                enabled = false;
                block = "No printed dropoff.";
            }
            else if (!AtBountyPlanet(game, player, bounty.DropoffPlanet))
            {
                enabled = false;
                block = $"Deliver at {bounty.DropoffPlanet}.";
            }

            var count = bound.CrewIds.Count;
            var pay = bounty.Pay * Math.Max(1, count);
            list.Add(new WorkAttemptOption(
                WorkAttemptKind.BountyDeliver,
                bounty.Id,
                $"Deliver · {bounty.Name}",
                $"Bound ×{count} → {bounty.DropoffPlanet}",
                enabled,
                enabled ? null : block,
                $"${pay:N0}",
                Legal: true,
                Immoral: bounty.Immoral,
                IsActive: true,
                PickedUp: true,
                MisbehaveCount: null,
                Site: null,
                GoalNumber: null));
        }

        foreach (var face in game.BountyDeck!.FaceUp)
            AddFaceUpBountyAttempts(game, player, face, canWork, workErr, list);

        foreach (var rival in game.Players)
        {
            if (string.Equals(rival.Id, player.Id, StringComparison.Ordinal))
                continue;
            if (rival.SectorId != player.SectorId)
                continue;

            foreach (var bound in rival.BoundBounties)
            {
                string? block = null;
                var enabled = canWork;
                if (!canWork)
                    block = workErr;

                list.Add(new WorkAttemptOption(
                    WorkAttemptKind.BountyJump,
                    bound.BountyId,
                    $"Jump · {bound.BountyName}",
                    $"Steal from {rival.Name} (same sector)",
                    enabled,
                    enabled ? null : block,
                    null,
                    Legal: true,
                    Immoral: false,
                    IsActive: false,
                    PickedUp: false,
                    MisbehaveCount: null,
                    Site: null,
                    GoalNumber: null,
                    RivalId: rival.Id,
                    Rescue: false));

                list.Add(new WorkAttemptOption(
                    WorkAttemptKind.BountyJump,
                    bound.BountyId,
                    $"Rescue · {bound.BountyName}",
                    $"Free from {rival.Name}; hire + return bounty to deck",
                    enabled,
                    enabled ? null : block,
                    null,
                    Legal: true,
                    Immoral: false,
                    IsActive: false,
                    PickedUp: false,
                    MisbehaveCount: null,
                    Site: null,
                    GoalNumber: null,
                    RivalId: rival.Id,
                    Rescue: true));
            }
        }
    }

    private static void AddFaceUpBountyAttempts(
        GameState game,
        PlayerState player,
        BountyCard bounty,
        bool canWork,
        string? workErr,
        List<WorkAttemptOption> list)
    {
        // Betrayal: wanted name on own roster (non-Leader).
        foreach (var member in player.Roster.Members)
        {
            if (member.IsLeader || !bounty.MatchesCrewName(member.Name))
                continue;

            string? block = null;
            var enabled = canWork;
            if (!canWork)
                block = workErr;

            list.Add(new WorkAttemptOption(
                WorkAttemptKind.BountyBetray,
                bounty.Id,
                $"Betray · {bounty.Name}",
                $"Nab {member.Name} from your own crew (PBH Betrayal)",
                enabled,
                enabled ? null : block,
                $"${bounty.Pay:N0}",
                Legal: true,
                Immoral: bounty.Immoral,
                IsActive: false,
                PickedUp: false,
                MisbehaveCount: null,
                Site: null,
                GoalNumber: null,
                CrewId: member.Id));
        }

        // Confrontation: matching crew on a same-sector rival.
        foreach (var rival in game.Players)
        {
            if (string.Equals(rival.Id, player.Id, StringComparison.Ordinal))
                continue;
            if (rival.SectorId != player.SectorId)
                continue;

            foreach (var member in rival.Roster.Members)
            {
                if (member.IsLeader || !bounty.MatchesCrewName(member.Name))
                    continue;

                string? block = null;
                var enabled = canWork;
                if (!canWork)
                    block = workErr;

                list.Add(new WorkAttemptOption(
                    WorkAttemptKind.BountyConfront,
                    bounty.Id,
                    $"Confront · {bounty.Name}",
                    $"Board {rival.Name} for {member.Name}",
                    enabled,
                    enabled ? null : block,
                    $"${bounty.Pay:N0}",
                    Legal: true,
                    Immoral: bounty.Immoral,
                    IsActive: false,
                    PickedUp: false,
                    MisbehaveCount: null,
                    Site: null,
                    GoalNumber: null,
                    RivalId: rival.Id,
                    CrewId: member.Id));
            }
        }

        // Lone Target: matching crew not hired/bound — at Supply discard / Last Seen.
        if (game.Crew == null)
            return;

        var candidates = new List<CrewCard>();
        foreach (var crew in game.Crew.Cards.Values)
        {
            if (!bounty.MatchesCrewName(crew.Name))
                continue;
            if (HiredOrBoundAnywhere(game, crew.Id))
                continue;
            candidates.Add(crew);
        }

        // Prefer one candidate per bounty name (first non-promo via FindByName when possible).
        if (candidates.Count == 0)
            return;

        var preferred = ExpansionChromeBuilder.ResolveCrewForBounty(game.Crew, bounty);
        if (preferred != null && candidates.Exists(c => c.Id == preferred.Id))
            candidates = new List<CrewCard> { preferred };
        else
            candidates = candidates.Take(1).ToList();

        foreach (var crew in candidates)
        {
            string? block = null;
            var enabled = canWork;
            if (!canWork)
                block = workErr;
            else if (!TryLonePlanet(game, bounty, crew, out var planet, out block))
                enabled = false;
            else if (!AtBountyPlanet(game, player, planet))
            {
                enabled = false;
                block = $"Must be at {planet} (Lone Target).";
            }
            else
                block = null;

            var where = bounty.IsCortex ? "Cortex Alert" : (bounty.PickupPlanet ?? "?");
            list.Add(new WorkAttemptOption(
                WorkAttemptKind.BountyLone,
                bounty.Id,
                $"Lone Target · {bounty.Name}",
                $"{crew.Name} · Last Seen / Supply · {where}",
                enabled,
                enabled ? null : block,
                $"${bounty.Pay:N0}",
                Legal: true,
                Immoral: bounty.Immoral,
                IsActive: false,
                PickedUp: false,
                MisbehaveCount: null,
                Site: null,
                GoalNumber: null,
                CrewId: crew.Id));
        }
    }

    private static bool HiredOrBoundAnywhere(GameState game, string crewId)
    {
        foreach (var p in game.Players)
        {
            if (p.Roster.Find(crewId) != null)
                return true;
            foreach (var bound in p.BoundBounties)
            {
                if (bound.CrewIds.Contains(crewId, StringComparer.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool TryLonePlanet(
        GameState game,
        BountyCard bounty,
        CrewCard crew,
        out string planet,
        out string? error)
    {
        planet = "";
        error = null;
        if (game.SupplyDecks != null)
        {
            foreach (var market in game.SupplyDecks.Markets)
            {
                if (market.Discard.Any(c => c.Id == crew.Id)
                    || market.FaceUp.Any(c => c.Id == crew.Id)
                    || market.Deck.Any(c => c.Id == crew.Id))
                {
                    planet = market.Planet;
                    return true;
                }
            }
        }

        if (!bounty.IsCortex && !string.IsNullOrWhiteSpace(bounty.PickupPlanet))
        {
            planet = bounty.PickupPlanet!;
            return true;
        }

        error = bounty.IsCortex
            ? $"{crew.Name} is not on a Supply market — use confrontation or betrayal."
            : $"Must be at {bounty.PickupPlanet}.";
        return false;
    }

    private static bool AtBountyPlanet(GameState game, PlayerState player, string planet)
    {
        if (planet.Equals("Command Cruiser", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId)
                && game.Tokens.AllianceCruiserSectorId == player.SectorId;
        }

        if (!game.Map.TryGet(player.SectorId, out var sector))
            return false;
        var here = BuyAction.ShopPlanet(sector);
        return string.Equals(here, planet, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sector.DisplayName, planet, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sector.Planet, planet, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Best Fight/Tech/Talk on the seat for Showdown attack.</summary>
    public static Skill BestAttackSkill(PlayerState player)
    {
        var best = Skill.Fight;
        var v = player.Fight;
        if (player.Tech > v)
        {
            best = Skill.Tech;
            v = player.Tech;
        }
        if (player.Talk > v)
            best = Skill.Talk;
        return best;
    }

    /// <summary>Best allowed Boarding skill (Tech / Negotiate) vs defender.</summary>
    public static Skill BestBoardSkill(PlayerState attacker, PlayerState defender)
    {
        var talkOk = BoardingTest.IsAllowedSkill(Skill.Talk, defender, out _);
        var techOk = BoardingTest.IsAllowedSkill(Skill.Tech, defender, out _);
        if (talkOk && (!techOk || attacker.Talk >= attacker.Tech))
            return Skill.Talk;
        if (techOk)
            return Skill.Tech;
        return Skill.Talk;
    }

    /// <summary>Defender showdown skill = their best Fight/Tech/Talk.</summary>
    public static Skill BestDefendSkill(PlayerState defender) => BestAttackSkill(defender);

    private static void TryAddJob(
        GameState game,
        PlayerState player,
        string jobId,
        ActiveJob? active,
        bool canWork,
        string? workErr,
        List<WorkAttemptOption> list)
    {
        if (game.Jobs == null || !game.Jobs.TryGet(jobId, out var job))
            return;

        var pay = job.PayBase != null
            ? $"${job.PayBase:N0}"
            : (string.IsNullOrWhiteSpace(job.PayRaw) ? null : job.PayRaw);

        string? block = null;
        WorkSite? site = null;
        int? misbehave = null;
        var enabled = canWork;

        if (!canWork)
            block = workErr;
        else if (!CanWorkContact(game, player, job, out block))
            enabled = false;
        else if (BountyJobAuthority.IsCoveredByBountyDeck(job, game.Bounties))
        {
            enabled = false;
            block = "Work from the Most Wanted List (Bounty deck), not as a Contact Job.";
        }
        else
        {
            var pickup = JobTerms.Pickup(job);
            var dropoff = JobTerms.Dropoff(job);
            var hasDropoff = JobTerms.HasDropoff(job);

            if (active == null)
            {
                if (player.ActiveJobs.Count >= player.ActiveJobLimit)
                {
                    enabled = false;
                    block = $"Already have {player.ActiveJobLimit} active job(s).";
                }
                else if (JobTerms.IsVarious(pickup.Location))
                {
                    enabled = false;
                    block = "Cortex Alert / Various pickups use the Bounty deck (PBH).";
                }
                else if (JobTerms.IsAnyRival(pickup.Location))
                {
                    enabled = false;
                    block = "Any Rival piracy Jobs use PiracyAction.";
                }
                else if (!AtSite(game, player.SectorId, pickup.Location))
                {
                    enabled = false;
                    block = $"Must be at {JobTerms.PlaceName(pickup.Location)} to start.";
                }
                else if (!CanLoad(player, pickup, out block))
                    enabled = false;
                else
                {
                    site = WorkSite.Pickup;
                    misbehave = EffectiveMisbehave(game, player, job, pickup);
                }
            }
            else if (!active.PickedUp)
            {
                if (!AtSite(game, player.SectorId, pickup.Location))
                {
                    enabled = false;
                    block = $"Must be at {JobTerms.PlaceName(pickup.Location)} to pick up.";
                }
                else if (!CanLoad(player, pickup, out block))
                    enabled = false;
                else
                {
                    site = WorkSite.Pickup;
                    misbehave = EffectiveMisbehave(game, player, job, pickup);
                }
            }
            else if (!hasDropoff)
            {
                enabled = false;
                block = "Already picked up; no drop-off.";
            }
            else if (JobTerms.IsVarious(dropoff.Location))
            {
                enabled = false;
                block = "Cortex Alert / Various drop-offs use the Bounty deck (PBH).";
            }
            else if (JobTerms.IsAnyRival(dropoff.Location))
            {
                enabled = false;
                block = $"Drop-off '{dropoff.Location}' is not handled by Work-as-Job.";
            }
            else if (!AtSite(game, player.SectorId, dropoff.Location))
            {
                enabled = false;
                block = $"Must be at {JobTerms.PlaceName(dropoff.Location)} to complete.";
            }
            else if (!CanUnload(player, active, dropoff, out block))
                enabled = false;
            else
            {
                site = WorkSite.Dropoff;
                misbehave = EffectiveMisbehave(game, player, job, dropoff);
            }
        }

        var phase = active == null
            ? "Hand · start"
            : !active.PickedUp
                ? "Active · pick up"
                : "Active · complete";

        list.Add(new WorkAttemptOption(
            WorkAttemptKind.Job,
            job.Id,
            job.Name,
            $"{job.ContactName} · {phase}",
            enabled,
            enabled ? null : block,
            pay,
            job.Legal,
            job.Immoral,
            active != null,
            active?.PickedUp ?? false,
            misbehave,
            site,
            null));
    }

    private static WorkAttemptOption BuildMakeWork(
        GameState game,
        PlayerState player,
        bool canWork,
        string? workErr)
    {
        string? block = null;
        var enabled = canWork;
        if (!canWork)
            block = workErr;
        else if (!game.Map.TryGet(player.SectorId, out var sector) || !sector.IsPlanetary)
        {
            enabled = false;
            block = "Make-Work requires a Planetary Sector.";
        }

        return new WorkAttemptOption(
            WorkAttemptKind.MakeWork,
            "make-work",
            "Make-Work",
            $"Planetary Work · ${WorkAction.MakeWorkPay}",
            enabled,
            enabled ? null : block,
            $"${WorkAction.MakeWorkPay}",
            Legal: true,
            Immoral: false,
            IsActive: false,
            PickedUp: false,
            MisbehaveCount: 0,
            Site: null,
            GoalNumber: null);
    }

    private static WorkAttemptOption BuildDatascope(
        GameState game,
        PlayerState player,
        bool canWork,
        string? workErr)
    {
        string? block = null;
        var enabled = canWork;
        if (!canWork)
            block = workErr;
        else if (!AbilityDispatcher.HasWorkRevealDiscardSupply(game, player))
        {
            enabled = false;
            block = "Early's Datascope must be carried.";
        }
        else if (!game.Map.TryGet(player.SectorId, out var sector)
                 || string.IsNullOrWhiteSpace(sector.Planet)
                 || game.SupplyDecks == null
                 || !game.SupplyDecks.TryGet(sector.Planet, out _))
        {
            enabled = false;
            block = "Datascope requires a Supply planet sector.";
        }

        return new WorkAttemptOption(
            WorkAttemptKind.Datascope,
            "datascope",
            "Datascope",
            "Reveal Supply into discard",
            enabled,
            enabled ? null : block,
            null,
            Legal: true,
            Immoral: false,
            IsActive: false,
            PickedUp: false,
            MisbehaveCount: 0,
            Site: null,
            GoalNumber: null);
    }

    private static WorkAttemptOption BuildGoal(
        GameState game,
        PlayerState player,
        ScenarioGoal goal,
        bool canWork,
        string? workErr)
    {
        string? block = null;
        var enabled = canWork;
        if (!canWork)
            block = workErr;
        else if (player.CompletedGoals.Contains(goal.Number))
        {
            enabled = false;
            block = $"Goal {goal.Number} is already complete.";
        }
        else if (!PriorGoalsComplete(player, game.Scenario!, goal.Number, out block))
            enabled = false;
        else if (!MeetsSolidGates(game, player, goal, out block))
            enabled = false;
        else if (!AtGoalSite(game, player, goal, out block))
            enabled = false;

        var loc = string.IsNullOrWhiteSpace(goal.Location) ? "?" : goal.Location;
        var mb = goal.Misbehave > 0 ? $" · Misbehave {goal.Misbehave}" : "";
        return new WorkAttemptOption(
            WorkAttemptKind.Goal,
            $"goal:{goal.Number}",
            $"Goal {goal.Number}",
            $"{loc}{mb}",
            enabled,
            enabled ? null : block,
            null,
            Legal: true,
            Immoral: false,
            IsActive: false,
            PickedUp: false,
            MisbehaveCount: goal.Misbehave,
            Site: null,
            GoalNumber: goal.Number);
    }

    private static int EffectiveMisbehave(
        GameState game,
        PlayerState player,
        JobCard job,
        JobSiteTerms terms)
    {
        var misbehave = terms.Misbehave + ActiveAlertRules.ExtraIllegalMisbehave(game, player, job);
        return AbilityDispatcher.ApplyMisbehaveDrawReduce(game, player, misbehave);
    }

    private static bool AtSite(GameState game, string sectorId, string? location)
    {
        if (JobTerms.IsOperativesCorvette(location))
        {
            return game.Tokens.OperativeCorvetteSectorId != null
                && game.Tokens.OperativeCorvetteSectorId == sectorId;
        }

        var name = JobTerms.PlaceName(location);
        return !string.IsNullOrEmpty(name) && game.Map.SatisfiesDestination(sectorId, name);
    }

    private static bool CanWorkContact(GameState game, PlayerState player, JobCard job, out string? error)
    {
        error = null;
        if (game.Contacts != null
            && game.Contacts.TryFindByName(job.ContactName, out var contact)
            && contact.IsHiggins
            && player.Roster.HasName("Jayne"))
        {
            error = "Higgins will not Work while Jayne is in the crew.";
            return false;
        }
        return true;
    }

    private static bool CanLoad(PlayerState player, JobSiteTerms terms, out string? error) =>
        HoldSpace.TryExplain(
            player,
            out error,
            addParts: terms.Parts,
            addCargo: terms.Cargo,
            addContraband: terms.Contraband,
            addPassengers: terms.PassengersUnlimited ? 1 : terms.Passengers,
            addFugitives: terms.FugitivesUnlimited ? 1 : terms.Fugitives);

    private static bool CanUnload(
        PlayerState player,
        ActiveJob active,
        JobSiteTerms terms,
        out string? error)
    {
        error = null;
        var cargo = terms.HasGoods ? terms.Cargo : active.Cargo;
        var contra = terms.HasGoods ? terms.Contraband : active.Contraband;
        var parts = terms.HasGoods ? terms.Parts : active.Parts;
        var fugi = terms.FugitivesUnlimited
            ? active.Fugitives
            : (terms.Fugitives > 0 ? terms.Fugitives : active.Fugitives);
        var pass = terms.PassengersUnlimited
            ? active.Passengers
            : (terms.Passengers > 0 ? terms.Passengers : active.Passengers);
        if (player.Cargo < cargo || player.Contraband < contra || player.Parts < parts
            || player.Fugitives < fugi || player.Passengers < pass)
        {
            error = "Ship is not carrying the goods this job requires.";
            return false;
        }
        return true;
    }

    private static bool PriorGoalsComplete(
        PlayerState player,
        ScenarioCard scenario,
        int goalNumber,
        out string? error)
    {
        error = null;
        foreach (var g in scenario.Goals)
        {
            if (g.Number >= goalNumber)
                continue;
            if (!player.CompletedGoals.Contains(g.Number))
            {
                error = $"Must complete Goal {g.Number} before Goal {goalNumber}.";
                return false;
            }
        }
        return true;
    }

    private static bool MeetsSolidGates(
        GameState game,
        PlayerState player,
        ScenarioGoal goal,
        out string? error)
    {
        error = null;
        foreach (var name in goal.RequiresSolidWith)
        {
            if (!IsSolidWithNameOrId(game, player, name))
            {
                error = $"Must be Solid with {name} to Work this Goal.";
                return false;
            }
        }
        if (game.Scenario != null && game.Scenario.GoalWorkRequiresSolidPatienceAndMrUniverse)
        {
            if (!IsSolidWithNameOrId(game, player, "Patience")
                || !IsSolidWithNameOrId(game, player, "Mr. Universe"))
            {
                error = "Must be Solid with Patience and Mr. Universe to Work Goals.";
                return false;
            }
        }
        return true;
    }

    private static bool IsSolidWithNameOrId(GameState game, PlayerState player, string nameOrId)
    {
        if (ActiveAlertRules.CountsAsSolidWith(game, player, nameOrId))
            return true;
        if (game.Contacts != null && game.Contacts.TryFindByName(nameOrId, out var contact))
            return ActiveAlertRules.CountsAsSolidWith(game, player, contact.Id)
                || ActiveAlertRules.CountsAsSolidWith(game, player, contact.Name);
        return false;
    }

    private static bool AtGoalSite(GameState game, PlayerState player, ScenarioGoal goal, out string? error)
    {
        error = null;
        var location = goal.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            error = "Goal has no location.";
            return false;
        }
        if (location.Equals("Alliance Cruiser", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(game.Tokens.AllianceCruiserSectorId)
                || game.Tokens.AllianceCruiserSectorId != player.SectorId)
            {
                error = "Must be in the Alliance Cruiser's Sector.";
                return false;
            }
            return true;
        }
        if (!game.Map.SatisfiesDestination(player.SectorId, location))
        {
            error = $"Must be at {location} to Work this Goal.";
            return false;
        }
        return true;
    }
}

/// <summary>
/// Routes PendingChoice kinds that belong to Misbehave / Work / Goal resumes.
/// Shared kinds (KillVictim, BribeAmount, …) prefer Misbehave when
/// <see cref="GameState.PendingMisbehave"/> is set; otherwise Nav handles them.
/// </summary>
public static class MisbehaveChoiceResume
{
    /// <summary>
    /// True when this PendingChoice should be handled by Work / Misbehave / Goal resume
    /// rather than <see cref="NavChoiceResume"/>.
    /// </summary>
    public static bool OwnsPendingChoice(GameState game)
    {
        var kind = game.PendingChoice?.Kind;
        if (string.IsNullOrEmpty(kind))
            return false;

        if (string.Equals(kind, PendingChoiceKinds.MakeWorkFugitive, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.FugitiveDeliverBonus, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.GoalPayOrBotch, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MagGrapplerUpgrade, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.DiscardOrLoseSolid, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveOption, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveDiscardRedraw, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveWarrantOrWanted, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveDiscardCargoOrContraband, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveChooseCrew, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveSplitCrew, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.MisbehaveReorder, StringComparison.Ordinal))
            return true;

        if (game.PendingMisbehave != null)
            return true;

        // Goal Work mid-flight (boarding/skill/evade) after Misbehave cleared.
        if (game.PendingGoalWork != null
            && (string.Equals(kind, PendingChoiceKinds.SectorDestination, StringComparison.Ordinal)
                || string.Equals(kind, PendingChoiceKinds.KillVictim, StringComparison.Ordinal)
                || string.Equals(kind, PendingChoiceKinds.BribeAmount, StringComparison.Ordinal)
                || string.Equals(kind, PendingChoiceKinds.MedFoamDiscard, StringComparison.Ordinal)
                || string.Equals(kind, PendingChoiceKinds.DiscardOrLoseSolid, StringComparison.Ordinal)))
            return true;

        return false;
    }

    public static bool TryResume(
        GameState game,
        MisbehaveResolver misbehave,
        WorkAction work,
        GoalWorkAction goals,
        ChoiceSubmission submission,
        IRng rng,
        out string? error)
    {
        error = null;
        var kind = game.PendingChoice?.Kind;
        if (kind == null)
        {
            error = "No choice is pending.";
            return false;
        }

        var playerId = game.PendingChoice!.PlayerId;
        var choice = new MisbehaveChoice();

        if (string.Equals(kind, PendingChoiceKinds.MakeWorkFugitive, StringComparison.Ordinal))
            return work.TryResumeMakeWorkFugitive(game, submission, out _, out error);

        if (string.Equals(kind, PendingChoiceKinds.FugitiveDeliverBonus, StringComparison.Ordinal))
            return work.TryResumeWrightBonus(game, submission, out _, out error);

        if (string.Equals(kind, PendingChoiceKinds.GoalPayOrBotch, StringComparison.Ordinal))
            return goals.TryResumePayOrBotch(game, submission, out _, out error);

        if (string.Equals(kind, PendingChoiceKinds.MagGrapplerUpgrade, StringComparison.Ordinal))
            return MagGrapplerAction.TryResumeUpgradePick(game, submission, out _, out error);

        if (string.Equals(kind, PendingChoiceKinds.DiscardOrLoseSolid, StringComparison.Ordinal))
            return ContactSolidBenefits.TryResumeDiscardOrLoseSolid(game, submission, solidRep: null, out error);

        if (string.Equals(kind, PendingChoiceKinds.SectorDestination, StringComparison.Ordinal)
            && game.PendingGoalWork != null
            && game.PendingMisbehave == null)
            return goals.TryResumeEvade(game, submission, out _, out error);

        if (game.PendingGoalWork != null && game.PendingMisbehave == null)
        {
            if (string.Equals(kind, PendingChoiceKinds.KillVictim, StringComparison.Ordinal))
            {
                if (!CrewKill.TryParseKillCount(game.PendingChoice.ContextId, out var count))
                {
                    error = "Kill-victim context is missing the kill count.";
                    return false;
                }
                var player = game.GetPlayer(playerId);
                if (!CrewKill.TryMergeVictimSubmission(
                        player, count, submission, choice: null, out var merged, out error))
                    return false;
                if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                    return false;
                return goals.TryResume(
                    game, playerId, out _, out error,
                    new GoalWorkChoice { Kill = merged });
            }

            if (string.Equals(kind, PendingChoiceKinds.BribeAmount, StringComparison.Ordinal))
            {
                if (submission.Amount == null)
                {
                    error = "Bribe amount is required.";
                    return false;
                }
                if (!game.TrySubmitChoice(playerId, submission, out _, out error))
                    return false;
                return goals.TryResume(
                    game, playerId, out _, out error,
                    new GoalWorkChoice
                    {
                        SkillCheck = new SkillCheckChoice { BribeDollars = submission.Amount }
                    });
            }
        }

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveReorder, StringComparison.Ordinal))
        {
            // Universal Encyclopedia Deal reorder is Deal-scoped; document as gap when hit mid-Work.
            error =
                $"PendingChoice kind '{kind}' has no Work/Misbehave resume wired in P7.";
            return false;
        }

        if (game.PendingMisbehave == null)
        {
            error = "No Misbehave is pending for this choice.";
            return false;
        }

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveOption, StringComparison.Ordinal))
            return misbehave.TryResumeMisbehaveOption(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveDiscardRedraw, StringComparison.Ordinal))
            return misbehave.TryResumeDalinRedraw(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.KillVictim, StringComparison.Ordinal))
            return misbehave.TryResumeKillVictims(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.BribeAmount, StringComparison.Ordinal))
            return misbehave.TryResumeBribeAmount(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.SkillSwitch, StringComparison.Ordinal))
            return misbehave.TryResumeSkillSwitch(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.SkillReroll, StringComparison.Ordinal))
            return misbehave.TryResumeSkillReroll(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.DiscardToReroll, StringComparison.Ordinal))
            return misbehave.TryResumeDiscardToReroll(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MedFoamDiscard, StringComparison.Ordinal))
            return misbehave.TryResumeMedFoam(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.GoodsMix, StringComparison.Ordinal))
            return misbehave.TryResumeGoodsMix(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveWarrantOrWanted, StringComparison.Ordinal))
            return misbehave.TryResumeWarrantOrWanted(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveDiscardCargoOrContraband, StringComparison.Ordinal))
            return misbehave.TryResumeDiscardCargoOrContraband(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveChooseCrew, StringComparison.Ordinal))
            return misbehave.TryResumeChooseCrew(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.GiftedSkill, StringComparison.Ordinal))
            return misbehave.TryResumeGiftedSkill(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.WantedRollIgnoreGear, StringComparison.Ordinal)
            || string.Equals(kind, PendingChoiceKinds.WantedRollIgnoreCrew, StringComparison.Ordinal))
            return misbehave.TryResumeWantedRollIgnore(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MeadowsRedirect, StringComparison.Ordinal))
            return misbehave.TryResumeMeadowsWantedRoll(game, playerId, submission, choice, out _, out error, rng);

        if (string.Equals(kind, PendingChoiceKinds.MisbehaveSplitCrew, StringComparison.Ordinal))
            return misbehave.TryResumeSplitCrew(game, playerId, submission, choice, out _, out error, rng);

        error =
            $"PendingChoice kind '{kind}' has no Misbehave resume wired in P7; resolve via PendingHost widget or End Turn.";
        return false;
    }
}
