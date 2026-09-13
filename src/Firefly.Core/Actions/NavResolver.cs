using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Firefly.Core.Cards;
using Firefly.Core.Map;
using Firefly.Core.Movement;
using Firefly.Core.State;

namespace Firefly.Core.Actions
{
    public sealed class NavResolution
    {
        public DrawnNav Drawn { get; }
        public NavOption Option { get; }
        public FlightOutcome Outcome { get; }
        public bool Stopped { get; }
        public SkillCheckResult? SkillCheck { get; }
        public ReaverContactResult? ReaverContact { get; }
        public CorvetteContactResult? CorvetteContact { get; }
        public bool ReaverCutterBlockedByCorvette { get; }
        public int CrewKilled { get; }
        public int WarrantsIssued { get; }
        public int FuelLost { get; }
        public int CashGained { get; }
        public int GoodsLoaded { get; }
        public int MoralDisgruntled { get; }
        public int DisgruntledCleared { get; }
        public int ContrabandSeized { get; }
        public int FugitivesSeized { get; }
        public int GoodsSeized { get; }

        public NavResolution(
            DrawnNav drawn,
            NavOption option,
            FlightOutcome outcome,
            bool stopped,
            SkillCheckResult? skillCheck = null,
            ReaverContactResult? reaverContact = null,
            CorvetteContactResult? corvetteContact = null,
            bool reaverCutterBlockedByCorvette = false,
            int crewKilled = 0,
            int warrantsIssued = 0,
            int fuelLost = 0,
            int cashGained = 0,
            int goodsLoaded = 0,
            int moralDisgruntled = 0,
            int disgruntledCleared = 0,
            int contrabandSeized = 0,
            int fugitivesSeized = 0,
            int goodsSeized = 0)
        {
            Drawn = drawn;
            Option = option;
            Outcome = outcome;
            Stopped = stopped;
            SkillCheck = skillCheck;
            ReaverContact = reaverContact;
            CorvetteContact = corvetteContact;
            ReaverCutterBlockedByCorvette = reaverCutterBlockedByCorvette;
            CrewKilled = crewKilled;
            WarrantsIssued = warrantsIssued;
            FuelLost = fuelLost;
            CashGained = cashGained;
            GoodsLoaded = goodsLoaded;
            MoralDisgruntled = moralDisgruntled;
            DisgruntledCleared = disgruntledCleared;
            ContrabandSeized = contrabandSeized;
            FugitivesSeized = fugitivesSeized;
            GoodsSeized = goodsSeized;
        }
    }

    /// <summary>
    /// Optional destinations when resolving Nav options that move tokens or Evade.
    /// </summary>
    public sealed class NavResolveChoice
    {
        public string? EvadeToSectorId { get; set; }
        public string? ReaverCutterToSectorId { get; set; }
        public int ReaverCutterIndex { get; set; }
        public string? OperativeCorvetteToSectorId { get; set; }
        /// <summary>
        /// When Corvette enters a Cutter's Sector, where that Cutter is driven off to (Reaver Starting Zone).
        /// </summary>
        public string? DriveOffReaverToSectorId { get; set; }
        public CorvetteContactChoice? CorvetteContact { get; set; }
        /// <summary>
        /// Destination for Cruiser Patrol / Alliance Entanglements (not the named Alliance Cruiser snap).
        /// Patrol: chosen by the player to the right. Entanglements: chosen by the drawer.
        /// </summary>
        public string? AllianceCruiserToSectorId { get; set; }
        /// <summary>
        /// Crew discarded when an option Requires Discarding 1 Crew (not the Leader).
        /// </summary>
        public string? DiscardCrewId { get; set; }
        /// <summary>
        /// When a skill-check band or option Loads N Goods (Fuel/Parts/Cargo/Contraband), the chosen mix.
        /// Counts must sum to the printed Load N. Thin hook until the shared PendingChoice layer.
        /// </summary>
        public int LoadGoodsFuel { get; set; }
        public int LoadGoodsParts { get; set; }
        public int LoadGoodsCargo { get; set; }
        public int LoadGoodsContraband { get; set; }
        /// <summary>
        /// For "Load up to N …" / "Load Fuel, no limit": how many to take.
        /// Negative = fill to max that Fits (capped by printed up-to). Thin hook until PendingChoice.
        /// </summary>
        public int LoadAmount { get; set; } = -1;
        /// <summary>
        /// Customs-style: how many Contraband / Fugitives remain in Stash after seizure.
        /// When both types are present and exceed StashHold, counts must sum to min(total, StashHold).
        /// Negative = auto-pack (protect Contraband first). Thin hook until PendingChoice.
        /// </summary>
        public int KeepInStashContraband { get; set; } = -1;
        public int KeepInStashFugitives { get; set; } = -1;
        /// <summary>
        /// When a band seizes N Goods not in Stash, the chosen mix to remove.
        /// Counts must sum to the seized amount. Negative fields → auto order.
        /// </summary>
        public int SeizeGoodsFuel { get; set; } = -1;
        public int SeizeGoodsParts { get; set; } = -1;
        public int SeizeGoodsCargo { get; set; } = -1;
        public int SeizeGoodsContraband { get; set; } = -1;
        /// <summary>
        /// Buy-on-the-go Opportunity purchases (Rogue Trader / Freighter Convoy). "You may" — zeros skip.
        /// </summary>
        public int BuyFuel { get; set; }
        public int BuyParts { get; set; }
        public int BuyCargo { get; set; }
        public int BuyContraband { get; set; }
        /// <summary>
        /// Outbound Colonists: sell up to N Parts at the printed price.
        /// </summary>
        public int SellParts { get; set; }
        /// <summary>
        /// Discard-pile grab: planet Supply discard (optional when "any") and card id to take.
        /// </summary>
        public string? TakeFromDiscardPlanet { get; set; }
        public string? TakeFromDiscardCardId { get; set; }
        /// <summary>
        /// Nav System on the Fritz: player-to-the-right 2-Sector path (via then destination).
        /// Origin is the Nav draw Sector. Thin hook until PendingChoice.
        /// </summary>
        public string? ShipNudgeViaSectorId { get; set; }
        public string? ShipNudgeToSectorId { get; set; }
        /// <summary>Thin kill / Medic hooks until PendingChoice.</summary>
        public KillChoice? Kill { get; set; }
        /// <summary>Thin skill-test Bribes hook until PendingChoice.</summary>
        public SkillCheckChoice? SkillCheck { get; set; }
        /// <summary>
        /// Harken Solid — Your Papers are in Order (may ignore Customs Inspection).
        /// Requires CountsAsSolidWith Harken (Privilege Suspension blocks).
        /// </summary>
        public bool IgnoreCustomsInspection { get; set; }
    }

    /// <summary>
    /// Resolves queued Full Burn Nav draws in order.
    /// Conditional options run a Fight/Tech/Talk test to pick Keep Flying vs Full Stop.
    /// Skill-check bands also apply printed side effects (Kill Crew, Lose/Discard Fuel,
    /// Warrant Issued, Load Goods, Take $ / Parts) for the rolled total.
    /// Option-level Moral / Warrant / Seize micro-effects (Disgruntle Moral Crew, free-text
    /// Warrant Issued, stash-aware Customs seizure) apply when printed outside skill bands.
    /// Option-level Salvage Loads (Cargo / Contraband / Parts / Goods / Fuel) pack via HoldSpace
    /// when printed outside skill bands; skill-band Loads stay band-only.
    /// Opportunity buy-on-the-go / discard-pile grabs / Fly range bonuses / ship nudge apply from
    /// printed option (and skill-band discard grabs) via thin <see cref="NavResolveChoice"/> hooks.
    /// Option Requires / Spend costs (Parts, Fuel, Cargo, crew keywords, Solid, Moral Crew, …)
    /// are enforced before applying flight outcomes; unmet gates fail closed.
    /// Named "Alliance Cruiser" Nav snaps the Cruiser onto the ship and queues Contact.
    /// Cruiser Patrol / Alliance Entanglements move the Cruiser per card text without that snap/Contact.
    /// Reaver Cutter cards move a Cutter; the named "Reaver Cutter" card applies Contact immediately.
    /// Operative's Corvette cards move the Corvette per card text; Contact if it ends on an Outlaw.
    /// Evade moves to an adjacent Sector and clears remaining Nav draws.
    /// </summary>
    public sealed class NavResolver
    {
        public DrawnNav? FaceUp { get; private set; }

        public bool HasPending(GameState game) => game.PendingNavDraws.Count > 0 || FaceUp != null;

        public DrawnNav DrawNext(GameState game)
        {
            if (FaceUp != null)
                return FaceUp;
            if (game.Decks == null)
                throw new System.InvalidOperationException("Nav decks have not been loaded.");
            if (game.PendingNavDraws.Count == 0)
                throw new System.InvalidOperationException("No pending Nav draws.");
            // FAQ 4.1 p.14: Alliance Contact is resolved before the Nav Card for that Sector.
            if (game.PendingEncounter.HasValue)
            {
                throw new System.InvalidOperationException(
                    "Resolve Contact before drawing a Nav Card.");
            }
            if (MustResolveAlertsBeforeNav(game))
            {
                throw new System.InvalidOperationException(
                    "Resolve Alert Tokens before drawing a Nav Card.");
            }

            var pending = game.PendingNavDraws[0];
            game.PendingNavDraws.RemoveAt(0);
            var card = game.Decks.For(pending.Region).Draw();
            FaceUp = new DrawnNav(card, pending.Region, pending.SectorId);
            return FaceUp;
        }

        /// <summary>
        /// Director's Cut / Blue Sun: resolve Alert Tokens before drawing a Nav Card for that Sector.
        /// </summary>
        public static bool MustResolveAlertsBeforeNav(GameState game)
        {
            if (game.PendingAlertSectors.Count == 0 || game.PendingNavDraws.Count == 0)
                return false;
            return string.Equals(
                game.PendingAlertSectors[0],
                game.PendingNavDraws[0].SectorId,
                System.StringComparison.OrdinalIgnoreCase);
        }

        public bool TryResolve(
            GameState game,
            int optionIndex,
            out NavResolution? resolution,
            out string? error,
            IRng? rng = null,
            NavResolveChoice? choice = null)
        {
            resolution = null;
            error = null;
            if (FaceUp == null)
            {
                error = "No Nav card is face up. Draw next first.";
                return false;
            }

            var drawnEarly = FaceUp;
            if (IsReaverCutterCardProtectedByCorvette(game, drawnEarly))
            {
                game.Decks!.For(drawnEarly.Region).ResolveIntoDiscard(drawnEarly.Card);
                FaceUp = null;
                resolution = new NavResolution(
                    drawnEarly,
                    drawnEarly.Card.Options.Count > 0
                        ? drawnEarly.Card.Options[0]
                        : new NavOption(null, "", FlightOutcome.KeepFlying),
                    FlightOutcome.KeepFlying,
                    stopped: false,
                    reaverCutterBlockedByCorvette: true);
                return true;
            }

            // Harken Solid: Your Papers are in Order — may ignore Customs Inspection (card text).
            // Privilege Suspension: CountsAsSolidWith(Harken) is false, so ignore is refused.
            if (choice != null
                && choice.IgnoreCustomsInspection
                && ContactSolidBenefits.IsCustomsInspection(drawnEarly.Card))
            {
                if (!ContactSolidBenefits.IsSolidHarken(game, game.CurrentPlayer))
                {
                    error = "Ignoring Customs Inspection requires Solid with Harken.";
                    return false;
                }
                game.Decks!.For(drawnEarly.Region).ResolveIntoDiscard(drawnEarly.Card);
                FaceUp = null;
                var ignored = drawnEarly.Card.Options.Count > 0
                    ? drawnEarly.Card.Options[optionIndex >= 0 && optionIndex < drawnEarly.Card.Options.Count
                        ? optionIndex
                        : 0]
                    : new NavOption(null, "", FlightOutcome.KeepFlying);
                resolution = new NavResolution(
                    drawnEarly,
                    ignored,
                    FlightOutcome.KeepFlying,
                    stopped: false);
                return true;
            }

            if (optionIndex < 0 || optionIndex >= FaceUp.Card.Options.Count)
            {
                error = "Invalid option index.";
                return false;
            }

            var drawn = FaceUp;
            var option = drawn.Card.Options[optionIndex];
            var outcome = option.Outcome;
            var tokensBefore = game.Tokens;
            var pendingBefore = game.PendingEncounter;
            var pendingSectorBefore = game.PendingEncounterSectorId;
            var player = game.CurrentPlayer;
            var fuelBefore = player.Fuel;
            var partsBefore = player.Parts;
            var cargoBefore = player.Cargo;
            var contraBefore = player.Contraband;
            var cashBefore = player.Cash;
            var rangeBonusBefore = game.FlyRangeBonusThisAction;
            var fuelCouplingBefore = game.DiscardFuelPerExtraSectorThisFly;

            SkillCheckResult? check = null;
            string? bandText = null;
            if (SkillCheck.TryParse(option.Details, out var skillCheck))
            {
                if (!skillCheck.TryResolve(
                    player,
                    rng ?? new SystemRng(),
                    out check,
                    out error,
                    choice?.SkillCheck))
                    return false;
                if (outcome == FlightOutcome.Conditional)
                    outcome = SkillCheck.OutcomeFor(option.Details, check.Success);
                bandText = SkillCheck.BandText(option.Details, check.Total);
            }

            if (!ApplyTokenMoves(
                game,
                drawn,
                option,
                choice,
                out var triggersReaverContact,
                out var triggersCorvetteContact,
                out error))
                return false;

            void RollbackTokens()
            {
                game.Tokens = tokensBefore;
                game.PendingEncounter = pendingBefore;
                game.PendingEncounterSectorId = pendingSectorBefore;
            }

            void RollbackResources()
            {
                player.Fuel = fuelBefore;
                player.Parts = partsBefore;
                player.Cargo = cargoBefore;
                player.Contraband = contraBefore;
                player.Cash = cashBefore;
                game.FlyRangeBonusThisAction = rangeBonusBefore;
                game.DiscardFuelPerExtraSectorThisFly = fuelCouplingBefore;
            }

            if (triggersReaverContact || outcome == FlightOutcome.Evade)
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.EvadeToSectorId))
                {
                    RollbackTokens();
                    error = triggersReaverContact
                        ? "Reaver Contact requires an Evade destination."
                        : "Evade requires an adjacent destination sector.";
                    return false;
                }
                var fromSector = game.CurrentPlayer.SectorId;
                game.CurrentPlayer.SectorId = drawn.SectorId;
                var canEvade = FlightEvade.CanMove(game, game.CurrentPlayer, choice.EvadeToSectorId!, out error);
                game.CurrentPlayer.SectorId = fromSector;
                if (!canEvade)
                {
                    RollbackTokens();
                    return false;
                }
            }

            if (triggersReaverContact && rng == null)
            {
                RollbackTokens();
                error = "Reaver Contact requires a Fight roll.";
                return false;
            }

            if (!TryApplyRequiresAndCosts(game, option.Details, ref outcome, choice, out error))
            {
                RollbackTokens();
                RollbackResources();
                return false;
            }

            if (!TryApplyAlertTokenEffects(game, drawn, option.Details, out error))
            {
                RollbackTokens();
                RollbackResources();
                return false;
            }

            if (check != null
                && !CanApplySkillBandEffects(game, player, bandText, choice, out error))
            {
                RollbackTokens();
                RollbackResources();
                return false;
            }

            if (!CanApplyOptionMicroEffects(game, drawn, player, option.Details, choice, check != null, out error))
            {
                RollbackTokens();
                RollbackResources();
                return false;
            }

            ReaverContactResult? reaverContact = null;
            CorvetteContactResult? corvetteContact = null;
            var stopped = outcome == FlightOutcome.FullStop || outcome == FlightOutcome.Evade;

            if (triggersReaverContact)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!ReaverContact.TryApplyImmediate(
                    game,
                    rng!,
                    choice!.EvadeToSectorId!,
                    out reaverContact,
                    out error,
                    choice.Kill))
                {
                    RollbackTokens();
                    RollbackResources();
                    return false;
                }
                game.PendingNavDraws.Clear();
                outcome = FlightOutcome.Evade;
                stopped = true;
            }
            else if (triggersCorvetteContact)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!CorvetteContact.TryApplyImmediate(
                    game,
                    out corvetteContact,
                    out error,
                    choice?.CorvetteContact))
                {
                    RollbackTokens();
                    RollbackResources();
                    return false;
                }
                game.PendingEncounter = null;
                game.PendingEncounterSectorId = null;
                game.PendingNavDraws.Clear();
                outcome = FlightOutcome.FullStop;
                stopped = true;
            }
            else if (outcome == FlightOutcome.FullStop)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                game.PendingNavDraws.Clear();
            }
            else if (outcome == FlightOutcome.Evade)
            {
                game.CurrentPlayer.SectorId = drawn.SectorId;
                if (!FlightEvade.TryMove(game, game.CurrentPlayer, choice!.EvadeToSectorId!, out error))
                {
                    RollbackTokens();
                    RollbackResources();
                    return false;
                }
                game.PendingNavDraws.Clear();
            }

            var crewKilled = 0;
            var warrantsIssued = 0;
            var fuelLost = 0;
            var cashGained = 0;
            var goodsLoaded = 0;
            var moralDisgruntled = 0;
            var disgruntledCleared = 0;
            var contrabandSeized = 0;
            var fugitivesSeized = 0;
            var goodsSeized = 0;
            if (check != null)
            {
                ApplySkillBandEffects(
                    game,
                    player,
                    bandText,
                    choice,
                    rng ?? new SystemRng(),
                    out crewKilled,
                    out warrantsIssued,
                    out fuelLost,
                    out cashGained,
                    out goodsLoaded,
                    out goodsSeized);
            }

            ApplyOptionMicroEffects(
                game,
                drawn,
                player,
                option.Details,
                choice,
                skillCheckPresent: check != null,
                ref warrantsIssued,
                out moralDisgruntled,
                out disgruntledCleared,
                out contrabandSeized,
                out fugitivesSeized,
                out var optionGoodsLoaded);
            if (check == null)
                goodsLoaded = optionGoodsLoaded;

            game.Decks!.For(drawn.Region).ResolveIntoDiscard(drawn.Card);
            FaceUp = null;
            resolution = new NavResolution(
                drawn,
                option,
                outcome,
                stopped,
                check,
                reaverContact,
                corvetteContact,
                crewKilled: crewKilled,
                warrantsIssued: warrantsIssued,
                fuelLost: fuelLost,
                cashGained: cashGained,
                goodsLoaded: goodsLoaded,
                moralDisgruntled: moralDisgruntled,
                disgruntledCleared: disgruntledCleared,
                contrabandSeized: contrabandSeized,
                fugitivesSeized: fugitivesSeized,
                goodsSeized: goodsSeized);
            return true;
        }

        private static bool TryApplyAlertTokenEffects(
            GameState game,
            DrawnNav drawn,
            string details,
            out string? error)
        {
            error = null;
            if (!ContainsPlaceAlert(details))
                return true;

            if (Contains(details, "Place Reaver Token"))
            {
                game.Tokens = game.Tokens.PlaceAlertToken(drawn.SectorId, AlertTokenKind.Reaver);
            }

            if (Contains(details, "Place Alliance Alert Token in this Sector")
                || Contains(details, "Place an Alliance Alert Token in this Sector"))
            {
                game.Tokens = game.Tokens.PlaceAlertToken(drawn.SectorId, AlertTokenKind.Alliance);
            }

            if (Contains(details, "Place an Alliance Alert Token in all Sectors adjacent")
                || Contains(details, "Place an Alliance Alert Token in all adjacent")
                || Contains(details, "Place Alliance Alert Token in all adjacent"))
            {
                foreach (var neighbor in game.Map.Neighbors(drawn.SectorId))
                    game.Tokens = game.Tokens.PlaceAlertToken(neighbor, AlertTokenKind.Alliance);
            }

            if (Contains(details, "Place an Alliance Alert Token in every Sector occupied by an Outlaw Ship"))
            {
                foreach (var player in game.Players)
                {
                    if (AlertTokenRules.IsOutlawShip(player))
                        game.Tokens = game.Tokens.PlaceAlertToken(player.SectorId, AlertTokenKind.Alliance);
                }
            }

            return true;
        }

        private static bool ContainsPlaceAlert(string details) =>
            Contains(details, "Place Reaver Token")
            || Contains(details, "Place Alliance Alert Token")
            || Contains(details, "Place an Alliance Alert Token");

        public bool TryAutoResolve(
            GameState game,
            out NavResolution? resolution,
            out string? error,
            IRng? rng = null,
            NavResolveChoice? choice = null)
        {
            resolution = null;
            var drawn = DrawNext(game);
            // Corvette protects against Reaver Cutter Nav even when the card has multiple options.
            if (IsReaverCutterCardProtectedByCorvette(game, drawn))
                return TryResolve(game, 0, out resolution, out error, rng, choice);
            if (drawn.Card.Options.Count != 1)
            {
                error = "Card requires an option choice.";
                return false;
            }
            if (SkillCheck.TryParse(drawn.Card.Options[0].Details, out _) && rng == null)
            {
                error = "Card requires an option choice.";
                return false;
            }
            return TryResolve(game, 0, out resolution, out error, rng, choice);
        }

        private static readonly Regex RequiresClause = new Regex(
            @"Requires\s*:?\s*([^.;]+?)(?=\s*(?:--|:|\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpendAmount = new Regex(
            @"Spend\s+(\d+)\s+(Parts?|Fuel|Cargo)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadingFugitives = new Regex(
            @"Loading\s+(\d+)\s+Fugitives",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MoralCrewNeed = new Regex(
            @"(\d+)\s+or more Moral Crew",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool TryApplyRequiresAndCosts(
            GameState game,
            string details,
            ref FlightOutcome outcome,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var player = game.CurrentPlayer;
            var text = details ?? "";
            var skipGenericFuelSpend = false;
            var skipGenericPartsSpend = false;

            // Nav Hazard: "If you have Pilot, Keep Flying. Otherwise: Spend 1 Fuel, Keep Flying."
            if (IsPilotOrSpendFuel(text))
            {
                skipGenericFuelSpend = true;
                if (!MisbehaveResolver.HasTag(game, player, "Pilot"))
                {
                    if (player.Fuel < 1)
                    {
                        error = "Not enough fuel.";
                        return false;
                    }
                    player.Fuel -= 1;
                }
            }

            // "Spend 1 Part to Keep Flying. Otherwise, Full Stop."
            if (IsSpendPartToKeepFlyingOtherwise(text))
            {
                skipGenericPartsSpend = true;
                if (player.Parts >= 1)
                {
                    player.Parts -= 1;
                    outcome = FlightOutcome.KeepFlying;
                }
                else
                {
                    outcome = FlightOutcome.FullStop;
                }
            }

            if (!TryMeetRequires(game, player, text, choice, out var discardCrewId, out error))
                return false;

            if (!TryApplySpends(player, text, skipGenericFuelSpend, skipGenericPartsSpend, out error))
                return false;

            if (discardCrewId != null)
            {
                if (!player.Roster.TryDismiss(discardCrewId, out error))
                    return false;
            }

            // Entanglements etc.: Take $ outside skill bands. Skill-check Take $ is band-applied.
            if (Contains(text, "Take $") && !SkillCheck.TryParse(text, out _))
            {
                var take = Regex.Match(text, @"Take\s+\$(\d+)", RegexOptions.IgnoreCase);
                if (take.Success)
                    player.Cash += int.Parse(take.Groups[1].Value);
            }

            // Resource-gated Conditional (no skill check) is resolved above; leftover Conditional → Keep Flying.
            if (outcome == FlightOutcome.Conditional)
                outcome = FlightOutcome.KeepFlying;

            return true;
        }

        private static bool IsPilotOrSpendFuel(string details) =>
            Contains(details, "If you have Pilot")
            && Contains(details, "Otherwise")
            && Contains(details, "Spend 1 Fuel");

        private static bool IsSpendPartToKeepFlyingOtherwise(string details) =>
            Contains(details, "Spend 1 Part to Keep Flying")
            && Contains(details, "Otherwise")
            && Contains(details, "Full Stop");

        private static bool TryMeetRequires(
            GameState game,
            PlayerState player,
            string details,
            NavResolveChoice? choice,
            out string? discardCrewId,
            out string? error)
        {
            error = null;
            discardCrewId = null;
            var match = RequiresClause.Match(details);
            if (!match.Success)
                return true;

            var need = match.Groups[1].Value.Trim();

            if (Contains(need, "Pilot and Mechanic"))
            {
                if (!MisbehaveResolver.HasTag(game, player, "Pilot")
                    || !MisbehaveResolver.HasTag(game, player, "Mechanic"))
                {
                    error = "Requires Pilot and Mechanic.";
                    return false;
                }
                return true;
            }

            var moral = MoralCrewNeed.Match(need);
            if (moral.Success)
            {
                var n = int.Parse(moral.Groups[1].Value);
                if (player.Roster.MoralCount < n)
                {
                    error = $"Requires {n} or more Moral Crew.";
                    return false;
                }
                return true;
            }

            if (Contains(need, "Discarding 1 Crew") || Contains(need, "Discarding one Crew"))
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.DiscardCrewId))
                {
                    error = "Requires Discarding 1 Crew.";
                    return false;
                }
                var member = player.Roster.Find(choice.DiscardCrewId!);
                if (member == null)
                {
                    error = "That crew is not on the ship.";
                    return false;
                }
                if (member.IsLeader)
                {
                    error = "Cannot dismiss your Leader.";
                    return false;
                }
                discardCrewId = member.Id;
                return true;
            }

            var fugitives = LoadingFugitives.Match(need);
            if (fugitives.Success)
            {
                var n = int.Parse(fugitives.Groups[1].Value);
                if (!HoldSpace.Fits(player, addFugitives: n))
                {
                    error = $"Requires Loading {n} Fugitives (not enough hold space).";
                    return false;
                }
                // Actual load is a salvage effect (separate slice). Gate only.
                return true;
            }

            if (TryParseSolidNeed(need, out var contactName))
            {
                if (!HasSolidWith(game, player, contactName))
                {
                    error = $"Requires Solid {contactName}.";
                    return false;
                }
                return true;
            }

            // Single keyword / profession / gear (Pilot, Mechanic, Soldier, Medic, Fake ID, …)
            var tag = need.Trim().TrimEnd(':').Trim();
            if (!MisbehaveResolver.HasTag(game, player, tag))
            {
                error = $"Requires {tag}.";
                return false;
            }
            return true;
        }

        private static bool TryParseSolidNeed(string need, out string contactName)
        {
            contactName = "";
            var with = Regex.Match(need, @"Solid\s+with\s+(.+)$", RegexOptions.IgnoreCase);
            if (with.Success)
            {
                contactName = with.Groups[1].Value.Trim();
                return contactName.Length > 0;
            }
            var harken = Regex.Match(
                need,
                @"Solid(?:\s+Rep)?\s+(?:with\s+)?(Harken)\s*(?:Rep)?",
                RegexOptions.IgnoreCase);
            if (harken.Success)
            {
                contactName = "Harken";
                return true;
            }
            return false;
        }

        private static bool TryApplySpends(
            PlayerState player,
            string details,
            bool skipFuel,
            bool skipParts,
            out string? error)
        {
            error = null;
            foreach (Match match in SpendAmount.Matches(details))
            {
                var amount = int.Parse(match.Groups[1].Value);
                var kind = match.Groups[2].Value;
                if (kind.StartsWith("Part", StringComparison.OrdinalIgnoreCase))
                {
                    if (skipParts)
                        continue;
                    if (player.Parts < amount)
                    {
                        error = amount == 1 ? "Not enough parts." : $"Need {amount} Parts.";
                        return false;
                    }
                    player.Parts -= amount;
                }
                else if (kind.Equals("Fuel", StringComparison.OrdinalIgnoreCase))
                {
                    if (skipFuel)
                        continue;
                    if (player.Fuel < amount)
                    {
                        error = "Not enough fuel.";
                        return false;
                    }
                    player.Fuel -= amount;
                }
                else if (kind.Equals("Cargo", StringComparison.OrdinalIgnoreCase))
                {
                    if (player.Cargo < amount)
                    {
                        error = amount == 1 ? "Not enough cargo." : $"Need {amount} Cargo.";
                        return false;
                    }
                    player.Cargo -= amount;
                }
            }
            return true;
        }

        private static bool HasSolidWith(GameState game, PlayerState player, string contactName)
        {
            if (ActiveAlertRules.CountsAsSolidWith(game, player, contactName))
                return true;
            if (game.Contacts != null && game.Contacts.TryFindByName(contactName, out var contact))
            {
                if (ActiveAlertRules.CountsAsSolidWith(game, player, contact.Id)
                    || ActiveAlertRules.CountsAsSolidWith(game, player, contact.Name))
                    return true;
            }

            if (!ActiveAlertRules.IsHarken(contactName))
                return false;
            foreach (var id in player.SolidWith)
            {
                if (ActiveAlertRules.IsHarken(id)
                    && ActiveAlertRules.CountsAsSolidWith(game, player, id))
                    return true;
            }
            return false;
        }

        private static bool ApplyTokenMoves(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersReaverContact,
            out bool triggersCorvetteContact,
            out string? error)
        {
            triggersReaverContact = false;
            triggersCorvetteContact = false;
            error = null;
            var type = drawn.Card.Type ?? "";
            if (type.Equals("Alliance Cruiser", System.StringComparison.OrdinalIgnoreCase)
                || IsNamedAllianceCruiserCard(drawn.Card))
            {
                return ApplyAllianceCruiserTypedCard(game, drawn, option, choice, out error);
            }

            if (type.Equals("Operative's Corvette", System.StringComparison.OrdinalIgnoreCase))
                return ApplyOperativeCorvetteCard(
                    game, drawn, option, choice, out triggersCorvetteContact, out error);

            if (!type.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
                return true;

            return ApplyReaverCutterCard(game, drawn, option, choice, out triggersReaverContact, out error);
        }

        /// <summary>
        /// GF9 p.8 / Director's Cut p.17: named Alliance Cruiser snaps to the drawer;
        /// Alliance Entanglements — drawer moves the Cruiser; Cruiser Patrol — player to the right
        /// moves it 1 Sector within Alliance Space. Only the named card queues Contact / Full Stop.
        /// </summary>
        private static bool ApplyAllianceCruiserTypedCard(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            if (IsNamedAllianceCruiserCard(drawn.Card))
            {
                game.Tokens = game.Tokens.WithAllianceCruiser(drawn.SectorId);
                AllianceCruiserContact.SetHead(game, game.CurrentPlayer.Id, drawn.SectorId);
                game.BountyDeck?.CycleWantedList(game.RemovedFromPlay);
                game.AllianceAlertDeck?.DrawAndActivate();
                return true;
            }

            var name = drawn.Card.Name ?? "";
            var id = drawn.Card.Id ?? "";
            if (IsCruiserPatrolCard(id, name))
                return ApplyCruiserPatrol(game, choice, out error);
            if (IsAllianceEntanglementsCard(id, name))
                return ApplyAllianceEntanglements(game, option, choice, out error);

            error = $"Unknown Alliance Cruiser-type Nav card '{id}'.";
            return false;
        }

        private static bool IsNamedAllianceCruiserCard(NavCard card)
        {
            var id = card.Id ?? "";
            var name = card.Name ?? "";
            return id.Equals("nav_alliance-cruiser", System.StringComparison.OrdinalIgnoreCase)
                || name.Equals("Alliance Cruiser", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCruiserPatrolCard(string id, string name) =>
            id.Equals("nav_cruiser-patrol", System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cruiser Patrol", System.StringComparison.OrdinalIgnoreCase);

        private static bool IsAllianceEntanglementsCard(string id, string name) =>
            id.Equals("nav_alliance-entanglements", System.StringComparison.OrdinalIgnoreCase)
            || name.Equals("Alliance Entanglements", System.StringComparison.OrdinalIgnoreCase);

        private static bool ApplyCruiserPatrol(
            GameState game,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var destination = choice?.AllianceCruiserToSectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Cruiser Patrol requires a destination sector (player to the right).";
                return false;
            }

            var from = game.Tokens.AllianceCruiserSectorId;
            if (string.IsNullOrEmpty(from))
            {
                error = "Alliance Cruiser is not on the board.";
                return false;
            }

            if (!IsAllianceSpace(game, destination!))
            {
                error = "Cruiser Patrol destination must be within Alliance Space.";
                return false;
            }

            if (!AreAdjacent(game, from!, destination!))
            {
                error = "Cruiser Patrol must move the Cruiser 1 Sector.";
                return false;
            }

            game.Tokens = game.Tokens.WithAllianceCruiser(destination);
            // FAQ 4.1 p.14: Outlaws in the Cruiser's new Sector resolve Contact before the flyer continues.
            AllianceCruiserContact.QueueForOutlawsInSector(game, destination!);
            return true;
        }

        private static bool ApplyAllianceEntanglements(
            GameState game,
            NavOption option,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var destination = choice?.AllianceCruiserToSectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Alliance Entanglements requires a destination sector.";
                return false;
            }

            if (!IsAllianceSpace(game, destination!))
            {
                error = "Alliance Entanglements destination must be an Alliance Sector.";
                return false;
            }

            var details = option.Details ?? "";
            if (RequiresUnoccupiedByFirefly(details) && HasFireflyInSector(game, destination!))
            {
                error = "Alliance Entanglements destination must not be occupied by a Firefly.";
                return false;
            }

            if (RequiresOutlawShipInSector(details) && !HasOutlawShipInSector(game, destination!))
            {
                error = "Alliance Entanglements destination must have an Outlaw Ship.";
                return false;
            }

            game.Tokens = game.Tokens.WithAllianceCruiser(destination);
            // FAQ 4.1 p.14: moving the Cruiser onto an Outlaw queues Contact (multi-seat interrupt).
            AllianceCruiserContact.QueueForOutlawsInSector(game, destination!);
            return true;
        }

        private static bool IsAllianceSpace(GameState game, string sectorId)
        {
            if (!game.Map.TryGet(sectorId, out var sector))
                return false;
            return sector.NavRegion == NavRegion.Alliance;
        }

        private static bool AreAdjacent(GameState game, string fromSectorId, string toSectorId)
        {
            foreach (var n in game.Map.Neighbors(fromSectorId))
            {
                if (string.Equals(n, toSectorId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool RequiresUnoccupiedByFirefly(string details) =>
            Contains(details, "not occupied by a Firefly");

        private static bool RequiresOutlawShipInSector(string details) =>
            Contains(details, "with an Outlaw Ship");

        private static bool HasFireflyInSector(GameState game, string sectorId)
        {
            foreach (var player in game.Players)
            {
                if (string.Equals(player.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasOutlawShipInSector(GameState game, string sectorId)
        {
            foreach (var player in game.Players)
            {
                if (string.Equals(player.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase)
                    && AlertTokenRules.IsOutlawShip(player))
                    return true;
            }
            return false;
        }

        private static bool ApplyOperativeCorvetteCard(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersCorvetteContact,
            out string? error)
        {
            triggersCorvetteContact = false;
            error = null;
            var destination = choice?.OperativeCorvetteToSectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Operative's Corvette move requires a destination sector.";
                return false;
            }

            if (!TryValidateCorvetteDestination(game, drawn, option.Details ?? "", destination!, out error))
                return false;

            if (!game.Tokens.TryMoveOperativeCorvette(
                destination!,
                out var moved,
                out error,
                choice?.DriveOffReaverToSectorId))
                return false;

            game.Tokens = moved;

            // Contact when Corvette ends its move in an Outlaw Ship's Sector (current player).
            if (string.Equals(game.CurrentPlayer.SectorId, destination, System.StringComparison.OrdinalIgnoreCase)
                && AlertTokenRules.IsOutlawShip(game.CurrentPlayer))
            {
                game.PendingEncounter = TokenKind.OperativeCorvette;
                game.PendingEncounterSectorId = destination;
                triggersCorvetteContact = true;
            }
            return true;
        }

        private static bool TryValidateCorvetteDestination(
            GameState game,
            DrawnNav drawn,
            string details,
            string destination,
            out string? error)
        {
            error = null;
            if (!game.Map.TryGet(destination, out var sector))
            {
                error = $"Unknown sector '{destination}'.";
                return false;
            }

            // Corvette may enter Alliance, Border, or Rim Space (any map sector) — already true for our map.
            if (RequiresUnoccupied(details) && !IsUnoccupiedSector(game, destination))
            {
                error = "Operative's Corvette destination must be unoccupied.";
                return false;
            }

            if (RequiresPlanetary(details) && !sector.IsPlanetary && string.IsNullOrWhiteSpace(sector.Planet))
            {
                error = "Operative's Corvette destination must be a Planetary Sector.";
                return false;
            }

            if (RequiresAdjacentToDraw(details))
            {
                var adjacent = false;
                foreach (var n in game.Map.Neighbors(drawn.SectorId))
                {
                    if (string.Equals(n, destination, System.StringComparison.OrdinalIgnoreCase))
                    {
                        adjacent = true;
                        break;
                    }
                }
                if (!adjacent)
                {
                    error = "Operative's Corvette destination must be adjacent to your current location.";
                    return false;
                }
            }

            if (RequiresOneOrTwoSectors(details))
            {
                var from = game.Tokens.OperativeCorvetteSectorId;
                if (string.IsNullOrEmpty(from))
                {
                    error = "Operative's Corvette is not on the board.";
                    return false;
                }
                var path = new Pathfinder(game.Map).ShortestPath(from!, destination);
                if (path == null)
                {
                    error = "No path for Operative's Corvette move.";
                    return false;
                }
                var distance = path.Count - 1;
                if (distance < 1 || distance > 2)
                {
                    error = "Operative's Corvette must move 1 or 2 Sectors.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsUnoccupiedSector(GameState game, string sectorId)
        {
            if (game.Tokens.EncounterAt(sectorId).HasValue)
                return false;
            foreach (var player in game.Players)
            {
                if (string.Equals(player.SectorId, sectorId, System.StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static bool RequiresUnoccupied(string details) =>
            Contains(details, "unoccupied");

        private static bool RequiresPlanetary(string details) =>
            Contains(details, "Planetary Sector");

        private static bool RequiresAdjacentToDraw(string details) =>
            Contains(details, "adjacent to your current location");

        private static bool RequiresOneOrTwoSectors(string details) =>
            Contains(details, "1 or 2 Sectors") || Contains(details, "1 or 2 sectors");

        /// <summary>
        /// Kalidasa / Director's Cut: Reaver Cutter Nav while moving into the Corvette's Sector
        /// is cancelled; reshuffle normally.
        /// </summary>
        private static bool IsReaverCutterCardProtectedByCorvette(GameState game, DrawnNav drawn)
        {
            var type = drawn.Card.Type ?? "";
            var name = drawn.Card.Name ?? "";
            if (!type.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
                return false;
            var corvette = game.Tokens.OperativeCorvetteSectorId;
            return corvette != null
                && string.Equals(corvette, drawn.SectorId, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool ApplyReaverCutterCard(
            GameState game,
            DrawnNav drawn,
            NavOption option,
            NavResolveChoice? choice,
            out bool triggersReaverContact,
            out string? error)
        {
            triggersReaverContact = false;
            error = null;
            var details = option.Details ?? "";
            var cardName = drawn.Card.Name ?? "";

            // Named "Reaver Cutter" Nav Card: Cutter moves to the draw sector; Contact if that option applies.
            if (cardName.Equals("Reaver Cutter", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!game.Tokens.TryMoveReaverCutter(
                    drawn.SectorId,
                    out var moved,
                    out error,
                    choice?.ReaverCutterIndex ?? 0,
                    leaveReaverAlertToken: game.UseAlertTokens))
                {
                    // Already occupied by a Reaver: do not move another; still resolve Contact if applicable.
                    // Blue Sun Alert Token note — also matches "already occupied by a Reaver ship".
                    if (game.Tokens.EncounterAt(drawn.SectorId) != TokenKind.ReaverCutter)
                        return false;
                    error = null;
                }
                else
                {
                    game.Tokens = moved;
                }

                if (IsImmediateReaverContactOption(details))
                    triggersReaverContact = true;
                return true;
            }

            // Other Reaver Cutter-type cards (Hunt / Dead Ahead / Bait / Orbit): move without Contact.
            // "If the Cutter moves into your Sector as a result of 'Reavers on the Hunt', do not resolve Reaver Contact yet."
            var destination = choice?.ReaverCutterToSectorId;
            if (string.IsNullOrWhiteSpace(destination) && MovesCutterToDrawSector(details))
                destination = drawn.SectorId;
            if (string.IsNullOrWhiteSpace(destination))
            {
                error = "Reaver Cutter move requires a destination sector.";
                return false;
            }

            var cutterIndex = choice?.ReaverCutterIndex ?? 0;
            if (!TryValidateReaverDestination(game, drawn, details, destination!, cutterIndex, out error))
                return false;

            if (!game.Tokens.TryMoveReaverCutter(
                destination!,
                out var updated,
                out error,
                cutterIndex,
                leaveReaverAlertToken: game.UseAlertTokens))
                return false;
            game.Tokens = updated;
            return true;
        }

        /// <summary>
        /// Enforces printed Reaver Nav destination constraints (mirror of Corvette validators).
        /// FAQ 4.1 p.12: Reaver Ships may never move into Alliance Space; if no legal
        /// Border/Rim destination exists, do not move (caller must not invent a fallback).
        /// </summary>
        private static bool TryValidateReaverDestination(
            GameState game,
            DrawnNav drawn,
            string details,
            string destination,
            int cutterIndex,
            out string? error)
        {
            error = null;
            if (!game.Map.TryGet(destination, out var sector))
            {
                error = $"Unknown sector '{destination}'.";
                return false;
            }

            // FAQ 4.1: "Reaver Ships may never move into Alliance Space, for any reason."
            if (sector.NavRegion == NavRegion.Alliance)
            {
                error = "Reaver Ships may never move into Alliance Space.";
                return false;
            }

            if (RequiresUnoccupiedByFirefly(details) && HasFireflyInSector(game, destination))
            {
                error = "Reaver destination must not be occupied by a Firefly.";
                return false;
            }

            if (RequiresPlanetary(details) && !sector.IsPlanetary && string.IsNullOrWhiteSpace(sector.Planet))
            {
                error = "Reaver destination must be a Planetary Sector.";
                return false;
            }

            if (RequiresAdjacentToDraw(details) && !AreAdjacent(game, drawn.SectorId, destination))
            {
                error = "Reaver destination must be adjacent to your current location.";
                return false;
            }

            if (RequiresBorderSectorOnly(details) && sector.NavRegion != NavRegion.Border)
            {
                error = "Reaver destination must be a Border Sector.";
                return false;
            }

            if (RequiresBorderOrRimSpace(details)
                && sector.NavRegion != NavRegion.Border
                && sector.NavRegion != NavRegion.Rim)
            {
                error = "Reaver destination must be in Border or Rim Space.";
                return false;
            }

            if (RequiresOneSectorMove(details))
            {
                if (cutterIndex < 0 || cutterIndex >= game.Tokens.ReaverCutterSectorIds.Count)
                {
                    error = "Invalid Reaver Cutter index.";
                    return false;
                }

                var from = game.Tokens.ReaverCutterSectorIds[cutterIndex];
                if (!AreAdjacent(game, from, destination))
                {
                    error = "Reaver Ship must move 1 Sector.";
                    return false;
                }

                if (sector.NavRegion != NavRegion.Border && sector.NavRegion != NavRegion.Rim)
                {
                    error = "Reaver destination must be in Border or Rim Space.";
                    return false;
                }
            }

            return true;
        }

        private static bool RequiresBorderSectorOnly(string details) =>
            Contains(details, "any Border Sector")
            || (Contains(details, "Border Sector")
                && !Contains(details, "Rim")
                && !Contains(details, "Planetary Sector"));

        private static bool RequiresBorderOrRimSpace(string details) =>
            Contains(details, "Border or Rim Space")
            || Contains(details, "within Border or Rim Space")
            || Contains(details, "in Border or Rim Space");

        private static bool RequiresOneSectorMove(string details) =>
            Contains(details, "1 Sector within") || Contains(details, "1 sector within");

        private static bool IsImmediateReaverContactOption(string details) =>
            Contains(details, "Kill all Passengers") || Contains(details, "Fight 8");

        private static bool MovesCutterToDrawSector(string details) =>
            Contains(details, "to your current location")
            || Contains(details, "to your Sector")
            || Contains(details, "to your sector");

        private static readonly Regex KillCrewCount = new Regex(
            @"Kill\s+(?:(\d+)|a)\s+Crew",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoseOrDiscardFuel = new Regex(
            @"(?:Lose|Discard)\s+(\d+)\s+Fuel",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TakeCash = new Regex(
            @"Take\s+\$(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TakeParts = new Regex(
            @"Take\s+\$\d+\s+and\s+(\d+)\s+Parts|Take\s+(\d+)\s+Parts",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadGoodsCount = new Regex(
            @"Load\s+(\d+)\s+Goods",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadTypedGoods = new Regex(
            @"Load\s+(\d+)\s+(Cargo|Contraband|Parts?|Fuel)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadUpToTyped = new Regex(
            @"Load\s+up\s+to\s+(\d+)\s+(Cargo|Contraband|Parts?|Fuel)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LoadFuelNoLimit = new Regex(
            @"Load\s+Fuel\s*,?\s*no\s+limit",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SeizeGoodsNotInStash = new Regex(
            @"(\d+)\s+Goods\s+not\s+in\s+Stash\s+are\s+seized",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Nested [Fight COP] trees are deferred; do not apply their Kill/Warrant text without rolling.
        /// </summary>
        private static bool IsNestedSkillTreeStub(string? bandText) =>
            !string.IsNullOrWhiteSpace(bandText) && SkillCheck.TryParse(bandText, out _);

        private static bool CanApplySkillBandEffects(
            GameState game,
            PlayerState player,
            string? bandText,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(bandText) || IsNestedSkillTreeStub(bandText))
                return true;

            var text = bandText!;
            if (!TryPlanGoodsLoad(player, text, choice, out _, out _, out _, out _, out _, out error))
                return false;

            var parts = PlannedTakeParts(text);
            if (parts > 0 && !HoldSpace.Fits(player, addParts: parts))
            {
                error = "Not enough cargo/stash space for Parts.";
                return false;
            }

            if (!TryPlanGoodsSeize(player, text, choice, out _, out _, out _, out _, out _, out error))
                return false;

            if (!CanApplyDiscardGrab(game, player, text, choice, optional: false, out error))
                return false;

            return true;
        }

        private static bool CanApplyOptionMicroEffects(
            GameState game,
            DrawnNav drawn,
            PlayerState player,
            string details,
            NavResolveChoice? choice,
            bool skillCheckPresent,
            out string? error)
        {
            error = null;
            var text = details ?? "";
            if (IsCustomsStashSeize(text)
                && !TryPlanCustomsStashKeep(player, choice, out _, out _, out error))
                return false;

            // Skill-band Loads are validated in CanApplySkillBandEffects; avoid double-parse.
            if (!skillCheckPresent
                && !TryPlanGoodsLoad(player, text, choice, out _, out _, out _, out _, out _, out error))
                return false;

            if (!CanApplyBuyOnTheGo(player, text, choice, out error))
                return false;

            if (!CanApplySellParts(player, text, choice, out error))
                return false;

            if (!skillCheckPresent
                && !CanApplyDiscardGrab(game, player, text, choice, optional: true, out error))
                return false;

            if (!CanApplyShipNudge(game, drawn, text, choice, out error))
                return false;

            return true;
        }

        private static void ApplySkillBandEffects(
            GameState game,
            PlayerState player,
            string? bandText,
            NavResolveChoice? choice,
            IRng rng,
            out int crewKilled,
            out int warrantsIssued,
            out int fuelLost,
            out int cashGained,
            out int goodsLoaded,
            out int goodsSeized)
        {
            crewKilled = 0;
            warrantsIssued = 0;
            fuelLost = 0;
            cashGained = 0;
            goodsLoaded = 0;
            goodsSeized = 0;
            if (string.IsNullOrWhiteSpace(bandText) || IsNestedSkillTreeStub(bandText))
                return;

            var text = bandText!;

            if (Contains(text, "Warrant Issued"))
            {
                player.Warrants++;
                warrantsIssued = 1;
            }

            var kill = KillCrewCount.Match(text);
            if (kill.Success)
            {
                var count = kill.Groups[1].Success ? int.Parse(kill.Groups[1].Value) : 1;
                crewKilled = CrewKill.KillUpTo(game, player, count, rng, choice?.Kill);
            }

            var fuel = LoseOrDiscardFuel.Match(text);
            if (fuel.Success)
            {
                var n = int.Parse(fuel.Groups[1].Value);
                fuelLost = System.Math.Min(n, player.Fuel);
                player.Fuel -= fuelLost;
            }

            var cash = TakeCash.Match(text);
            if (cash.Success)
            {
                cashGained = int.Parse(cash.Groups[1].Value);
                player.Cash += cashGained;
            }

            var parts = PlannedTakeParts(text);
            if (parts > 0 && HoldSpace.Fits(player, addParts: parts))
                player.Parts += parts;

            if (TryPlanGoodsLoad(
                player,
                text,
                choice,
                out var addFuel,
                out var addParts,
                out var addCargo,
                out var addContra,
                out var loaded,
                out _))
            {
                player.Fuel += addFuel;
                player.Parts += addParts;
                player.Cargo += addCargo;
                player.Contraband += addContra;
                goodsLoaded = loaded;
            }

            if (TryPlanGoodsSeize(
                player,
                text,
                choice,
                out var seizeFuel,
                out var seizeParts,
                out var seizeCargo,
                out var seizeContra,
                out var seized,
                out _))
            {
                player.Fuel -= seizeFuel;
                player.Parts -= seizeParts;
                player.Cargo -= seizeCargo;
                player.Contraband -= seizeContra;
                goodsSeized = seized;
            }

            TryApplyDiscardGrab(game, player, text, choice, optional: false);
        }

        /// <summary>
        /// Option-level Moral / free-text Warrant / Customs stash seize / Salvage Load /
        /// Opportunity buy-sell / discard grabs / range / ship-nudge / fuel-coupling
        /// (not skill-band text). When a skill check is present, Warrant Issued and Load are
        /// band-only to avoid double-issue / double-load.
        /// </summary>
        private static void ApplyOptionMicroEffects(
            GameState game,
            DrawnNav drawn,
            PlayerState player,
            string details,
            NavResolveChoice? choice,
            bool skillCheckPresent,
            ref int warrantsIssued,
            out int moralDisgruntled,
            out int disgruntledCleared,
            out int contrabandSeized,
            out int fugitivesSeized,
            out int goodsLoaded)
        {
            moralDisgruntled = 0;
            disgruntledCleared = 0;
            contrabandSeized = 0;
            fugitivesSeized = 0;
            goodsLoaded = 0;
            var text = details ?? "";

            if (IsDisgruntleMoral(text))
                moralDisgruntled = player.Roster.DisgruntleMoral();

            if (Contains(text, "Remove Disgruntled from all Moral Crew"))
                disgruntledCleared = player.Roster.ClearDisgruntledMoral();
            else if (Contains(text, "Remove Disgruntled from all Crew"))
                disgruntledCleared = player.Roster.ClearDisgruntled();

            if (!skillCheckPresent && ShouldIssueWarrant(text, player))
            {
                player.Warrants++;
                warrantsIssued += 1;
            }

            if (IsCustomsStashSeize(text)
                && TryPlanCustomsStashKeep(player, choice, out var keepContra, out var keepFug, out _))
            {
                contrabandSeized = player.Contraband - keepContra;
                fugitivesSeized = player.Fugitives - keepFug;
                player.Contraband = keepContra;
                player.Fugitives = keepFug;
            }

            if (!skillCheckPresent
                && TryPlanGoodsLoad(
                    player,
                    text,
                    choice,
                    out var addFuel,
                    out var addParts,
                    out var addCargo,
                    out var addContra,
                    out var loaded,
                    out _))
            {
                player.Fuel += addFuel;
                player.Parts += addParts;
                player.Cargo += addCargo;
                player.Contraband += addContra;
                goodsLoaded = loaded;
            }

            TryApplyBuyOnTheGo(player, text, choice);
            TryApplySellParts(player, text, choice);

            if (!skillCheckPresent)
                TryApplyDiscardGrab(game, player, text, choice, optional: true);

            TryApplyRangeBonus(game, drawn, player, text);
            TryApplyFuelCoupling(game, player, text);
            TryApplyShipNudge(game, drawn, text, choice);
        }

        private static bool IsDisgruntleMoral(string text) =>
            Contains(text, "Moral Crew become Disgruntled")
            || Contains(text, "Disgruntle all Moral Crew")
            || Contains(text, "All Moral Crew are Disgruntled");

        private static bool IsCustomsStashSeize(string text) =>
            Contains(text, "not in your Stash are seized")
            || (Contains(text, "Contraband")
                && Contains(text, "Fugitives")
                && Contains(text, "not in your Stash")
                && Contains(text, "seized"));

        private static bool ShouldIssueWarrant(string text, PlayerState player)
        {
            if (!Contains(text, "Warrant Issued"))
                return false;
            // Regulated Salvage FAKE ID branch is deferred (Otherwise: … Warrant Issued).
            if (Contains(text, "If you have FAKE ID") && Contains(text, "Otherwise"))
                return false;
            if (Contains(text, "If you are an Outlaw Ship"))
                return AlertTokenRules.IsOutlawShip(player);
            return true;
        }

        private static bool TryPlanCustomsStashKeep(
            PlayerState player,
            NavResolveChoice? choice,
            out int keepContraband,
            out int keepFugitives,
            out string? error)
        {
            error = null;
            keepContraband = 0;
            keepFugitives = 0;
            var stash = System.Math.Max(0, player.StashHold);
            var total = player.Contraband + player.Fugitives;
            var keep = System.Math.Min(total, stash);

            var choiceContra = choice?.KeepInStashContraband ?? -1;
            var choiceFug = choice?.KeepInStashFugitives ?? -1;
            if (choiceContra >= 0 || choiceFug >= 0)
            {
                keepContraband = System.Math.Max(0, choiceContra);
                keepFugitives = System.Math.Max(0, choiceFug);
                if (keepContraband > player.Contraband || keepFugitives > player.Fugitives)
                {
                    error = "Stash keep exceeds Contraband or Fugitives on board.";
                    return false;
                }
                if (keepContraband + keepFugitives != keep)
                {
                    error = $"Customs stash keep must total {keep} (StashHold {stash}).";
                    return false;
                }
                return true;
            }

            // Auto-pack: protect Contraband first, then Fugitives (Corvette-style free rearrange).
            keepContraband = System.Math.Min(player.Contraband, keep);
            keepFugitives = keep - keepContraband;
            return true;
        }

        private static bool TryPlanGoodsSeize(
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            out int seizeFuel,
            out int seizeParts,
            out int seizeCargo,
            out int seizeContra,
            out int seized,
            out string? error)
        {
            seizeFuel = 0;
            seizeParts = 0;
            seizeCargo = 0;
            seizeContra = 0;
            seized = 0;
            error = null;

            var match = SeizeGoodsNotInStash.Match(text);
            if (!match.Success)
                return true;

            var n = int.Parse(match.Groups[1].Value);
            var unprotected = GoodsTokensNotInStash(player);
            var toSeize = System.Math.Min(n, unprotected);
            if (toSeize <= 0)
                return true;

            var choiceFuel = choice?.SeizeGoodsFuel ?? -1;
            var choiceParts = choice?.SeizeGoodsParts ?? -1;
            var choiceCargo = choice?.SeizeGoodsCargo ?? -1;
            var choiceContra = choice?.SeizeGoodsContraband ?? -1;
            if (choiceFuel >= 0 || choiceParts >= 0 || choiceCargo >= 0 || choiceContra >= 0)
            {
                seizeFuel = System.Math.Max(0, choiceFuel);
                seizeParts = System.Math.Max(0, choiceParts);
                seizeCargo = System.Math.Max(0, choiceCargo);
                seizeContra = System.Math.Max(0, choiceContra);
                var sum = seizeFuel + seizeParts + seizeCargo + seizeContra;
                if (sum != toSeize)
                {
                    error = $"Seize {toSeize} Goods requires a Goods composition choice totaling {toSeize}.";
                    return false;
                }
                if (seizeFuel > player.Fuel
                    || seizeParts > player.Parts
                    || seizeCargo > player.Cargo
                    || seizeContra > player.Contraband)
                {
                    error = "Seize Goods exceeds tokens on board.";
                    return false;
                }
                if (!CanRemoveFromUnprotected(player, seizeFuel, seizeParts, seizeCargo, seizeContra))
                {
                    error = "Cannot seize Goods protected in Stash.";
                    return false;
                }
                seized = toSeize;
                return true;
            }

            AutoSeizeUnprotectedGoods(
                player,
                toSeize,
                out seizeFuel,
                out seizeParts,
                out seizeCargo,
                out seizeContra);
            seized = seizeFuel + seizeParts + seizeCargo + seizeContra;
            return true;
        }

        /// <summary>
        /// Director's Cut Piracy: Goods = Fuel, Parts, Cargo, Contraband. Free rearrange into Stash;
        /// stash capacity uses Hold packing (2 Fuel/Parts per space).
        /// </summary>
        private static int GoodsTokensNotInStash(PlayerState player)
        {
            var total = player.Fuel + player.Parts + player.Cargo + player.Contraband;
            return System.Math.Max(0, total - MaxGoodsProtectedInStash(player));
        }

        private static int MaxGoodsProtectedInStash(PlayerState player)
        {
            var stash = System.Math.Max(0, player.StashHold);
            if (stash == 0)
                return 0;

            var half = player.Fuel + player.Parts;
            var halfProtected = System.Math.Min(half, stash * HoldSpace.FuelOrPartsPerHold);
            var slotsUsedByHalf = (halfProtected + HoldSpace.FuelOrPartsPerHold - 1) / HoldSpace.FuelOrPartsPerHold;
            var fullSlotsLeft = stash - slotsUsedByHalf;
            var fullProtected = System.Math.Min(
                player.Cargo + player.Contraband,
                System.Math.Max(0, fullSlotsLeft));
            return halfProtected + fullProtected;
        }

        private static void AutoSeizeUnprotectedGoods(
            PlayerState player,
            int toSeize,
            out int seizeFuel,
            out int seizeParts,
            out int seizeCargo,
            out int seizeContra)
        {
            UnprotectedGoods(player, out var openFuel, out var openParts, out var openCargo, out var openContra);
            seizeFuel = 0;
            seizeParts = 0;
            seizeCargo = 0;
            seizeContra = 0;
            var remaining = toSeize;
            // Prefer seizing full-slot Goods first (Contraband, Cargo), then Parts, then Fuel.
            Take(ref remaining, openContra, ref seizeContra);
            Take(ref remaining, openCargo, ref seizeCargo);
            Take(ref remaining, openParts, ref seizeParts);
            Take(ref remaining, openFuel, ref seizeFuel);
        }

        private static void UnprotectedGoods(
            PlayerState player,
            out int openFuel,
            out int openParts,
            out int openCargo,
            out int openContra)
        {
            var stash = System.Math.Max(0, player.StashHold);
            var half = player.Fuel + player.Parts;
            var halfProtected = System.Math.Min(half, stash * HoldSpace.FuelOrPartsPerHold);
            var slotsUsedByHalf = (halfProtected + HoldSpace.FuelOrPartsPerHold - 1) / HoldSpace.FuelOrPartsPerHold;
            var fullSlotsLeft = System.Math.Max(0, stash - slotsUsedByHalf);
            var protectContra = System.Math.Min(player.Contraband, fullSlotsLeft);
            fullSlotsLeft -= protectContra;
            var protectCargo = System.Math.Min(player.Cargo, fullSlotsLeft);
            // Split half-protection across Fuel then Parts.
            var protectFuel = System.Math.Min(player.Fuel, halfProtected);
            var protectParts = System.Math.Min(player.Parts, halfProtected - protectFuel);
            openFuel = player.Fuel - protectFuel;
            openParts = player.Parts - protectParts;
            openCargo = player.Cargo - protectCargo;
            openContra = player.Contraband - protectContra;
        }

        private static void Take(ref int remaining, int available, ref int taken)
        {
            if (remaining <= 0 || available <= 0)
                return;
            taken = System.Math.Min(remaining, available);
            remaining -= taken;
        }

        private static bool CanRemoveFromUnprotected(
            PlayerState player,
            int seizeFuel,
            int seizeParts,
            int seizeCargo,
            int seizeContra)
        {
            if (seizeFuel < 0 || seizeParts < 0 || seizeCargo < 0 || seizeContra < 0)
                return false;
            UnprotectedGoods(player, out var openFuel, out var openParts, out var openCargo, out var openContra);
            return seizeFuel <= openFuel
                && seizeParts <= openParts
                && seizeCargo <= openCargo
                && seizeContra <= openContra;
        }

        private static int PlannedTakeParts(string text)
        {
            var match = TakeParts.Match(text);
            if (!match.Success)
                return 0;
            if (match.Groups[1].Success)
                return int.Parse(match.Groups[1].Value);
            if (match.Groups[2].Success)
                return int.Parse(match.Groups[2].Value);
            return 0;
        }

        private static bool TryPlanGoodsLoad(
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            out int addFuel,
            out int addParts,
            out int addCargo,
            out int addContra,
            out int loaded,
            out string? error)
        {
            addFuel = 0;
            addParts = 0;
            addCargo = 0;
            addContra = 0;
            loaded = 0;
            error = null;

            // Regulated Salvage FAKE ID vs Otherwise Load branch is deferred.
            if (Contains(text, "If you have FAKE ID") && Contains(text, "Otherwise"))
                return true;

            if (Contains(text, "Load no Goods"))
                return true;

            var goods = LoadGoodsCount.Match(text);
            if (goods.Success)
            {
                var n = int.Parse(goods.Groups[1].Value);
                if (n <= 0)
                    return true;
                addFuel = choice?.LoadGoodsFuel ?? 0;
                addParts = choice?.LoadGoodsParts ?? 0;
                addCargo = choice?.LoadGoodsCargo ?? 0;
                addContra = choice?.LoadGoodsContraband ?? 0;
                var sum = addFuel + addParts + addCargo + addContra;
                if (sum != n)
                {
                    error = $"Load {n} Goods requires a Goods composition choice totaling {n}.";
                    return false;
                }
                if (!HoldSpace.TryExplain(
                    player,
                    out error,
                    addFuel: addFuel,
                    addParts: addParts,
                    addCargo: addCargo,
                    addContraband: addContra))
                    return false;
                loaded = n;
                return true;
            }

            var upTo = LoadUpToTyped.Match(text);
            if (upTo.Success)
            {
                var max = int.Parse(upTo.Groups[1].Value);
                var kind = NormalizeLoadKind(upTo.Groups[2].Value);
                var requested = choice?.LoadAmount ?? -1;
                var count = requested < 0
                    ? MaxTypedLoad(player, kind, max)
                    : requested;
                if (count < 0 || count > max)
                {
                    error = $"Load up to {max} {kind} requires LoadAmount between 0 and {max}.";
                    return false;
                }
                AssignTypedLoad(kind, count, ref addFuel, ref addParts, ref addCargo, ref addContra);
                if (!HoldSpace.TryExplain(
                    player,
                    out error,
                    addFuel: addFuel,
                    addParts: addParts,
                    addCargo: addCargo,
                    addContraband: addContra))
                    return false;
                loaded = count;
                return true;
            }

            if (LoadFuelNoLimit.IsMatch(text))
            {
                var requested = choice?.LoadAmount ?? -1;
                var count = requested < 0
                    ? MaxTypedLoad(player, "Fuel", int.MaxValue)
                    : requested;
                if (count < 0)
                {
                    error = "Load Fuel, no limit requires a non-negative LoadAmount.";
                    return false;
                }
                addFuel = count;
                if (!HoldSpace.TryExplain(player, out error, addFuel: addFuel))
                    return false;
                loaded = count;
                return true;
            }

            var typed = LoadTypedGoods.Match(text);
            if (!typed.Success)
                return true;

            var exact = int.Parse(typed.Groups[1].Value);
            var exactKind = NormalizeLoadKind(typed.Groups[2].Value);
            AssignTypedLoad(exactKind, exact, ref addFuel, ref addParts, ref addCargo, ref addContra);

            if (!HoldSpace.TryExplain(
                player,
                out error,
                addFuel: addFuel,
                addParts: addParts,
                addCargo: addCargo,
                addContraband: addContra))
                return false;
            loaded = exact;
            return true;
        }

        private static string NormalizeLoadKind(string kind)
        {
            if (kind.Equals("Part", StringComparison.OrdinalIgnoreCase)
                || kind.Equals("Parts", StringComparison.OrdinalIgnoreCase))
                return "Parts";
            if (kind.Equals("Fuel", StringComparison.OrdinalIgnoreCase))
                return "Fuel";
            if (kind.Equals("Cargo", StringComparison.OrdinalIgnoreCase))
                return "Cargo";
            return "Contraband";
        }

        private static void AssignTypedLoad(
            string kind,
            int count,
            ref int addFuel,
            ref int addParts,
            ref int addCargo,
            ref int addContra)
        {
            if (kind.Equals("Fuel", StringComparison.OrdinalIgnoreCase))
                addFuel = count;
            else if (kind.Equals("Parts", StringComparison.OrdinalIgnoreCase))
                addParts = count;
            else if (kind.Equals("Cargo", StringComparison.OrdinalIgnoreCase))
                addCargo = count;
            else
                addContra = count;
        }

        /// <summary>
        /// Max additional tokens of one Goods type that still Fit, capped by <paramref name="max"/>.
        /// Fuel/Parts share half-slots (2 per hold); Cargo/Contraband use full slots.
        /// </summary>
        private static int MaxTypedLoad(PlayerState player, string kind, int max)
        {
            if (max <= 0)
                return 0;
            var lo = 0;
            var hi = max == int.MaxValue ? 64 : max;
            // Grow upper bound for no-limit Fuel until it no longer Fits.
            if (max == int.MaxValue)
            {
                while (TypedLoadFits(player, kind, hi) && hi < 10_000)
                    hi *= 2;
                if (TypedLoadFits(player, kind, hi))
                    return hi;
            }

            var best = 0;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (TypedLoadFits(player, kind, mid))
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return System.Math.Min(best, max == int.MaxValue ? best : max);
        }

        private static bool TypedLoadFits(PlayerState player, string kind, int count)
        {
            if (count <= 0)
                return true;
            var addFuel = 0;
            var addParts = 0;
            var addCargo = 0;
            var addContra = 0;
            AssignTypedLoad(kind, count, ref addFuel, ref addParts, ref addCargo, ref addContra);
            return HoldSpace.Fits(
                player,
                addFuel: addFuel,
                addParts: addParts,
                addCargo: addCargo,
                addContraband: addContra);
        }

        private static readonly Regex BuyFuelPartsContra = new Regex(
            @"You may buy Fuel for \$(\d+),\s*Parts for \$(\d+)\s+or up to (\d+) Contraband for \$(\d+) each",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BuyFuelPartsCargo = new Regex(
            @"You may purchase Fuel for \$(\d+),\s*Parts for \$(\d+)\s+and up to (\d+) Cargo for \$(\d+) each",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SellPartsUpTo = new Regex(
            @"You may sell up to (\d+) Parts for \$(\d+) per Part",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TakeCryBabyDiscard = new Regex(
            @"take one\s+""Cry Baby""\s+card from any Supply Deck discard pile",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TakeCrewAnyDiscard = new Regex(
            @"take 1 Crew Card from any discard pile",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TakeUpgradeAnyDiscard = new Regex(
            @"Take 1 Ship Upgrade from any discard pile",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TakeCrewNamedDiscard = new Regex(
            @"Take 1 Crew from ([A-Za-z][A-Za-z\s]*?)(?:'s)? Discard Pile",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MoralRangeBonus = new Regex(
            @"Add 1 to the Range of this Fly Action for each Moral Crew",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PlanetaryRangeBonus = new Regex(
            @"\+(\d+) to Ship's Range this turn",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool CanApplyBuyOnTheGo(
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            if (!TryPlanBuyOnTheGo(player, text, choice, out _, out _, out _, out _, out _, out error))
                return false;
            return true;
        }

        private static bool TryPlanBuyOnTheGo(
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            out int buyFuel,
            out int buyParts,
            out int buyCargo,
            out int buyContra,
            out int cost,
            out string? error)
        {
            buyFuel = 0;
            buyParts = 0;
            buyCargo = 0;
            buyContra = 0;
            cost = 0;
            error = null;

            var contra = BuyFuelPartsContra.Match(text);
            var cargo = BuyFuelPartsCargo.Match(text);
            if (!contra.Success && !cargo.Success)
                return true;

            buyFuel = System.Math.Max(0, choice?.BuyFuel ?? 0);
            buyParts = System.Math.Max(0, choice?.BuyParts ?? 0);
            buyCargo = System.Math.Max(0, choice?.BuyCargo ?? 0);
            buyContra = System.Math.Max(0, choice?.BuyContraband ?? 0);

            int fuelPrice;
            int partsPrice;
            int unitPrice;
            int unitCap;
            bool isContra;
            if (contra.Success)
            {
                fuelPrice = int.Parse(contra.Groups[1].Value);
                partsPrice = int.Parse(contra.Groups[2].Value);
                unitCap = int.Parse(contra.Groups[3].Value);
                unitPrice = int.Parse(contra.Groups[4].Value);
                isContra = true;
                if (buyCargo > 0)
                {
                    error = "Rogue Trader does not sell Cargo.";
                    return false;
                }
                if (buyContra > unitCap)
                {
                    error = $"May buy at most {unitCap} Contraband.";
                    return false;
                }
            }
            else
            {
                fuelPrice = int.Parse(cargo.Groups[1].Value);
                partsPrice = int.Parse(cargo.Groups[2].Value);
                unitCap = int.Parse(cargo.Groups[3].Value);
                unitPrice = int.Parse(cargo.Groups[4].Value);
                isContra = false;
                if (buyContra > 0)
                {
                    error = "Freighter Convoy does not sell Contraband.";
                    return false;
                }
                if (buyCargo > unitCap)
                {
                    error = $"May buy at most {unitCap} Cargo.";
                    return false;
                }
            }

            var units = isContra ? buyContra : buyCargo;
            cost = buyFuel * fuelPrice + buyParts * partsPrice + units * unitPrice;
            if (cost == 0)
                return true;
            if (player.Cash < cost)
            {
                error = $"Need ${cost}, have ${player.Cash}.";
                return false;
            }
            if (!HoldSpace.TryExplain(
                player,
                out error,
                addFuel: buyFuel,
                addParts: buyParts,
                addCargo: buyCargo,
                addContraband: buyContra))
                return false;
            return true;
        }

        private static void TryApplyBuyOnTheGo(PlayerState player, string text, NavResolveChoice? choice)
        {
            if (!TryPlanBuyOnTheGo(
                player,
                text,
                choice,
                out var buyFuel,
                out var buyParts,
                out var buyCargo,
                out var buyContra,
                out var cost,
                out _))
                return;
            if (cost == 0 && buyFuel == 0 && buyParts == 0 && buyCargo == 0 && buyContra == 0)
                return;
            player.Cash -= cost;
            player.Fuel += buyFuel;
            player.Parts += buyParts;
            player.Cargo += buyCargo;
            player.Contraband += buyContra;
        }

        private static bool CanApplySellParts(
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            var match = SellPartsUpTo.Match(text);
            if (!match.Success)
                return true;
            var cap = int.Parse(match.Groups[1].Value);
            var sell = System.Math.Max(0, choice?.SellParts ?? 0);
            if (sell > cap)
            {
                error = $"May sell at most {cap} Parts.";
                return false;
            }
            if (sell > player.Parts)
            {
                error = "Not enough Parts to sell.";
                return false;
            }
            return true;
        }

        private static void TryApplySellParts(PlayerState player, string text, NavResolveChoice? choice)
        {
            var match = SellPartsUpTo.Match(text);
            if (!match.Success)
                return;
            var price = int.Parse(match.Groups[2].Value);
            var sell = System.Math.Max(0, choice?.SellParts ?? 0);
            if (sell <= 0)
                return;
            player.Parts -= sell;
            player.Cash += sell * price;
        }

        private enum DiscardGrabKind
        {
            None,
            CryBaby,
            CrewAny,
            UpgradeAny,
            CrewPlanet
        }

        private static DiscardGrabKind ParseDiscardGrab(string text, out string? planetHint)
        {
            planetHint = null;
            if (TakeCryBabyDiscard.IsMatch(text))
                return DiscardGrabKind.CryBaby;
            if (TakeUpgradeAnyDiscard.IsMatch(text))
                return DiscardGrabKind.UpgradeAny;
            if (TakeCrewAnyDiscard.IsMatch(text))
                return DiscardGrabKind.CrewAny;
            var named = TakeCrewNamedDiscard.Match(text);
            if (named.Success)
            {
                var raw = named.Groups[1].Value.Trim();
                if (raw.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
                    planetHint = raw.Substring(0, raw.Length - 2).Trim();
                else if (raw.EndsWith("s'", StringComparison.OrdinalIgnoreCase))
                    planetHint = raw.Substring(0, raw.Length - 2).Trim();
                else
                    planetHint = raw;
                return DiscardGrabKind.CrewPlanet;
            }
            return DiscardGrabKind.None;
        }

        private static bool CanApplyDiscardGrab(
            GameState game,
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            bool optional,
            out string? error)
        {
            error = null;
            var kind = ParseDiscardGrab(text, out var planetHint);
            if (kind == DiscardGrabKind.None)
                return true;

            var cardId = choice?.TakeFromDiscardCardId;
            if (string.IsNullOrWhiteSpace(cardId))
            {
                // Optional grabs ("You may" / "If you have space") may skip.
                if (optional || Contains(text, "You may") || Contains(text, "If you have space"))
                    return true;
                // Printed Take with nothing available: skip rather than invent a card.
                if (!AnyMatchingDiscardAvailable(game, kind, planetHint))
                    return true;
                error = "Discard-pile grab requires TakeFromDiscardCardId.";
                return false;
            }

            if (game.SupplyDecks == null)
            {
                error = "Supply decks have not been loaded.";
                return false;
            }

            if (!TryResolveDiscardCard(
                game,
                choice,
                planetHint,
                cardId!,
                kind,
                out var market,
                out var card,
                out error))
                return false;

            return CanReceiveDiscardCard(game, player, card, market.Planet, out error);
        }

        private static bool AnyMatchingDiscardAvailable(
            GameState game,
            DiscardGrabKind kind,
            string? planetHint)
        {
            if (game.SupplyDecks == null)
                return false;
            IEnumerable<SupplyMarket> markets = game.SupplyDecks.Markets;
            if (!string.IsNullOrWhiteSpace(planetHint)
                && game.SupplyDecks.TryGet(planetHint!, out var one))
                markets = new[] { one };

            foreach (var market in markets)
            {
                foreach (var card in market.Discard)
                {
                    if (kind == DiscardGrabKind.CryBaby
                        && string.Equals(card.Name, "Cry Baby", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if ((kind == DiscardGrabKind.CrewAny || kind == DiscardGrabKind.CrewPlanet)
                        && card.Kind == SupplyKind.Crew)
                        return true;
                    if (kind == DiscardGrabKind.UpgradeAny && card.Kind == SupplyKind.ShipUpgrade)
                        return true;
                }
            }
            return false;
        }

        private static void TryApplyDiscardGrab(
            GameState game,
            PlayerState player,
            string text,
            NavResolveChoice? choice,
            bool optional)
        {
            var kind = ParseDiscardGrab(text, out var planetHint);
            if (kind == DiscardGrabKind.None)
                return;
            var cardId = choice?.TakeFromDiscardCardId;
            if (string.IsNullOrWhiteSpace(cardId))
                return;
            if (game.SupplyDecks == null)
                return;
            if (!TryResolveDiscardCard(
                game,
                choice,
                planetHint,
                cardId!,
                kind,
                out var market,
                out var card,
                out _))
                return;
            if (!CanReceiveDiscardCard(game, player, card, market.Planet, out _))
                return;
            if (!market.TryTakeFromDiscard(card.Id, out var taken))
                return;
            GiveDiscardCard(game, player, taken, market.Planet);
        }

        private static bool TryResolveDiscardCard(
            GameState game,
            NavResolveChoice? choice,
            string? planetHint,
            string cardId,
            DiscardGrabKind kind,
            out SupplyMarket market,
            out SupplyCard card,
            out string? error)
        {
            market = null!;
            card = null!;
            error = null;
            var planet = choice?.TakeFromDiscardPlanet ?? planetHint;

            if (!string.IsNullOrWhiteSpace(planet))
            {
                if (!game.SupplyDecks!.TryGet(planet!, out market))
                {
                    error = $"Unknown Supply planet '{planet}'.";
                    return false;
                }
                if (!market.TryFindInDiscard(cardId, out card))
                {
                    error = $"'{cardId}' is not in {market.Planet}'s discard pile.";
                    return false;
                }
            }
            else if (!game.SupplyDecks!.TryFindInAnyDiscard(cardId, out market, out card))
            {
                error = $"'{cardId}' is not in any Supply discard pile.";
                return false;
            }

            if (kind == DiscardGrabKind.CryBaby
                && !string.Equals(card.Name, "Cry Baby", StringComparison.OrdinalIgnoreCase))
            {
                error = "Damaged Spy Satellite requires a Cry Baby card.";
                return false;
            }
            if ((kind == DiscardGrabKind.CrewAny || kind == DiscardGrabKind.CrewPlanet)
                && card.Kind != SupplyKind.Crew)
            {
                error = "Expected a Crew card from the discard pile.";
                return false;
            }
            if (kind == DiscardGrabKind.UpgradeAny && card.Kind != SupplyKind.ShipUpgrade)
            {
                error = "Expected a Ship Upgrade from the discard pile.";
                return false;
            }
            return true;
        }

        private static bool CanReceiveDiscardCard(
            GameState game,
            PlayerState player,
            SupplyCard card,
            string planet,
            out string? error)
        {
            error = null;
            switch (card.Kind)
            {
                case SupplyKind.Crew:
                    if (player.Roster.Count >= player.Roster.MaxCrew)
                    {
                        error = $"Roster is full ({player.Roster.MaxCrew}).";
                        return false;
                    }
                    if (game.Crew == null || !game.Crew.TryGet(card.Id, out _))
                    {
                        error = $"Crew card '{card.Id}' is not in the catalog.";
                        return false;
                    }
                    return true;
                case SupplyKind.ShipUpgrade:
                    return true;
                default:
                    error = $"Cannot take '{card.Kind}' via this Nav discard grab.";
                    return false;
            }
        }

        private static void GiveDiscardCard(GameState game, PlayerState player, SupplyCard card, string planet)
        {
            switch (card.Kind)
            {
                case SupplyKind.Crew:
                    if (game.Crew != null && game.Crew.TryGet(card.Id, out var crew))
                    {
                        if (player.Roster.TryHire(crew, out _))
                        {
                            DeceptiveCrew.AfterHired(game, crew.Name);
                            if (crew.Wanted)
                                ActiveAlertRules.OnWantedCrewHired(game, planet);
                        }
                    }
                    break;
                case SupplyKind.ShipUpgrade:
                    player.ShipUpgrades.Add(card.Id);
                    break;
            }
        }

        private static void TryApplyRangeBonus(
            GameState game,
            DrawnNav drawn,
            PlayerState player,
            string text)
        {
            if (MoralRangeBonus.IsMatch(text))
            {
                game.FlyRangeBonusThisAction += System.Math.Max(0, player.Roster.MoralCount);
                return;
            }

            var planetary = PlanetaryRangeBonus.Match(text);
            if (!planetary.Success)
                return;
            if (!game.Map.TryGet(drawn.SectorId, out var sector) || !sector.IsPlanetary)
                return;
            game.FlyRangeBonusThisAction += int.Parse(planetary.Groups[1].Value);
        }

        private static void TryApplyFuelCoupling(GameState game, PlayerState player, string text)
        {
            if (!Contains(text, "Discard 1 Fuel for each additional Sector"))
                return;
            game.DiscardFuelPerExtraSectorThisFly = true;
            // Remaining queued Full Burn sectors are "additional" after Keep Flying.
            var extra = game.PendingNavDraws.Count;
            if (extra <= 0)
                return;
            var lost = System.Math.Min(extra, player.Fuel);
            player.Fuel -= lost;
        }

        private static bool CanApplyShipNudge(
            GameState game,
            DrawnNav drawn,
            string text,
            NavResolveChoice? choice,
            out string? error)
        {
            error = null;
            if (!Contains(text, "move your ship two Sectors"))
                return true;
            if (choice == null
                || string.IsNullOrWhiteSpace(choice.ShipNudgeViaSectorId)
                || string.IsNullOrWhiteSpace(choice.ShipNudgeToSectorId))
            {
                error = "Ship nudge requires a two-Sector path (via + destination).";
                return false;
            }
            return TryValidateShipNudgePath(
                game,
                drawn.SectorId,
                choice.ShipNudgeViaSectorId!,
                choice.ShipNudgeToSectorId!,
                out error);
        }

        private static void TryApplyShipNudge(
            GameState game,
            DrawnNav drawn,
            string text,
            NavResolveChoice? choice)
        {
            if (!Contains(text, "move your ship two Sectors"))
                return;
            if (choice == null
                || string.IsNullOrWhiteSpace(choice.ShipNudgeViaSectorId)
                || string.IsNullOrWhiteSpace(choice.ShipNudgeToSectorId))
                return;
            if (!TryValidateShipNudgePath(
                game,
                drawn.SectorId,
                choice.ShipNudgeViaSectorId!,
                choice.ShipNudgeToSectorId!,
                out _))
                return;

            // Two Mosey-like hops from the Nav draw Sector; no Nav draws for the nudge.
            var player = game.CurrentPlayer;
            player.SectorId = drawn.SectorId;
            if (!FlightEvade.TryMove(game, player, choice.ShipNudgeViaSectorId!, out _))
                return;
            FlightEvade.TryMove(game, player, choice.ShipNudgeToSectorId!, out _);
        }

        private static bool TryValidateShipNudgePath(
            GameState game,
            string fromSectorId,
            string viaSectorId,
            string toSectorId,
            out string? error)
        {
            var player = game.CurrentPlayer;
            var saved = player.SectorId;
            player.SectorId = fromSectorId;
            var ok = FlightEvade.CanMove(game, player, viaSectorId, out error);
            if (ok)
            {
                player.SectorId = viaSectorId;
                ok = FlightEvade.CanMove(game, player, toSectorId, out error);
            }
            player.SectorId = saved;
            return ok;
        }

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
