using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/tasks/T105.md</c> "A command moves money between the treasury and a purse", Done-when 1
/// through 6. The treasury-side tests start from the real, committed <c>classical-mediterranean</c> world
/// (Done-when 1 names it); the <c>via</c>-fleet and limit tests dispatch the real command through the real
/// engine dispatcher against hand-built states, so each unit, position and purse is exactly the case under
/// test.
/// </summary>
/// <remarks>
/// <para>
/// The per-move ceiling and the receiving purse's cap are both
/// <see cref="EconomyRules.PurseCapPerUnit"/> — the report's one "capped at 1,000 money per army or fleet"
/// sentence, kept twice by this task's first Hazard. The clamp tests assert against
/// <see cref="TreasuryPurseTransfer"/>'s own result for the same inputs rather than against a copied
/// formula.
/// </para>
/// <para>
/// <strong>Why Done-when 2 uses the toy scenario.</strong> No <c>classical-mediterranean</c> seat starts
/// with a fleet that has room under the 1,000 cap <em>and</em> is not touched by an earlier AI turn:
/// <c>carthage</c>'s <c>fleet-0</c> starts at exactly 1,000 (no room for the +100 the Done-when asks for)
/// and <c>ptolemaic</c>'s <c>fleet-1</c> is reached only after Seleucid's AI turn. The shipped
/// <c>toy-3city</c> scenario opens directly on its default human seat <c>north</c> with no AI turn first,
/// and <c>north-fleet-1</c> starts at 0 — the same "+100 in, then −50 back" shape as the army case, with
/// nothing else moving either account.
/// </para>
/// </remarks>
public sealed class TransferMoneyCommandTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    /// <summary>Rome is <c>classical-mediterranean</c>'s own turn-order seat 0, so the session opens on it.</summary>
    private static GameSession NewRomeSession() =>
        new(Classical.World, Classical.Ruleset, Classical.Scenario, seedOverride: null, humanSeatNationId: "rome");

    /// <summary>The toy scenario opens on its default human seat <c>north</c>, with no AI turn first.</summary>
    private static GameSession NewToySession() =>
        new(EconomyTestbed.Toy.World, EconomyTestbed.Toy.Ruleset, EconomyTestbed.Toy.Scenario, seedOverride: null);

    private static CommandDispatcher Dispatcher() =>
        new(SystemRegistry.FromEngineAssembly(), EconomyTestbed.Ruleset, EconomyTestbed.Toy.World, NullEventSink.Instance);

    private static ArmyState Army(string id, string nation, int x, int y, int money) =>
        new(id, nation, x, y, Moves: 5, Morale: 60, money, SupplyTons: 0, CoveredTileCode: null,
            AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 1000, 6, "Test Battalion")));

    private static FleetState Fleet(
        string id, string nation, int x, int y, int money, string? carriedArmyId = null) =>
        new(id, nation, x, y, Moves: 5, Ships: 10, ConditionPercent: 90, money, SupplyTons: 0,
            ConstructionTicksRemaining: null, BuildCityId: null, carriedArmyId, CoveredTileCode: null);

    private static GameState WithNorthTreasury(GameState state, int treasury)
    {
        var nations = state.Nations.Select(n =>
            string.Equals(n.Id, "north", StringComparison.Ordinal) ? n with { Treasury = treasury } : n);
        return state with { Nations = ValueList.From(nations) };
    }

    // ---- Done-when 1 ----

    /// <summary>
    /// On the classical-mediterranean scenario as Rome, <c>transfer-money army-0 100</c> moves exactly 100
    /// from Rome's treasury into that army's purse, and <c>transfer-money army-0 -50</c> moves exactly 50
    /// back; <c>treasury + purse</c> is invariant across both. Rome opens the session (turn-order seat 0),
    /// so no AI turn runs first.
    /// </summary>
    [Fact]
    public void OnClassicalRome_ArmyTransfersInAndOut_ConserveTreasuryPlusPurse()
    {
        var session = NewRomeSession();
        Assert.Equal("rome", session.State.ActiveNationId);

        var armyId = session.State.Armies.First(a => string.Equals(a.Nation, "rome", StringComparison.Ordinal)).Id;
        var beforeNation = session.State.NationById("rome")!;
        var beforeArmy = session.State.ArmyById(armyId)!;
        Assert.Equal(2200, beforeNation.Treasury); // the committed starting value; pinned so the sums below are readable.
        Assert.Equal(100, beforeArmy.Money);

        var intoPurse = session.Submit($"transfer-money {armyId} 100");
        Assert.Contains($"{armyId} received 100 talents from the treasury.", intoPurse.Lines);
        Assert.Equal(beforeArmy.Money + 100, session.State.ArmyById(armyId)!.Money);
        Assert.Equal(beforeNation.Treasury - 100, session.State.NationById("rome")!.Treasury);

        var back = session.Submit($"transfer-money {armyId} -50");
        Assert.Contains($"the treasury received 50 talents from {armyId}.", back.Lines);
        Assert.Equal(beforeArmy.Money + 50, session.State.ArmyById(armyId)!.Money);
        Assert.Equal(beforeNation.Treasury - 50, session.State.NationById("rome")!.Treasury);

        Assert.Equal(
            beforeNation.Treasury + beforeArmy.Money,
            session.State.NationById("rome")!.Treasury + session.State.ArmyById(armyId)!.Money);
    }

    // ---- Done-when 2 ----

    /// <summary>The same +100/−50 shape for a fleet's own purse — see the class remarks for why toy-3city.</summary>
    [Fact]
    public void OnToyNorth_FleetTransfersInAndOut_ConserveTreasuryPlusPurse()
    {
        var session = NewToySession();
        Assert.Equal("north", session.State.ActiveNationId);

        var beforeNation = session.State.NationById("north")!;
        var beforeFleet = session.State.FleetById("north-fleet-1")!;
        Assert.Equal(500, beforeNation.Treasury);
        Assert.Equal(0, beforeFleet.Money);

        var intoPurse = session.Submit("transfer-money north-fleet-1 100");
        Assert.Contains("north-fleet-1 received 100 talents from the treasury.", intoPurse.Lines);
        Assert.Equal(beforeFleet.Money + 100, session.State.FleetById("north-fleet-1")!.Money);
        Assert.Equal(beforeNation.Treasury - 100, session.State.NationById("north")!.Treasury);

        var back = session.Submit("transfer-money north-fleet-1 -50");
        Assert.Contains("the treasury received 50 talents from north-fleet-1.", back.Lines);
        Assert.Equal(beforeFleet.Money + 50, session.State.FleetById("north-fleet-1")!.Money);
        Assert.Equal(beforeNation.Treasury - 50, session.State.NationById("north")!.Treasury);

        Assert.Equal(
            beforeNation.Treasury + beforeFleet.Money,
            session.State.NationById("north")!.Treasury + session.State.FleetById("north-fleet-1")!.Money);
    }

    // ---- Done-when 3 ----

    /// <summary>
    /// An own fleet one tile from the army: <c>transfer-money &lt;army&gt; 100 via &lt;fleet&gt;</c> moves
    /// 100 from the fleet's purse to the army's, and <c>−50</c> moves 50 back. The two purses together are
    /// conserved and the treasury is untouched.
    /// </summary>
    [Fact]
    public void CoLocatedFleet_AsVia_MovesBetweenTheTwoPurses_AndLeavesTheTreasuryAlone()
    {
        var army = Army("via-army", "north", x: 3, y: 2, money: 100);
        var fleet = Fleet("via-fleet", "north", x: 3, y: 3, money: 300);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
        }, 500);

        var dispatcher = Dispatcher();

        var result = dispatcher.Dispatch(state, new TransferMoneyCommand("north", army.Id, 100, fleet.Id));
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(200, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(200, result.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(400, result.State.ArmyById(army.Id)!.Money + result.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(500, result.State.NationById("north")!.Treasury);

        var back = dispatcher.Dispatch(
            result.State, new TransferMoneyCommand("north", army.Id, -50, fleet.Id));
        Assert.True(back.IsAccepted, back.ToString());
        Assert.Equal(150, back.State.ArmyById(army.Id)!.Money);
        Assert.Equal(250, back.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(400, back.State.ArmyById(army.Id)!.Money + back.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(500, back.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// The scope's other co-location reading: the fleet carrying the named army is always a valid
    /// <c>via</c>, even when the army's own recorded land position is elsewhere.
    /// </summary>
    [Fact]
    public void CarryingFleet_AsVia_IsAccepted_EvenWhenTheArmyPositionIsElsewhere()
    {
        var fleet = Fleet("carrier", "north", x: 0, y: 5, money: 200, carriedArmyId: "aboard");
        var army = Army("aboard", "north", x: 3, y: 2, money: 100) with { AboardFleetId = fleet.Id };
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
        }, 500);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 100, fleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(200, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(100, result.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(500, result.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// The named unit is a fleet, and the other side a co-located own fleet: the two purses move, with
    /// balances asserted, and the treasury is untouched (review round 1, N2).
    /// </summary>
    [Fact]
    public void NamedFleet_WithVia_MovesBetweenTheTwoPurses_AndLeavesTheTreasuryAlone()
    {
        var named = Fleet("named-fleet", "north", x: 3, y: 3, money: 200);
        var via = Fleet("via-fleet", "north", x: 3, y: 4, money: 400);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList.Of(named, via),
        }, 500);

        var dispatcher = Dispatcher();

        var intoNamed = dispatcher.Dispatch(
            state, new TransferMoneyCommand("north", named.Id, 50, via.Id));
        Assert.True(intoNamed.IsAccepted, intoNamed.ToString());
        Assert.Equal(250, intoNamed.State.FleetById(named.Id)!.Money);
        Assert.Equal(350, intoNamed.State.FleetById(via.Id)!.Money);
        Assert.Equal(600, intoNamed.State.FleetById(named.Id)!.Money + intoNamed.State.FleetById(via.Id)!.Money);
        Assert.Equal(500, intoNamed.State.NationById("north")!.Treasury);

        var back = dispatcher.Dispatch(
            intoNamed.State, new TransferMoneyCommand("north", named.Id, -50, via.Id));
        Assert.True(back.IsAccepted, back.ToString());
        Assert.Equal(200, back.State.FleetById(named.Id)!.Money);
        Assert.Equal(400, back.State.FleetById(via.Id)!.Money);
        Assert.Equal(600, back.State.FleetById(named.Id)!.Money + back.State.FleetById(via.Id)!.Money);
        Assert.Equal(500, back.State.NationById("north")!.Treasury);
    }

    // ---- Done-when 4 ----

    /// <summary>A <c>via</c> fleet two tiles away is refused and the state is unchanged.</summary>
    [Fact]
    public void ViaFleetTwoTilesAway_IsRefused_WithTheStateUnchanged()
    {
        var army = Army("far-army", "north", x: 3, y: 2, money: 100);
        var fleet = Fleet("far-fleet", "north", x: 5, y: 5, money: 300);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
        }, 500);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 100, fleet.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(TransferMoneyRejections.ViaFleetNotWithinRange, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>A foreign <c>via</c> fleet is refused and the state is unchanged.</summary>
    [Fact]
    public void ForeignViaFleet_IsRefused_WithTheStateUnchanged()
    {
        var army = Army("own-army", "north", x: 3, y: 2, money: 100);
        var fleet = Fleet("south-fleet", "south", x: 3, y: 3, money: 300);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
        }, 500);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 100, fleet.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(TransferMoneyRejections.ViaFleetNotYours, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>The named unit itself as <c>via</c> is refused and the state is unchanged.</summary>
    [Fact]
    public void NamedUnitAsItsOwnVia_IsRefused_WithTheStateUnchanged()
    {
        var fleet = Fleet("self-fleet", "north", x: 3, y: 3, money: 300);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList.Of(fleet),
        }, 500);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", fleet.Id, 100, fleet.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(TransferMoneyRejections.ViaFleetIsTheUnit, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>An unknown <c>via</c> fleet id is refused and the state is unchanged (review round 1, N2).</summary>
    [Fact]
    public void UnknownViaFleet_IsRefused_WithTheStateUnchanged()
    {
        var army = Army("own-army", "north", x: 3, y: 2, money: 100);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList<FleetState>.Empty,
        }, 500);

        var result = Dispatcher().Dispatch(
            state, new TransferMoneyCommand("north", army.Id, 100, "no-such-fleet"));

        Assert.True(result.IsRejected);
        Assert.Equal(TransferMoneyRejections.UnknownViaFleet, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- Done-when 5 ----

    /// <summary>1,000 talents is accepted; 1,001 and −1,001 are refused, reading the ceiling from the ruleset.</summary>
    [Fact]
    public void MoveLimit_IsTheRulesetsPurseCapPerUnit()
    {
        var army = Army("limit-army", "north", x: 3, y: 2, money: 0);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList<FleetState>.Empty,
        }, treasury: 5000);

        var limit = EconomyTestbed.Ruleset.Economy.PurseCapPerUnit;
        var dispatcher = Dispatcher();

        var atLimit = dispatcher.Dispatch(state, new TransferMoneyCommand("north", army.Id, limit));
        Assert.True(atLimit.IsAccepted, atLimit.ToString());
        Assert.Equal(limit, atLimit.State.ArmyById(army.Id)!.Money);
        Assert.Equal(5000 - limit, atLimit.State.NationById("north")!.Treasury);

        var over = dispatcher.Dispatch(state, new TransferMoneyCommand("north", army.Id, limit + 1));
        Assert.True(over.IsRejected);
        Assert.Equal(TransferMoneyRejections.AmountExceedsMoveLimit, over.Code);
        Assert.Same(state, over.State);

        var under = dispatcher.Dispatch(state, new TransferMoneyCommand("north", army.Id, -(limit + 1)));
        Assert.True(under.IsRejected);
        Assert.Equal(TransferMoneyRejections.AmountExceedsMoveLimit, under.Code);
        Assert.Same(state, under.State);
    }

    /// <summary>
    /// A request past the receiving purse's cap is clamped exactly as
    /// <see cref="TreasuryPurseTransfer"/> clamps it, asserted against that function's own result.
    /// </summary>
    [Fact]
    public void RequestPastThePurseCap_ClampsExactlyAsTreasuryPurseTransfer()
    {
        var army = Army("capped-army", "north", x: 3, y: 2, money: 900);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList<FleetState>.Empty,
        }, treasury: 5000);

        var ruleset = EconomyTestbed.Ruleset;
        var expected = TreasuryPurseTransfer.TransferWithArmy(
            state.NationById("north")!, army, 500, ruleset);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 500));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(expected.Army.Money, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(expected.Nation.Treasury, result.State.NationById("north")!.Treasury);
        Assert.Equal(100, result.State.ArmyById(army.Id)!.Money - army.Money); // 900 + 500 capped to 1,000.
    }

    /// <summary>
    /// A request for more than the source holds is clamped to the treasury's balance, exactly as
    /// <see cref="TreasuryPurseTransfer"/> clamps it.
    /// </summary>
    [Fact]
    public void RequestPastTheSource_ClampsExactlyAsTreasuryPurseTransfer()
    {
        var army = Army("poor-source-army", "north", x: 3, y: 2, money: 0);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList<FleetState>.Empty,
        }, treasury: 50);

        var ruleset = EconomyTestbed.Ruleset;
        var expected = TreasuryPurseTransfer.TransferWithArmy(
            state.NationById("north")!, army, 200, ruleset);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 200));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(expected.Army.Money, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(expected.Nation.Treasury, result.State.NationById("north")!.Treasury);
        Assert.Equal(50, result.State.ArmyById(army.Id)!.Money - army.Money);
        Assert.Equal(0, result.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// Review round 1, B1, negative direction: the named army funds and the <c>via</c> fleet receives,
    /// so the cap must hold on the via fleet. 950 + 100 would pass the 1,000 cap, so only 50 moves.
    /// Asserted against <see cref="TreasuryPurseTransfer.TransferBetweenPurses"/>'s own result, with the
    /// receiving purse at or below the cap and the two purses conserved.
    /// </summary>
    [Fact]
    public void NegativeViaMove_IntoANearlyFullViaPurse_RespectsTheCap()
    {
        var army = Army("cap-army", "north", x: 3, y: 2, money: 100);
        var fleet = Fleet("cap-via", "north", x: 3, y: 3, money: 950);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
        }, 500);

        var ruleset = EconomyTestbed.Ruleset;
        var cap = ruleset.Economy.PurseCapPerUnit;
        var expected = TreasuryPurseTransfer.TransferBetweenPurses(fleet.Money, army.Money, -100, ruleset);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, -100, fleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.True(expected.FromMoney <= cap, $"via purse {expected.FromMoney} exceeds cap {cap}");
        Assert.True(result.State.FleetById(fleet.Id)!.Money <= cap);
        Assert.Equal(expected.FromMoney, result.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(expected.ToMoney, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(cap, result.State.FleetById(fleet.Id)!.Money); // 950 + the 50 that fits.
        Assert.Equal(50, result.State.ArmyById(army.Id)!.Money); // 100 - the 50 that moved.
        Assert.Equal(
            fleet.Money + army.Money,
            result.State.FleetById(fleet.Id)!.Money + result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(500, result.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// Review round 1, B1, positive direction: the <c>via</c> fleet funds and the named army receives, so
    /// the cap must hold on the named purse. 950 + 100 clamps to the 1,000 cap, moving only 50. Asserted
    /// against <see cref="TreasuryPurseTransfer.TransferBetweenPurses"/>'s own result, with the receiving
    /// purse at or below the cap and the two purses conserved.
    /// </summary>
    [Fact]
    public void PositiveViaMove_IntoANearlyFullNamedPurse_RespectsTheCap()
    {
        var army = Army("cap-army", "north", x: 3, y: 2, money: 950);
        var fleet = Fleet("cap-via", "north", x: 3, y: 3, money: 500);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
        }, 500);

        var ruleset = EconomyTestbed.Ruleset;
        var cap = ruleset.Economy.PurseCapPerUnit;
        var expected = TreasuryPurseTransfer.TransferBetweenPurses(fleet.Money, army.Money, 100, ruleset);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 100, fleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.True(expected.ToMoney <= cap, $"named purse {expected.ToMoney} exceeds cap {cap}");
        Assert.True(result.State.ArmyById(army.Id)!.Money <= cap);
        Assert.Equal(expected.FromMoney, result.State.FleetById(fleet.Id)!.Money);
        Assert.Equal(expected.ToMoney, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(450, result.State.FleetById(fleet.Id)!.Money); // 500 - the 50 that fit.
        Assert.Equal(cap, result.State.ArmyById(army.Id)!.Money); // 950 + the 50 that fit.
        Assert.Equal(
            fleet.Money + army.Money,
            result.State.FleetById(fleet.Id)!.Money + result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(500, result.State.NationById("north")!.Treasury);
    }

    /// <summary>
    /// Review round 1, N5: a positive move into a purse already over the cap must apply nothing rather
    /// than let <see cref="PurseAccounting.Credit"/>'s clamp-<em>down</em> move money backwards into the
    /// treasury. The result is asserted against <see cref="TreasuryPurseTransfer"/>'s own result.
    /// </summary>
    [Fact]
    public void PositiveMove_IntoAPurseAlreadyOverTheCap_AppliesNothing()
    {
        var army = Army("over-cap-army", "north", x: 3, y: 2, money: 1090);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList<FleetState>.Empty,
        }, treasury: 500);

        var ruleset = EconomyTestbed.Ruleset;
        var expected = TreasuryPurseTransfer.TransferWithArmy(
            state.NationById("north")!, army, 10, ruleset);

        var result = Dispatcher().Dispatch(state, new TransferMoneyCommand("north", army.Id, 10));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(0, expected.AppliedTalents);
        Assert.Equal(expected.Army.Money, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(expected.Nation.Treasury, result.State.NationById("north")!.Treasury);
        Assert.Equal(1090, result.State.ArmyById(army.Id)!.Money);
        Assert.Equal(500, result.State.NationById("north")!.Treasury);
    }

    // ---- Done-when 6 ----

    /// <summary>A foreign army, an unknown id and a zero amount are each refused with the state unchanged.</summary>
    [Fact]
    public void ForeignArmy_UnknownId_AndZeroAmount_AreRefused_WithTheStateUnchanged()
    {
        var foreign = Army("foreign-army", "south", x: 3, y: 2, money: 100);
        var own = Army("own-army", "north", x: 3, y: 2, money: 100);
        var state = WithNorthTreasury(EconomyTestbed.InitialState() with
        {
            Armies = ValueList.Of(own, foreign),
            Fleets = ValueList<FleetState>.Empty,
        }, 500);

        var dispatcher = Dispatcher();

        var foreignResult = dispatcher.Dispatch(state, new TransferMoneyCommand("north", foreign.Id, 100));
        Assert.True(foreignResult.IsRejected);
        Assert.Equal(TransferMoneyRejections.NotYourUnit, foreignResult.Code);
        Assert.Same(state, foreignResult.State);

        var unknownResult = dispatcher.Dispatch(state, new TransferMoneyCommand("north", "no-such-unit", 100));
        Assert.True(unknownResult.IsRejected);
        Assert.Equal(TransferMoneyRejections.UnknownUnit, unknownResult.Code);
        Assert.Same(state, unknownResult.State);

        var zeroResult = dispatcher.Dispatch(state, new TransferMoneyCommand("north", own.Id, 0));
        Assert.True(zeroResult.IsRejected);
        Assert.Equal(TransferMoneyRejections.InvalidAmount, zeroResult.Code);
        Assert.Same(state, zeroResult.State);
    }

    /// <summary>
    /// A non-integer amount never reaches the command layer: the parser prints its own usage line and the
    /// state is byte-for-byte unchanged.
    /// </summary>
    [Fact]
    public void NonIntegerAmount_IsRefusedAtTheParser_WithTheStateUnchanged()
    {
        var session = NewRomeSession();
        var before = GameStateHash.Compute(session.State);

        var output = session.Submit("transfer-money army-0 x");

        Assert.Contains("Usage: transfer-money", string.Join("\n", output.Lines));
        Assert.Equal(before, GameStateHash.Compute(session.State));
    }
}
