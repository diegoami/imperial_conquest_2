using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T107.md</c> "Change units: split, rename and disband a single unit", Done-when 1 through
/// 4.
/// </summary>
/// <remarks>
/// <para>
/// Done-when 1 runs against the real committed <c>classical-mediterranean</c> scenario and Rome's own
/// <c>army-0</c>, the same world the task's success golden uses. Done-when 2 through 4 dispatch the real
/// commands through the real engine dispatcher against hand-built states built on the shipped
/// <c>toy-3city</c> scenario (reused through <see cref="ArmiesTestbed"/>), so each unit, position and cap
/// boundary is exactly the case under test.
/// </para>
/// <para>
/// <strong>Every rejection asserts the whole state is untouched</strong> (<c>Assert.Same</c>), not one
/// field: <see cref="CommandResult.State"/> on a rejection is the very object that went in, and the
/// task's brief makes that the assertion, not a sample.
/// </para>
/// <para>
/// <strong>Every acceptance asserts conservation.</strong> A split conserves the source army's troops
/// exactly (the two parts sum to the original); a disband drops exactly the removed unit's troops and
/// changes nothing else in the army. Split boundaries follow the researched type-specific 2/5 source
/// threshold and 1/5 resulting-unit floor. Other designed defaults from issue #565 retain their own tests.
/// </para>
/// </remarks>
public sealed class ChangeUnitsCommandTests
{
    // ---- the classical scenario (Done-when 1) ----

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static CommandDispatcher ClassicalDispatcher() => new(
        SystemRegistry.FromEngineAssembly(), Classical.Ruleset, Classical.World, NullEventSink.Instance);

    private static GameState ClassicalInitial() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

    /// <summary>Rome's first army in the committed scenario — the state Done-when 1 names.</summary>
    private static ArmyState RomanArmy(GameState state) =>
        state.Armies.Single(a => string.Equals(a.Id, "army-0", StringComparison.Ordinal));

    private static GameState WithRomanFirstUnitTroops(GameState state, int troops)
    {
        var army = RomanArmy(state);
        var units = ValueList.From(army.Units.Select((unit, index) =>
            index == 0 ? unit with { Troops = troops } : unit));
        var updatedArmy = army with { Units = units };
        return state with
        {
            Armies = ValueList.From(state.Armies.Select(candidate =>
                candidate.Id == army.Id ? updatedArmy : candidate)),
        };
    }

    [Fact]
    public void SplitUnit_LightInfantryBelowTheOriginalMinimumIsRefusedOnTheClassicalScenario()
    {
        var state = WithRomanFirstUnitTroops(ClassicalInitial(), 4_800);
        var army = RomanArmy(state);

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitUnitCommand("rome", army.Id, 0, 2_400));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.TooSmall, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void SplitUnit_OnTheClassicalScenario_LeavesTwoUnitsOfTheSameTypeQualityAndMarkerAndConservesTroops()
    {
        var state = ClassicalInitial();
        state = WithRomanFirstUnitTroops(state, 6_000);
        var army = RomanArmy(state);
        var source = army.Units[0];
        var sourceTroops = source.Troops;
        var before = army.TotalTroops;

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitUnitCommand("rome", army.Id, 0, 3_000));

        Assert.True(result.IsAccepted, result.ToString());

        var updated = result.State.ArmyById(army.Id)!;
        Assert.Equal(army.Units.Count + 1, updated.Units.Count);

        var kept = updated.Units[0];
        var splitOff = updated.Units[^1];
        Assert.Equal(source.UnitTypeId, kept.UnitTypeId);
        Assert.Equal(source.UnitTypeId, splitOff.UnitTypeId);
        Assert.Equal(source.Quality, kept.Quality);
        Assert.Equal(source.Quality, splitOff.Quality);
        Assert.Equal(source.MercenaryLabel, kept.MercenaryLabel);
        Assert.Equal(source.MercenaryLabel, splitOff.MercenaryLabel);
        Assert.Equal(3_000, splitOff.Troops);
        Assert.Equal(before, updated.TotalTroops);
        Assert.Equal(sourceTroops, kept.Troops + splitOff.Troops);
        Assert.Equal(sourceTroops - 3_000, kept.Troops);
    }

    [Fact]
    public void RenameUnit_OnTheClassicalScenario_SetsTheNameAndChangesNothingElse()
    {
        var state = ClassicalInitial();
        var army = RomanArmy(state);
        const int target = 1; // non-zero, so an index off-by-one cannot pass silently.
        var expected = army.Units
            .Select((unit, index) => index == target ? unit with { Name = "Legio I" } : unit)
            .ToList();

        var result = ClassicalDispatcher().Dispatch(
            state, new RenameUnitCommand("rome", army.Id, target, "Legio I"));

        Assert.True(result.IsAccepted, result.ToString());

        // The whole unit list equals the original with only the target renamed: a mutation that renames
        // every unit, or reorders/removes one, fails here.
        var updated = result.State.ArmyById(army.Id)!;
        Assert.Equal(expected, updated.Units);
    }

    [Fact]
    public void DisbandUnit_OnTheClassicalScenario_RemovesTheUnitAndItsTroops()
    {
        var state = ClassicalInitial();
        var army = RomanArmy(state);
        var removed = army.Units[1];
        var before = army.TotalTroops;
        var expected = army.Units.Where((_, index) => index != 1).ToList();

        var result = ClassicalDispatcher().Dispatch(
            state, new DisbandUnitCommand("rome", army.Id, 1));

        Assert.True(result.IsAccepted, result.ToString());

        var updated = result.State.ArmyById(army.Id)!;
        Assert.Equal(expected, updated.Units);
        Assert.DoesNotContain(updated.Units, unit => unit == removed);
        Assert.Equal(before - removed.Troops, updated.TotalTroops);
        Assert.Equal(army.SupplyTons, updated.SupplyTons); // no refund of supply or money
        Assert.Equal(army.Money, updated.Money);
    }

    [Fact]
    public void RenameUnit_ThroughTheSession_AcceptsANameWithSpaces()
    {
        // N1: the CLI parser joins tokens[3..], so "Legio I" reaches the handler as one name.
        var session = new GameSession(
            CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario);

        var output = session.Submit("rename-unit north-army-1 0 Legio I");

        Assert.Contains(
            output.Lines, line => line.Contains("armies.rename-unit accepted", StringComparison.Ordinal));
        Assert.Equal("Legio I", session.State.ArmyById("north-army-1")!.Units[0].Name);
    }

    // ---- sourced rules (Done-when 2) ----

    private static IEnumerable<UnitSlot> RegularUnits(int count, int troops = 10) =>
        Enumerable.Range(0, count).Select(i => RegularUnit($"u{i}", troops: troops));

    [Fact]
    public void SplitUnit_InAnArmyAtTheUnitCap_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("cap-full", NorthNationId, 5, 5,
                Enumerable.Range(0, ArmiesTestbed.Ruleset.ArmyManagement.MaxUnitsPerArmy)
                    .Select(i => RegularUnit($"u{i}", troops: 6_000))));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "cap-full", 0, 3_000));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.TooManyUnits, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void SplitUnit_OneUnitBelowTheCap_IsAccepted()
    {
        var state = WithArmies(
            InitialState(),
            Army("cap-room", NorthNationId, 5, 5,
                Enumerable.Range(0, ArmiesTestbed.Ruleset.ArmyManagement.MaxUnitsPerArmy - 1)
                    .Select(i => RegularUnit($"u{i}", troops: 6_000))));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "cap-room", 0, 3_000));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(
            ArmiesTestbed.Ruleset.ArmyManagement.MaxUnitsPerArmy,
            result.State.ArmyById("cap-room")!.Units.Count);
        Assert.Equal(state.ArmyById("cap-room")!.TotalTroops, result.State.ArmyById("cap-room")!.TotalTroops);
    }

    [Fact]
    public void SplitUnit_ARegularSplitOffInheritsTypeQualityAndMarker()
    {
        var state = WithArmies(
            InitialState(),
            Army(
                "inherit-a", NorthNationId, 5, 5,
                new[] { RegularUnit("source", unitTypeId: "light_infantry", troops: 10_000, quality: 6) }));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "inherit-a", 0, 3_000));

        Assert.True(result.IsAccepted, result.ToString());
        var splitOff = result.State.ArmyById("inherit-a")!.Units[^1];
        Assert.Equal("light_infantry", splitOff.UnitTypeId);
        Assert.Equal(6, splitOff.Quality);
        Assert.Equal(0, splitOff.MercenaryLabel);
        Assert.True(splitOff.IsRegular);
        Assert.Equal(state.ArmyById("inherit-a")!.TotalTroops, result.State.ArmyById("inherit-a")!.TotalTroops);
    }

    /// <summary>#802 R39 (play CU02): the original splits only regular units, so a mercenary split is refused.</summary>
    [Fact]
    public void SplitUnit_AMercenaryIsRefused()
    {
        var state = WithArmies(
            InitialState(),
            Army(
                "inherit-b", NorthNationId, 5, 5,
                new[] { MercenaryUnit("Gallic", unitTypeId: "heavy_infantry", troops: 5_000, quality: 7, label: 9) }));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "inherit-b", 0, 2_000));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.NotRegular, result.Code);
        Assert.Equal(state, result.State);
    }

    /// <summary>#721 R37: the original renames only regular units.</summary>
    [Fact]
    public void RenameUnit_AMercenaryIsRefused()
    {
        var state = WithArmies(
            InitialState(),
            Army("rename-m", NorthNationId, 5, 5, new[] { MercenaryUnit("Gallic") }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "rename-m", 0, "Gauls"));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.NotRegular, result.Code);
    }

    [Fact]
    public void SplitUnit_SplitOffNameIsTheNextFreeOrdinalAcrossTheNationsArmiesAndGarrison()
    {
        // North owns regular "3rd Foot" in one army and "7th Foot" in the arx garrison; a north mercenary
        // whose name looks like "9th Foot" is skipped (the scan is regular-only) and a south army's
        // "12th Foot" is out of nation. So the highest regular ordinal is 7 and the next is
        // "8th Foot  Battalion" on this Split unit path (mobilization retains its single-space form).
        var state = WithArmies(
            InitialState(),
            Army("names-a", NorthNationId, 5, 5, new[] { RegularUnit("split-me", troops: 10_000, quality: 6) }),
            Army("names-b", NorthNationId, 6, 5, new[] { RegularUnit("3rd Foot  Battalion", troops: 1_000) }),
            Army("names-merc", NorthNationId, 7, 5, new[] { MercenaryUnit("9th Foot Battalion", troops: 1_000, label: 4) }),
            Army("names-south", SouthNationId, 5, 6, new[] { RegularUnit("12th Foot Battalion", troops: 1_000) }));
        state = WithCity(
            state,
            state.Cities.Single(c => string.Equals(c.Id, "arx", StringComparison.Ordinal)) with
            {
                Garrison = ValueList.Of(RegularUnit("7th Foot  Battalion", troops: 1_000)),
            });

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "names-a", 0, 3_000));

        Assert.True(result.IsAccepted, result.ToString());
        var splitOff = result.State.ArmyById("names-a")!.Units[^1];
        Assert.Equal("8th Foot  Battalion", splitOff.Name);
        Assert.Equal(state.ArmyById("names-a")!.TotalTroops, result.State.ArmyById("names-a")!.TotalTroops);
    }

    [Fact]
    public void SplitUnit_HeavyInfantryMatchesTheOriginalF51Name()
    {
        var guards = Enumerable.Range(1, 6)
            .Select(n => RegularUnit($"{n}{Suffix(n)} Guards Battalion", "heavy_infantry", troops: 1_000))
            .Append(RegularUnit("source", "heavy_infantry", troops: 5_000));
        var state = WithArmies(
            InitialState(),
            Army("f51-guards", NorthNationId, 5, 5, guards));

        var result = Dispatcher().Dispatch(
            state, new SplitUnitCommand(NorthNationId, "f51-guards", 6, 2_500));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal("7th Guards  Battalion", result.State.ArmyById("f51-guards")!.Units[^1].Name);
    }

    private static string Suffix(int value) => value % 100 is >= 11 and <= 13
        ? "th"
        : (value % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };

    // ---- rejections (Done-when 3) ----

    [Fact]
    public void SplitUnit_UnknownArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("split-u", NorthNationId, 5, 5, RegularUnits(2)));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "nope", 0, 5));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.UnknownArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void SplitUnit_ForeignArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("split-f", SouthNationId, 5, 5, RegularUnits(2)));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "split-f", 0, 5));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.NotYourArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void SplitUnit_UnitIndexOutOfRange_IsRejectedAndTheStateIsUntouched(int unitIndex)
    {
        var state = WithArmies(InitialState(), Army("split-i", NorthNationId, 5, 5, RegularUnits(2)));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "split-i", unitIndex, 5));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.InvalidUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void RenameUnit_UnknownArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("ren-u", NorthNationId, 5, 5, RegularUnits(1)));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "nope", 0, "Legio I"));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.UnknownArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void RenameUnit_ForeignArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("ren-f", SouthNationId, 5, 5, RegularUnits(1)));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "ren-f", 0, "Legio I"));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.NotYourArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void RenameUnit_UnitIndexOutOfRange_IsRejectedAndTheStateIsUntouched(int unitIndex)
    {
        var state = WithArmies(InitialState(), Army("ren-i", NorthNationId, 5, 5, RegularUnits(1)));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "ren-i", unitIndex, "Legio I"));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.InvalidUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void DisbandUnit_UnknownArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("dis-u", NorthNationId, 5, 5, RegularUnits(2)));

        var result = Dispatcher().Dispatch(state, new DisbandUnitCommand(NorthNationId, "nope", 0));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandUnitRejections.UnknownArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void DisbandUnit_ForeignArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("dis-f", SouthNationId, 5, 5, RegularUnits(2)));

        var result = Dispatcher().Dispatch(state, new DisbandUnitCommand(NorthNationId, "dis-f", 0));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandUnitRejections.NotYourArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void DisbandUnit_UnitIndexOutOfRange_IsRejectedAndTheStateIsUntouched(int unitIndex)
    {
        var state = WithArmies(InitialState(), Army("dis-i", NorthNationId, 5, 5, RegularUnits(2)));

        var result = Dispatcher().Dispatch(state, new DisbandUnitCommand(NorthNationId, "dis-i", unitIndex));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandUnitRejections.InvalidUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- issue #565 arithmetic boundaries and researched type-specific split limits ----

    [Fact]
    public void SplitUnit_ZeroTroops_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("bound-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 6_000) }));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "bound-a", 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.InvalidTroops, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void SplitUnit_AllTheTroops_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("bound-b", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 6_000) }));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "bound-b", 0, 100));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.InvalidTroops, result.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData("light_infantry", 15_000)]
    [InlineData("heavy_infantry", 6_000)]
    [InlineData("archers", 3_500)]
    [InlineData("light_cavalry", 7_000)]
    [InlineData("heavy_cavalry", 2_500)]
    public void SplitUnit_UsesEachTypesTwoFifthsSourceThreshold(string unitTypeId, int standardSize)
    {
        var threshold = standardSize * 2 / 5;
        var tooSmall = WithArmies(
            InitialState(),
            Army("type-threshold", NorthNationId, 5, 5,
                new[] { RegularUnit("source", unitTypeId, threshold - 1) }));

        var rejected = Dispatcher().Dispatch(
            tooSmall, new SplitUnitCommand(NorthNationId, "type-threshold", 0, threshold / 2));

        Assert.True(rejected.IsRejected);
        Assert.Equal(SplitUnitRejections.TooSmall, rejected.Code);
        Assert.Same(tooSmall, rejected.State);

        var exactThreshold = WithArmies(
            InitialState(),
            Army("type-threshold", NorthNationId, 5, 5,
                new[] { RegularUnit("source", unitTypeId, threshold) }));
        var minimumPart = standardSize / 5;
        var accepted = Dispatcher().Dispatch(
            exactThreshold, new SplitUnitCommand(NorthNationId, "type-threshold", 0, minimumPart));

        Assert.True(accepted.IsAccepted, accepted.ToString());
        var updated = accepted.State.ArmyById("type-threshold")!;
        Assert.Equal(minimumPart, updated.Units[0].Troops);
        Assert.Equal(minimumPart, updated.Units[^1].Troops);
        Assert.Equal(threshold, updated.TotalTroops);
    }

    [Theory]
    [InlineData(2_999)]
    [InlineData(7_001)]
    public void SplitUnit_RefusesWhenEitherResultWouldFallBelowOneFifth(int splitTroops)
    {
        var state = WithArmies(
            InitialState(),
            Army("part-floor", NorthNationId, 5, 5,
                new[] { RegularUnit("source", "light_infantry", 10_000) }));

        var result = Dispatcher().Dispatch(
            state, new SplitUnitCommand(NorthNationId, "part-floor", 0, splitTroops));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitUnitRejections.InvalidTroops, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void SplitUnit_AtTheMinimumPartFloor_IsAcceptedAndConservesTroops()
    {
        var state = WithArmies(InitialState(), Army("bound-c", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 6_000) }));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "bound-c", 0, 3_000));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById("bound-c")!;
        Assert.Equal(6_000, updated.TotalTroops);
        Assert.Equal(3_000, updated.Units[^1].Troops);
        Assert.Equal(3_000, updated.Units[0].Troops);
    }

    [Fact]
    public void SplitUnit_AtTheMaximumPartBoundary_IsAcceptedAndConservesTroops()
    {
        var state = WithArmies(InitialState(), Army("bound-d", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 10_000) }));

        var result = Dispatcher().Dispatch(state, new SplitUnitCommand(NorthNationId, "bound-d", 0, 7_000));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById("bound-d")!;
        Assert.Equal(10_000, updated.TotalTroops);
        Assert.Equal(7_000, updated.Units[^1].Troops);
        Assert.Equal(3_000, updated.Units[0].Troops);
    }

    [Fact]
    public void RenameUnit_TwentyThreeCharacters_IsAccepted()
    {
        var name = new string('a', 23);
        var state = WithArmies(InitialState(), Army("name-23", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 100) }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "name-23", 0, name));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(name, result.State.ArmyById("name-23")!.Units[0].Name);
        Assert.Equal(100, result.State.ArmyById("name-23")!.TotalTroops);
    }

    [Fact]
    public void RenameUnit_TwentyFourCharacters_IsRejectedAndTheStateIsUntouched()
    {
        var name = new string('a', 24);
        var state = WithArmies(InitialState(), Army("name-24", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 100) }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "name-24", 0, name));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.InvalidName, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void RenameUnit_EmptyName_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(InitialState(), Army("name-empty", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 100) }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "name-empty", 0, string.Empty));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.InvalidName, result.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData("a\tb")]       // 0x09, below the printable floor
    [InlineData("a\u007fb")]   // 0x7f, the first byte above the printable ceiling
    [InlineData("L\u00e9gio")] // a non-ASCII byte
    public void RenameUnit_NonPrintableCharacter_IsRejectedAndTheStateIsUntouched(string name)
    {
        var state = WithArmies(InitialState(), Army("name-ctl", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 100) }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "name-ctl", 0, name));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.InvalidName, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void RenameUnit_Tilde_IsAcceptedAsTheLastPrintableByte()
    {
        // B4: the upper edge of the printable-ASCII range — '~' (0x7e) is the last byte the save reader
        // accepts; 0x7f and above are rejected (see the theory above).
        var state = WithArmies(InitialState(), Army("name-tilde", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 100) }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "name-tilde", 0, "~"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal("~", result.State.ArmyById("name-tilde")!.Units[0].Name);
        Assert.Equal(100, result.State.ArmyById("name-tilde")!.TotalTroops);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData(" Legio I")]
    [InlineData("Legio I ")]
    public void RenameUnit_WhitespacePaddedOrBlankName_IsRejectedAndTheStateIsUntouched(string name)
    {
        // N4: SaveArmyTable.cs:111 trims on import, so a padded or blank name cannot round-trip.
        var state = WithArmies(InitialState(), Army("name-ws", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 100) }));

        var result = Dispatcher().Dispatch(state, new RenameUnitCommand(NorthNationId, "name-ws", 0, name));

        Assert.True(result.IsRejected);
        Assert.Equal(RenameUnitRejections.InvalidName, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void DisbandUnit_TheLastUnit_IsRejectedWithAReasonPointingAtDisbandArmy()
    {
        var state = WithArmies(InitialState(), Army("last", NorthNationId, 5, 5, RegularUnits(1)));

        var result = Dispatcher().Dispatch(state, new DisbandUnitCommand(NorthNationId, "last", 0));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandUnitRejections.LastUnit, result.Code);
        Assert.Contains("disband-army", result.Rejection!.Message, StringComparison.Ordinal);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void DisbandUnit_RemovesExactlyTheUnitsTroopsAndRefundsNothing()
    {
        var state = WithArmies(
            InitialState(),
            Army("dis-ok", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 1_500), RegularUnit("b", troops: 400) }, money: 50, supplyTons: 20));
        var before = state.ArmyById("dis-ok")!;
        var removed = before.Units[0];

        var result = Dispatcher().Dispatch(state, new DisbandUnitCommand(NorthNationId, "dis-ok", 0));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById("dis-ok")!;
        Assert.Single(updated.Units);
        Assert.Equal("b", updated.Units[0].Name);
        Assert.Equal(before.TotalTroops - removed.Troops, updated.TotalTroops);
        Assert.Equal(before.Money, updated.Money);
        Assert.Equal(before.SupplyTons, updated.SupplyTons);
    }
}
