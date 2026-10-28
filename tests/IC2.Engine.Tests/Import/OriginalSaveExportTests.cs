using IC2.Engine.Armies.Commands;
using IC2.Data;
using IC2.Engine.Core;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// T185 Done-when 1–2 on native state, without private files: a game played by the engine exports every
/// turn, the bytes pass the complete framing and the production readers, and importing them back gives
/// the same game state in every field the original format holds. The refusals are typed and name the field.
/// </summary>
public sealed class OriginalSaveExportTests
{
    [Fact]
    public void Manual_length_leader_name_survives_original_save_encoding()
    {
        var state = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario);
        var name = "ABCDEFGHIJKLMNOPQRSTUVWXY";
        state = state with { Nations = ValueList.From(state.Nations.Select((n, i) => i == 0 ? n with { LeaderName = name } : n)) };
        var document = Export(state);
        var bytes = OriginalSaveCodec.Encode(document);
        Assert.Contains(name, System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal(name, document.Nations[0].Leader);
    }
    /// <summary>A plausible interface profile, chosen here (the export copies it unchanged).</summary>
    internal static OriginalSaveUiProfile Ui { get; } = new(
        Enumerable.Range(0, 16).Select(_ => new OriginalNationUi(1, 0, [0, 0, 400, 300], [400, 0, 300, 200], [400, 200, 300, 260], 40, 80, 0, 10, 5)).ToArray(),
        0, 0, 600, 800);

    private static SaveGame Save(GameState state) => new(GameDataSchema.CurrentVersion, "id", "label", state.ScenarioId, state.WorldId, state.RulesetId, state);

    private static OriginalSaveDocument Export(GameState state) =>
        OriginalSaveExporter.ToDocument(Save(state), RealGameData.World, RealGameData.Ruleset, Ui).Document;

    [Fact]
    public void T216_whole_slot_overflow_exports_with_land_receiver_and_carrier_link()
    {
        var world = RealGameData.World;
        var ruleset = RealGameData.Ruleset;
        var state = GameStateFactory.CreateInitial(world, ruleset, RealGameData.Scenario);
        var source = state.Armies.First(a => a.Nation == "carthage");
        // T224: use a legal adjacent free land source, not the coastal city's
        // occupied tile. Boarding vacates this cell for the receiver search.
        var placement = (from city in state.Cities
            where city.Owner == source.Nation
            let coast = CoastalCity.FirstAdjacentSeaTile(city, world)
            where coast is not null
            from dx in Enumerable.Range(-1, 3)
            from dy in Enumerable.Range(-1, 3)
            let land = new IC2.Engine.Movement.GridPoint(coast.Value.X + dx, coast.Value.Y + dy)
            where LandingTile.IsPassableForArmy(land, world)
                && !state.Cities.Any(c => c.X == land.X && c.Y == land.Y)
            select (Sea: coast.Value, Land: land)).First();
        var sea = placement.Sea;
        source = source with
        {
            X = placement.Land.X, Y = placement.Land.Y, Moves = 5,
            CoveredTileCode = world.Terrain.Decode(world.Width, world.Height)[placement.Land.Y * world.Width + placement.Land.X],
            Money = 100, SupplyTons = 90,
            Units = ValueList.Of(
                new UnitSlot(0, "light_infantry", 4_000, 6, "Light"),
                new UnitSlot(0, "heavy_infantry", 2_000, 7, "Heavy")),
        };
        var fleet = state.Fleets[0] with
        {
            Id = "overflow-test-fleet", Nation = source.Nation, X = sea.X, Y = sea.Y,
            Ships = 10, Moves = 4, ConditionPercent = 100, SupplyTons = 500,
            ConstructionTicksRemaining = null, BuildCityId = null, CarriedArmyId = null,
            CoveredTileCode = 0,
        };
        state = state with
        {
            Armies = ValueList.Of(source), Fleets = ValueList.Of(fleet),
            ActiveSeatIndex = Array.IndexOf(state.TurnOrder.ToArray(), source.Nation),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == source.Nation
                ? n with { Control = SeatControl.Ai } : n)),
        };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), ruleset,
            world, NullEventSink.Instance);
        var result = dispatcher.Dispatch(state, new EmbarkArmyCommand(source.Nation, source.Id, fleet.Id));
        Assert.True(result.IsAccepted, result.ToString());

        var document = Export(result.State);
        Assert.Equal(2, document.Armies.Count);
        Assert.Equal(6_000, result.State.Armies.Sum(a => a.TotalTroops));
        Assert.Equal(0, Assert.Single(document.Fleets).Army);
        Assert.Equal((-1, 4_000, 67, 60),
            (document.Armies[0].Cell, Assert.Single(document.Armies[0].Units).Troops,
                document.Armies[0].Money, document.Armies[0].Supplies));
        Assert.Equal((2_000, 33, 30),
            (Assert.Single(document.Armies[1].Units).Troops,
                document.Armies[1].Money, document.Armies[1].Supplies));
        Assert.True(document.Armies[1].Cell >= 0);
        Assert.Equal(0, document.Armies[1].Moves);
        var decoded = OriginalSaveCodec.Decode(OriginalSaveCodec.Encode(document));
        Assert.Equal(0, decoded.Armies[1].Moves);
        Assert.Equal(document.Fleets[0].Army, decoded.Fleets[0].Army);
        Assert.Equal(document.Armies[1].Units[0].Troops, decoded.Armies[1].Units[0].Troops);
    }

    [Fact]
    public void Live_session_export_matches_the_saved_state_without_mutating_the_session()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            seedOverride: 7, humanSeatNationId: "rome");
        var before = session.State;
        var live = OriginalSaveExporter.ToDocument(session, Ui);

        Assert.Equal(OriginalSaveCodec.Encode(Export(before)), OriginalSaveCodec.Encode(live.Document));
        Assert.Same(before, session.State);
        Assert.DoesNotContain(live.Report.Omitted, item => item.StartsWith("ArmyTravelTargets", StringComparison.Ordinal));

        var army = session.State.Armies.First(a => a.Nation == "rome");
        session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation, army.X, army.Y));
        var withPlan = OriginalSaveExporter.ToDocument(session, Ui);
        Assert.Equal(OriginalSaveCodec.Encode(live.Document), OriginalSaveCodec.Encode(withPlan.Document));
        Assert.Contains(withPlan.Report.Omitted, item => item.StartsWith("ArmyTravelTargets", StringComparison.Ordinal));
        Assert.Single(session.ArmyTravelTargets);

        session.ClearArmyTravelTarget(army.Id);
        session.SetRetreatSupplyPlan(new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false));
        var withRecovery = OriginalSaveExporter.ToDocument(session, Ui);
        Assert.Equal(OriginalSaveCodec.Encode(live.Document), OriginalSaveCodec.Encode(withRecovery.Document));
        Assert.Contains(withRecovery.Report.Omitted,
            item => item.StartsWith("RetreatSupplyPlans", StringComparison.Ordinal));
        Assert.Single(session.RetreatSupplyPlans);
    }

    [Fact]
    public void Live_session_export_refuses_an_open_battle_and_an_unanswered_peace_offer()
    {
        var battle = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical,
            armies: TacticalSessionTestbed.StrongAttacker());
        battle.Submit("attack-army north-army-1 south-army-1");
        Assert.NotNull(battle.OpenTacticalBattle);
        var battleRefusal = Assert.Throws<OriginalSaveExportException>(() =>
            OriginalSaveExporter.ToDocument(battle, Ui));
        Assert.Contains(battleRefusal.Refusals, item => item.Code == OriginalSaveExportRefusal.OpenBattle);

        var pending = TacticalSessionTestbed.PeaceSession();
        TacticalSessionTestbed.PlayUntilEnded(pending, TacticalSessionTestbed.LostBattleScript);
        Assert.True(pending.HasPendingPeaceOffers);
        var offerRefusal = Assert.Throws<OriginalSaveExportException>(() =>
            OriginalSaveExporter.ToDocument(pending, Ui));
        Assert.Contains(offerRefusal.Refusals, item => item.Code == OriginalSaveExportRefusal.PendingPeaceOffer);
    }

    [Fact]
    public void Live_session_file_export_uses_the_checked_writer_and_keeps_the_native_state()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            seedOverride: 11, humanSeatNationId: "rome");
        var before = session.State;
        var folder = Path.Combine(Path.GetTempPath(), "ic2-live-sav-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var template = Path.Combine(folder, "template.sav");
            var output = Path.Combine(folder, "export.sav");
            File.WriteAllBytes(template, OriginalSaveCodec.Encode(Export(before)));

            var result = OriginalSaveExporter.ExportFile(session, template, output);

            Assert.Equal(output, result.OutputPath);
            Assert.True(File.Exists(output));
            Assert.Equal(OriginalSaveCodec.Encode(Export(before)), File.ReadAllBytes(output));
            Assert.Same(before, session.State);
            Assert.Throws<IOException>(() => OriginalSaveExporter.ExportFile(session, template, output));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_game_played_for_24_turns_exports_every_turn_and_imports_back_unchanged()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, seedOverride: 7, humanSeatNationId: "rome");
        for (var turn = 0; turn < 24; turn++)
        {
            var state = session.State;
            var bytes = OriginalSaveCodec.Encode(Export(state));
            OriginalSaveLayout.Validate(bytes);
            var back = OriginalSaveImporter.Import(bytes, "export.sav", RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "id", "label").Save.State;
            AssertSameGame(state, back, turn);
            session.Submit("end");
        }

        Assert.True(session.State.Calendar.TurnIndex >= 24);
    }

    /// <summary>Every field the original format holds, compared native → SAV → native.</summary>
    private static void AssertSameGame(GameState expected, GameState actual, int turn)
    {
        var at = $"turn {turn}";
        Assert.True(expected.Cities.Select(c => c with { Garrison = ValueList<UnitSlot>.Empty }).SequenceEqual(actual.Cities), $"{at}: cities");
        Assert.Equal(expected.Armies.Count, actual.Armies.Count);
        for (var a = 0; a < expected.Armies.Count; a++)
        {
            var (x, y) = (expected.Armies[a], actual.Armies[a]);
            Assert.True((x.Nation, x.X, x.Y, x.Moves, x.Morale, x.Money, x.SupplyTons) == (y.Nation, y.X, y.Y, y.Moves, y.Morale, y.Money, y.SupplyTons), $"{at}: army {a}");
            // The one alias T185 allows: a generated mercenary name too long for the original.
            var aliased = x.Units.Select(u => u.Name == $"Mercenary unit (label {u.MercenaryLabel})" && u.MercenaryLabel != 0
                ? u with { Name = $"Mercenary (label {u.MercenaryLabel})" } : u);
            Assert.True(aliased.SequenceEqual(y.Units), $"{at}: army {a} units");
            Assert.Equal(x.AboardFleetId is null, y.AboardFleetId is null);
        }

        Assert.Equal(expected.Fleets.Count, actual.Fleets.Count);
        for (var f = 0; f < expected.Fleets.Count; f++)
        {
            var (x, y) = (expected.Fleets[f], actual.Fleets[f]);
            // A launched fleet's build city is native bookkeeping only: the original's word holds the condition then.
            Assert.True((x.Nation, x.Ships, x.ConstructionTicksRemaining, x.SupplyTons, x.Money) == (y.Nation, y.Ships, y.ConstructionTicksRemaining, y.SupplyTons, y.Money), $"{at}: fleet {f} {x} VS {y}");
            if (x.ConstructionTicksRemaining is not null) Assert.Equal(x.BuildCityId, y.BuildCityId);
            else
                Assert.True((x.X, x.Y, x.Moves, x.ConditionPercent, x.CarriedArmyId is null) == (y.X, y.Y, y.Moves, y.ConditionPercent, y.CarriedArmyId is null), $"{at}: launched fleet {f}");
        }

        for (var n = 0; n < expected.Nations.Count; n++)
        {
            var (x, y) = (expected.Nations[n], actual.Nations[n]);
            Assert.True((x.Name, x.LeaderName, x.CapitalCityId, x.Control, x.Treasury, x.Unity, x.Wealth, x.TaxBase, x.TaxRatePercent, x.MobilizedPercent)
                == (y.Name, y.LeaderName, y.CapitalCityId, y.Control, y.Treasury, y.Unity, y.Wealth, y.TaxBase, y.TaxRatePercent, y.MobilizedPercent), $"{at}: nation {x.Id}");
            Assert.True(x.RecruitmentSlots.SequenceEqual(y.RecruitmentSlots), $"{at}: nation {x.Id} recruitment");
        }

        Assert.True(expected.TurnOrder.SequenceEqual(actual.TurnOrder) && expected.ActiveSeatIndex == actual.ActiveSeatIndex, $"{at}: turn order");
        Assert.True((expected.Calendar.Week, expected.Calendar.SeasonIndex, expected.Calendar.YearBc) == (actual.Calendar.Week, actual.Calendar.SeasonIndex, actual.Calendar.YearBc), $"{at}: calendar");
        Assert.Equal(expected.PendingOffer, actual.PendingOffer);
        Assert.True(expected.NewsLog.Slots.SequenceEqual(actual.NewsLog.Slots), $"{at}: news");
        Assert.True(expected.MercenaryPool.SequenceEqual(actual.MercenaryPool), $"{at}: mercenaries");
        foreach (var a in RealGameData.World.Nations)
            foreach (var b in RealGameData.World.Nations)
                Assert.Equal(expected.Relations.Get(a.Id, b.Id), actual.Relations.Get(a.Id, b.Id));
        Assert.Equal((expected.RoughSeaCells ?? ValueList<RoughSeaCell>.Empty).OrderBy(c => (c.X, c.Y)), (actual.RoughSeaCells ?? ValueList<RoughSeaCell>.Empty).OrderBy(c => (c.X, c.Y)));
    }

    private static OriginalSaveExportException Refused(GameState state, Ruleset? ruleset = null) =>
        Assert.Throws<OriginalSaveExportException>(() =>
            OriginalSaveExporter.ToDocument(Save(state), RealGameData.World, ruleset ?? RealGameData.Ruleset, Ui));

    private static GameState Start() => new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, seedOverride: 3).State;

    [Fact]
    public void Another_ruleset_is_refused()
    {
        var ex = Refused(Start() with { RulesetId = "improved" });
        Assert.Contains(ex.Refusals, r => r.Code == OriginalSaveExportRefusal.UnsupportedRuleset);
    }

    [Fact]
    public void An_open_battle_is_refused()
    {
        var state = Start();
        var save = Save(state) with { Battle = new SavedTacticalBattle(state.Armies[0].Id, state.Armies[1].Id, 0, false, 0,
            ValueList<SavedTacticalSlot>.Empty, ValueList<int>.Empty, ValueList<bool>.Empty, ValueList<bool>.Empty, ValueList<int>.Empty, -1, 0, true) };
        var ex = Assert.Throws<OriginalSaveExportException>(() =>
            OriginalSaveExporter.ToDocument(save, RealGameData.World, RealGameData.Ruleset, Ui));
        Assert.Contains(ex.Refusals, r => r.Code == OriginalSaveExportRefusal.OpenBattle);
    }

    [Fact]
    public void A_unit_name_longer_than_23_characters_is_refused_naming_the_unit()
    {
        var state = Start();
        var army = state.Armies[0];
        var renamed = state with { Armies = state.Armies.Replace(0, army with { Units = army.Units.Replace(0, army.Units[0] with { Name = new string('N', 24) }) }) };
        var ex = Refused(renamed);
        Assert.Contains(ex.Refusals, r => r.Code == OriginalSaveExportRefusal.Unrepresentable && r.Field == "army[0].unit[0].name");
    }

    [Fact]
    public void Very_poor_quality_four_survives_export_and_original_save_parse()
    {
        var state = Start();
        var army = state.Armies[0];
        var unit = army.Units[0] with { Quality = 4 };
        var changed = state with { Armies = state.Armies.Replace(0, army with { Units = army.Units.Replace(0, unit) }) };

        var bytes = OriginalSaveCodec.Encode(Export(changed));
        Assert.Equal((ushort)4, SaveArmyTable.Parse(bytes).Armies[0].Units[0].QualityCode);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public void Unsupported_live_quality_is_refused_before_writing(int quality)
    {
        var state = Start();
        var army = state.Armies[0];
        var unit = army.Units[0] with { Quality = quality };
        var changed = state with { Armies = state.Armies.Replace(0, army with { Units = army.Units.Replace(0, unit) }) };

        var ex = Refused(changed);
        Assert.Contains(ex.Refusals, r => r.Code == OriginalSaveExportRefusal.Unrepresentable
            && r.Field == "army[0].unit[0].quality");
    }

    [Theory]
    [InlineData(10, "10th")]
    [InlineData(11, "11th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(99, "99th")]
    public void A_real_Dragoons_split_exports_with_a_reported_separator_alias(int ordinal, string prefix)
    {
        var state = Start();
        var army = state.Armies.First(a => a.Nation == "rome");
        var previous = ordinal - 1;
        var suffix = previous is >= 11 and <= 13 ? "th" : (previous % 10) switch
        {
            1 => "st", 2 => "nd", 3 => "rd", _ => "th",
        };
        army = army with { Units = ValueList.Of(
            new UnitSlot(0, "heavy_cavalry", 5_000, 6, $"{previous}{suffix} Dragoons Battalion")) };
        state = state with { Armies = ValueList.Of(army),
            ActiveSeatIndex = Array.IndexOf(state.TurnOrder.ToArray(), army.Nation) };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), RealGameData.Ruleset,
            RealGameData.World, NullEventSink.Instance);
        var split = dispatcher.Dispatch(state, new SplitUnitCommand(army.Nation, army.Id, 0, 1_250));
        Assert.True(split.IsAccepted, split.ToString());
        var native = $"{prefix} Dragoons  Battalion";
        Assert.Equal(native, split.State.ArmyById(army.Id)!.Units[1].Name);
        var before = split.State;
        var result = OriginalSaveExporter.ToDocument(Save(before), RealGameData.World, RealGameData.Ruleset, Ui);
        var exported = $"{prefix} Dragoons Battalion";
        Assert.Equal(exported, result.Document.Armies[0].Units[1].Name);
        Assert.Equal(new OriginalSaveExportAlias(army.Id, 1, native, exported), Assert.Single(result.Report.Aliases));
        Assert.Equal(native, before.ArmyById(army.Id)!.Units[1].Name);
        var decoded = OriginalSaveCodec.Decode(OriginalSaveCodec.Encode(result.Document));
        Assert.Equal(exported, decoded.Armies[0].Units[1].Name);
    }

    [Theory]
    [InlineData("10st Dragoons  Battalion", "heavy_cavalry", 0)]
    [InlineData("10th Dragoons  Battalion", "light_cavalry", 0)]
    [InlineData("10th Dragoons  Battalion", "heavy_cavalry", 7)]
    [InlineData("100th Dragoons  Battalion", "heavy_cavalry", 0)]
    [InlineData("My custom Dragoons Battalion", "heavy_cavalry", 0)]
    public void Dragoons_alias_never_normalizes_other_long_names(string name, string type, int label)
    {
        var state = Start();
        var army = state.Armies[0];
        var unit = army.Units[0] with { Name = name, UnitTypeId = type, MercenaryLabel = label };
        var ex = Refused(state with { Armies = state.Armies.Replace(0,
            army with { Units = army.Units.Replace(0, unit) }) });
        Assert.Contains(ex.Refusals, r => r.Field == "army[0].unit[0].name");
    }

    [Theory]
    [InlineData(7, "Mercenary unit (label 7)", "Mercenary (label 7)")]
    [InlineData(12, "Mercenary unit (label 12)", "Mercenary (label 12)")]
    public void A_generated_mercenary_name_too_long_for_the_original_is_aliased_and_reported(int label, string native, string exported)
    {
        var state = Start();
        var army = state.Armies[0];
        var unit = army.Units[0] with { Name = native, MercenaryLabel = label };
        var result = OriginalSaveExporter.ToDocument(Save(state with { Armies = state.Armies.Replace(0, army with { Units = army.Units.Replace(0, unit) }) }),
            RealGameData.World, RealGameData.Ruleset, Ui);
        Assert.Equal(exported, result.Document.Armies[0].Units[0].Name);
        Assert.Equal(new OriginalSaveExportAlias(army.Id, 0, native, exported), Assert.Single(result.Report.Aliases));
    }

    [Theory]
    [InlineData(8, "Mercenary unit (label 7)")]   // the label in the name is not the unit's own
    [InlineData(0, "Mercenary unit (label 0)")]   // no mercenary label
    [InlineData(7, "Mercenaryunit (label 7) xx")] // not the exact generated name
    public void Any_other_long_name_is_refused_rather_than_aliased(int label, string native)
    {
        var state = Start();
        var army = state.Armies[0];
        var unit = army.Units[0] with { Name = native, MercenaryLabel = label };
        var ex = Refused(state with { Armies = state.Armies.Replace(0, army with { Units = army.Units.Replace(0, unit) }) });
        Assert.Contains(ex.Refusals, r => r.Field == "army[0].unit[0].name");
    }

    [Fact]
    public void An_unknown_city_reference_is_refused_naming_it()
    {
        var state = Start();
        var nation = state.Nations[0];
        var slot = new RecruitmentSlot("atlantis", "archers", 500, 1);
        var ex = Refused(state with { Nations = state.Nations.Replace(0, nation with { RecruitmentSlots = ValueList.Of(slot) }) });
        Assert.Contains(ex.Refusals, r => r.Code == OriginalSaveExportRefusal.UnknownReference && r.Detail.Contains("atlantis"));
    }

    [Fact]
    public void The_start_exports_the_world_history_and_the_interface_profile()
    {
        var document = Export(Start());
        var rome = RealGameData.World.Nations[0];
        Assert.Equal(rome.Treasury, document.Nations[0].Treasury0);
        Assert.Equal(rome.Wealth, document.Nations[0].Wealth0);
        Assert.Equal(RealGameData.World.Cities.Count(c => c.Owner == rome.Id), document.Nations[0].Cities0);
        Assert.Equal(Ui.Nations[3], document.Nations[3].Ui);
        Assert.Equal((Ui.MainClientHeight, Ui.MainClientWidth), (document.Trailer.MainClientHeight, document.Trailer.MainClientWidth));
        // Rome's battle colours, purple, white and blue, as the Delphi words an original save holds.
        Assert.Equal(new uint[] { 0x800080, 0xFFFFFF, 0xFF0000 }, document.Nations[0].Colours);
    }
}

internal static class ValueListReplace
{
    public static ValueList<T> Replace<T>(this ValueList<T> list, int index, T value) =>
        ValueList.From(list.Select((x, i) => i == index ? value : x));
}
