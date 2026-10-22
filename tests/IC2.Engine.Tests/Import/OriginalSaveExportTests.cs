using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// T185 Done-when 1–2 on native state, without private files: a game played by the engine exports every
/// turn, the bytes pass the complete framing and the production readers, and importing them back gives
/// the same game state in every field the original format holds. The refusals are typed and name the field.
/// </summary>
public sealed class OriginalSaveExportTests
{
    /// <summary>A plausible interface profile, chosen here (the export copies it unchanged).</summary>
    internal static OriginalSaveUiProfile Ui { get; } = new(
        Enumerable.Range(0, 16).Select(_ => new OriginalNationUi(1, 0, [0, 0, 400, 300], [400, 0, 300, 200], [400, 200, 300, 260], 40, 80, 0, 10, 5)).ToArray(),
        0, 0, 600, 800);

    private static SaveGame Save(GameState state) => new(GameDataSchema.CurrentVersion, "id", "label", state.ScenarioId, state.WorldId, state.RulesetId, state);

    private static OriginalSaveDocument Export(GameState state) =>
        OriginalSaveExporter.ToDocument(Save(state), RealGameData.World, RealGameData.Ruleset, Ui).Document;

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
