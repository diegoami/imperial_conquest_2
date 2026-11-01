using IC2.Engine.Core;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;

namespace IC2.Engine.Barbarians;

/// <summary>Authoritative role policy; no role is inferred from a display name or ordinary AI personality.</summary>
public static class BarbarianFaction
{
    /// <summary>Validate the optional role against the existing campaign and unit limits.</summary>
    public static void ValidateScenario(World world, Ruleset ruleset, Scenario scenario)
    {
        if (scenario.Barbarians is not { } config) return;
        var nation = world.NationById(config.NationId);
        if (world.Nations.Count > 16 || nation is null || nation.CapitalCityId is not null
            || !world.TurnOrder.Contains(config.NationId)
            || scenario.SeatFor(config.NationId)?.Control != SeatControl.Ai
            || nation.RecruitmentSlots is { Count: > 0 })
            throw new ArgumentException("Barbarians require one AI-only, capitalless nation within the 16-nation budget and no recruitment queue.", nameof(scenario));
        if (config.ChancePercent is < 0 or > 100 || config.ArmyCap <= 0 || config.FleetCap <= 0
            || config.FleetShips <= 0 || config.FleetShips > ruleset.Naval.JoinMaxShips
            || config.ArmyMorale < ruleset.Economy.SupplyMorale.MoraleFloor
            || config.ArmyMorale > ruleset.Economy.SupplyMorale.MoraleCeiling
            || config.ArrivalUnits.Count == 0 || config.ArrivalUnits.Count > ruleset.ArmyManagement.MaxUnitsPerArmy)
            throw new ArgumentException("Invalid barbarian arrival limits, morale or unit count.", nameof(scenario));
        long troops = 0;
        foreach (var unit in config.ArrivalUnits)
        {
            if (ruleset.UnitTypeById(unit.UnitTypeId) is null || unit.Troops <= 0 || unit.IsMercenary
                || unit.Quality < ruleset.Combat.QualityFloor || unit.Quality > ruleset.Combat.QualityCap)
                throw new ArgumentException("Barbarian arrivals require positive regular units with valid types and quality.", nameof(scenario));
            troops += unit.Troops;
        }
        if (troops > int.MaxValue || Naval.ArmyTransportCapacity.Exceeds(troops, config.FleetShips, ruleset, SeatControl.Ai))
            throw new ArgumentException("Barbarian arrival army must fit its transport fleet.", nameof(scenario));
    }

    /// <summary>Captured towns cannot create new barbarian troops or ships.</summary>
    public static CommandRejection? ProductionRefusal(GameState state, ICommand command) =>
        Is(state, command.IssuingNationId) && command is
            Recruitment.Commands.RecruitStandingUnitCommand or Recruitment.Commands.MobilizeRecruitSlotCommand
            or Recruitment.Commands.HireMercenaryCommand or Naval.Commands.OrderFleetCommand
            ? new(new RejectionCode("barbarians.no-production"), "Barbarian reinforcements arrive from outside the map; captured towns cannot recruit troops or build fleets.")
            : null;

    /// <summary>Whether this explicit saved faction marker identifies the nation.</summary>
    public static bool Is(GameState state, string id) => state.NationById(id)?.IsBarbarian == true;

    /// <summary>Keep all living regular/barbarian pairs at war, including after elimination/rebirth resets.</summary>
    public static GameState EnforceHostility(GameState state, Ruleset ruleset)
    {
        if (!state.Nations.Any(n => n.IsBarbarian)) return state;
        var relations = state.Relations;
        foreach (var faction in state.Nations.Where(n => n.IsBarbarian))
        foreach (var other in state.Nations)
        {
            if (other.Id == faction.Id || other.Eliminated) continue;
            if (relations.Get(faction.Id, other.Id) != ruleset.Diplomacy.StateCodes.War
                || relations.Get(other.Id, faction.Id) != ruleset.Diplomacy.StateCodes.War)
                relations = relations.WithRelation(faction.Id, other.Id, ruleset.Diplomacy.StateCodes.War);
        }
        return ReferenceEquals(relations, state.Relations) ? state : state with { Relations = relations };
    }

    /// <summary>Refuse diplomacy before it can transfer money, alter relations or emit an offer.</summary>
    public static CommandRejection? DiplomacyRefusal(GameState state, ICommand command)
    {
        if (!command.Kind.StartsWith("diplomacy.", StringComparison.Ordinal) || command is DeclareWarCommand) return null;
        var involved = Is(state, command.IssuingNationId) || (command switch
        {
            MakePeaceCommand c => Is(state, c.TargetNationId),
            ProposeTradeCommand c => Is(state, c.TargetNationId),
            ProposeAllianceCommand c => Is(state, c.TargetNationId),
            AiFormTradeCommand c => Is(state, c.PartnerNationId),
            AiFormAllianceCommand c => Is(state, c.PartnerNationId),
            AiSwapTradePartnerCommand c => Is(state, c.PoorerPartnerId) || Is(state, c.RicherCandidateId),
            AcceptPeaceTreatyCommand c => Is(state, c.WinnerNationId) || Is(state, c.LoserNationId),
            AcceptPendingOfferCommand => state.PendingOffer is { } offer && Is(state, offer.ProposingNationId),
            _ => false,
        });
        return involved ? new CommandRejection(NoDiplomacy, "Barbarians are always hostile and cannot make peace, trade or alliances.") : null;
    }

    /// <summary>Stable refusal for all permanent-hostility diplomatic entry points.</summary>
    public static readonly RejectionCode NoDiplomacy = new("barbarians.no-diplomacy");
}
