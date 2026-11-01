using IC2.Engine.Core;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;

namespace IC2.Engine.Economy;

/// <summary>Opt-in runtime automation, not an addition to the saved campaign schema.</summary>
public sealed record HumanSupportOptions(bool SupplyArmies = false, bool FundArmies = false,
    bool SupplyFleets = false, bool FundFleets = false)
{
    /// <summary>Whether any support option is selected.</summary>
    public bool Enabled => SupplyArmies || FundArmies || SupplyFleets || FundFleets;
}

/// <summary>A refused automatic attempt, retaining the ordinary handler's reason.</summary>
public sealed record HumanSupportFailure(string UnitId, string CommandKind, CommandRejection Rejection);

/// <summary>Accepted effects and typed failures; the caller owns publication and its RNG boundary.</summary>
public sealed record HumanSupportResult(GameState State, ValueList<DomainEvent> Events,
    ValueList<HumanSupportFailure> Failures);

/// <summary>Shared support rules for human turn-start and movement integration (T265).</summary>
public static class HumanAutoSupport
{
    /// <summary>One stable pass over the active human nation's armies, then deployed fleets.</summary>
    public static HumanSupportResult ForNation(GameState state, Ruleset ruleset, World world,
        HumanSupportOptions? options, IRng rng)
    {
        var work = new Work(state, ruleset, world, options, rng);
        if (work.Enabled)
        {
            foreach (var army in state.Armies) work.Army(army.Id);
            foreach (var fleet in state.Fleets) work.Fleet(fleet.Id);
        }
        return work.Result();
    }

    /// <summary>Support only the named army, including valid own fleet/army supply providers.</summary>
    public static HumanSupportResult ForArmy(GameState state, Ruleset ruleset, World world,
        HumanSupportOptions? options, IRng rng, string armyId)
    {
        var work = new Work(state, ruleset, world, options, rng);
        if (work.Enabled) work.Army(armyId);
        return work.Result();
    }

    /// <summary>Support only the named deployed fleet, without repairing or consuming moves.</summary>
    public static HumanSupportResult ForFleet(GameState state, Ruleset ruleset, World world,
        HumanSupportOptions? options, IRng rng, string fleetId)
    {
        var work = new Work(state, ruleset, world, options, rng);
        if (work.Enabled) work.Fleet(fleetId);
        return work.Result();
    }

    /// <summary>The existing army information-panel quarterly regular and mercenary cost.</summary>
    public static int ArmyPurseTarget(ArmyState army, Ruleset ruleset) =>
        Math.Clamp(ArmyUpkeep.Compute(army.Units, ruleset), 0, ruleset.Economy.PurseCapPerUnit);

    /// <summary>Owner-selected fleet target: one quarter's ship upkeep, within the ordinary purse cap.</summary>
    public static int FleetPurseTarget(FleetState fleet, Ruleset ruleset) =>
        Math.Clamp(ShipUpkeep.Compute(fleet.Ships, ruleset), 0, ruleset.Economy.PurseCapPerUnit);

    private sealed class Work
    {
        private GameState _state;
        private readonly Ruleset _rules;
        private readonly World _world;
        private readonly HumanSupportOptions _options;
        private readonly IRng _rng;
        private readonly List<DomainEvent> _events = new();
        private readonly List<HumanSupportFailure> _failures = new();
        public bool Enabled { get; }

        public Work(GameState state, Ruleset rules, World world, HumanSupportOptions? options, IRng rng)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(rng);
            _state = state;
            _rules = rules;
            _world = world;
            _options = options ?? new();
            _rng = rng;
            Enabled = _options.Enabled && state.NationById(state.ActiveNationId) is
                { Control: SeatControl.Human, Eliminated: false };
            if (Enabled && state.WorldId != world.Id)
                throw new ArgumentException("Human support requires the campaign's matching world.", nameof(world));
        }

        public void Army(string id)
        {
            if (_state.ArmyById(id) is not { } army || army.Nation != _state.ActiveNationId) return;
            if (_options.SupplyArmies)
            {
                foreach (var provider in SupplyProviders.ForArmy(_state, id, _rules))
                {
                    if (!provider.IsFree) continue;
                    army = _state.ArmyById(id)!;
                    var room = Math.Max(0, SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, _rules) - army.SupplyTons);
                    if (room == 0) break;
                    var stock = provider.Kind switch
                    {
                        SupplyProviderKind.City => _state.CityById(provider.Id)?.SupplyTons ?? 0,
                        SupplyProviderKind.Fleet => _state.FleetById(provider.Id)?.SupplyTons ?? 0,
                        SupplyProviderKind.Army => _state.ArmyById(provider.Id)?.SupplyTons ?? 0,
                        _ => 0,
                    };
                    var tons = Math.Min(room, Math.Max(0, stock));
                    if (tons == 0) continue;
                    Apply(new BuySupplyCommand(army.Nation, id,
                        provider.Kind == SupplyProviderKind.City ? provider.Id : null, tons,
                        provider.Kind == SupplyProviderKind.Fleet ? provider.Id : null,
                        provider.Kind == SupplyProviderKind.Army ? provider.Id : null), id);
                }
            }
            army = _state.ArmyById(id)!;
            if (_options.FundArmies) Fund(id, army.Money, ArmyPurseTarget(army, _rules));
        }

        public void Fleet(string id)
        {
            if (_state.FleetById(id) is not { IsUnderConstruction: false } fleet
                || fleet.Nation != _state.ActiveNationId) return;
            if (_options.SupplyFleets)
            {
                foreach (var original in _state.Cities)
                {
                    var city = _state.CityById(original.Id)!;
                    if (city.Owner != fleet.Nation || LandingTile.ChebyshevDistance(
                        new GridPoint(city.X, city.Y), new GridPoint(fleet.X, fleet.Y)) > _rules.Economy.CommandAdjacencyRadiusTiles)
                        continue;
                    fleet = _state.FleetById(id)!;
                    var room = Math.Max(0, SupplyCapacity.FleetCapacityTons(fleet.Ships, _rules) - fleet.SupplyTons);
                    if (room == 0) break;
                    var tons = Math.Min(room, Math.Max(0, city.SupplyTons));
                    if (tons > 0) Apply(new BuyFleetSupplyCommand(fleet.Nation, id, city.Id, null, tons), id);
                }
            }
            fleet = _state.FleetById(id)!;
            if (_options.FundFleets) Fund(id, fleet.Money, FleetPurseTarget(fleet, _rules));
        }

        private void Fund(string id, int money, int target)
        {
            var amount = (int)Math.Min(_rules.Economy.PurseCapPerUnit, Math.Max(0L, (long)target - money));
            if (amount > 0) Apply(new TransferMoneyCommand(_state.ActiveNationId, id, amount), id);
        }

        private void Apply(ICommand command, string id)
        {
            var buffer = new RecordingEventSink();
            var context = new CommandContext(_state, _rules, _world, command,
                _rng.ForStream(command.Kind), buffer);
            var outcome = command switch
            {
                BuySupplyCommand supply => new BuySupplyCommandHandler().Handle(supply, context),
                BuyFleetSupplyCommand fleetSupply => new BuyFleetSupplyCommandHandler().Handle(fleetSupply, context),
                TransferMoneyCommand money => new TransferMoneyCommandHandler().Handle(money, context),
                _ => throw new InvalidOperationException("Unsupported automatic support command."),
            };
            if (outcome.Rejection is { } refusal)
            {
                _failures.Add(new(id, command.Kind, refusal));
                return;
            }
            var next = outcome.AcceptedState ?? throw new InvalidOperationException("Support accepted without state.");
            if (next != _state) _state = next;
            _events.AddRange(buffer.Events);
        }

        public HumanSupportResult Result() => new(_state,
            new ValueList<DomainEvent>(_events), new ValueList<HumanSupportFailure>(_failures));
    }
}
