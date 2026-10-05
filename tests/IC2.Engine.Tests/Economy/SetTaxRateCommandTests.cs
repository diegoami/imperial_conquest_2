using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/tasks/T103.md</c> "A command sets the nation's tax rate", Done-when 1 through 3. Every test
/// starts from the real, committed <c>classical-mediterranean</c> world/scenario — the same data the
/// audit's Taxation row and bug #468 describe — never a hand-built state.
/// </summary>
/// <remarks>
/// The accepted range is the original <c>TChangeTax</c> dialog's own 0–40, measured live under Wine
/// (the <c>TTrackBar</c> reports min 0, max 40, line 1, page 5) and carried into the clone as the
/// ruleset's two <c>economy</c> bounds
/// <strong>[Wine candidate:
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md">2026-10-02-unit-map-mouse-orders-and-tax-range.md</see>,
/// (g)]</strong>.
/// </remarks>
public sealed class SetTaxRateCommandTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    /// <summary>Rome is <c>classical-mediterranean</c>'s own turn-order seat 0, so the session opens on it.</summary>
    private static GameSession NewRomeSession() =>
        new(Classical.World, Classical.Ruleset, Classical.Scenario, seedOverride: null, humanSeatNationId: "rome");

    // ---- Done-when 1 ----

    /// <summary>
    /// On the classical-mediterranean scenario, <c>set-tax 20</c> as Rome sets Rome's
    /// <see cref="NationState.TaxRatePercent"/> to 20 and leaves every other nation's rate exactly where
    /// it was. The command is <c>economy.set-tax</c> and its outcome is the generic accepted line.
    /// <strong>[Wine candidate: 2026-10-02-unit-map-mouse-orders-and-tax-range.md, (g)]</strong>
    /// </summary>
    [Fact]
    public void SetTax20_AsRome_SetsOnlyRomesRate()
    {
        var session = NewRomeSession();
        Assert.Equal("rome", session.State.ActiveNationId);

        var ratesBefore = session.State.Nations.ToDictionary(
            nation => nation.Id, nation => nation.TaxRatePercent, StringComparer.Ordinal);

        var output = session.Submit("set-tax 20");

        Assert.Contains("economy.set-tax accepted.", output.Lines);
        Assert.Equal(20, session.State.NationById("rome")!.TaxRatePercent);

        foreach (var nation in session.State.Nations)
        {
            if (string.Equals(nation.Id, "rome", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Equal(ratesBefore[nation.Id], nation.TaxRatePercent);
        }
    }

    // ---- Done-when 2 ----

    /// <summary>
    /// After <c>set-tax 20</c>, the very next quarter's treasury credit for Rome is the one computed from
    /// the new rate: its tax term is exactly <see cref="TaxIncome.Compute"/> at 20, and the whole credit
    /// (tax + tax-base share − city/wealth upkeep + trade income) equals Rome's treasury change across
    /// <see cref="QuarterlyNationEconomySystem"/>. Rome's unity drift is
    /// <see cref="NationUnityUpdate.Compute"/>'s own result at 20, whose tax term is
    /// <c>20 / UnityTaxRateDivisor = 10</c>.
    /// <strong>[Wine candidate: 2026-10-02-unit-map-mouse-orders-and-tax-range.md, (g)]</strong>
    /// </summary>
    [Fact]
    public void AfterSetTax20_TheNextQuarterUsesTheNewRateForTaxIncomeAndUnity()
    {
        var ruleset = Classical.Ruleset;
        var session = NewRomeSession();
        session.Submit("set-tax 20");

        var before = session.State;
        var romeBefore = before.NationById("rome")!;
        Assert.Equal(20, romeBefore.TaxRatePercent);

        var expectedTaxIncome = TaxIncome.Compute(romeBefore.TaxBase, 20, ruleset);

        var context = new QuarterBoundaryContext(
            before, ruleset, session.World, EndingSeasonIndex: 0, new ScriptedRng(), NullEventSink.Instance);
        var after = new QuarterlyNationEconomySystem().OnQuarterBoundary(context);
        var romeAfter = after.NationById("rome")!;

        // The next quarter's credit is the same five-term expression with the new rate in its tax term.
        var expectedCredit = expectedTaxIncome
                             + NationTreasuryCredit.TaxBaseQuarterShare(romeBefore, ruleset)
                             - NationTreasuryCredit.CityAndWealthUpkeep(romeBefore, before, ruleset)
                             + NationTreasuryCredit.TradeIncome(romeBefore, before, ruleset);
        Assert.Equal(expectedCredit, romeAfter.Treasury - romeBefore.Treasury);

        // The field the next quarter read is the one the command wrote, and 20/2 = 10 is its unity cost.
        Assert.Equal(expectedTaxIncome, TaxIncome.Compute(romeBefore.TaxBase, romeBefore.TaxRatePercent, ruleset));
        Assert.Equal(10, 20 / ruleset.Economy.UnityTaxRateDivisor);

        var decayedMobilization = NationUnityUpdate.DecayMobilization(romeBefore.MobilizedPercent, ruleset.Economy);
        Assert.Equal(
            NationUnityUpdate.Compute(romeBefore.Unity, 20, decayedMobilization, ruleset.Economy),
            romeAfter.Unity);
    }

    // ---- Done-when 3 ----

    /// <summary>
    /// The range is read from the ruleset's own bounds, not a C# literal: the shipped values are 0 and 40,
    /// and <c>set-tax 0</c> and <c>set-tax 40</c> are accepted. One below the minimum, one above the
    /// maximum, and a non-integer are each refused with the state unchanged. The non-integer never reaches
    /// the command layer: it is refused at the parser, before any command is built.
    /// <strong>[Wine candidate: 2026-10-02-unit-map-mouse-orders-and-tax-range.md, (g)]</strong>
    /// </summary>
    [Fact]
    public void BoundsComeFromTheRuleset_AndOutOfRangeOrNonIntegerIsRejectedUnchanged()
    {
        var session = NewRomeSession();
        var economy = session.Ruleset.Economy;

        // The dialog's own inclusive range, pinned: 0 and 40, not the percentage's 0 to 100.
        Assert.Equal(0, economy.TaxRateMinPercent);
        Assert.Equal(40, economy.TaxRateMaxPercent);

        // Both ends accepted, read from the ruleset rather than spelled here.
        var atMin = session.Submit($"set-tax {economy.TaxRateMinPercent}");
        Assert.Contains("economy.set-tax accepted.", atMin.Lines);
        Assert.Equal(economy.TaxRateMinPercent, session.State.NationById("rome")!.TaxRatePercent);

        var atMax = session.Submit($"set-tax {economy.TaxRateMaxPercent}");
        Assert.Contains("economy.set-tax accepted.", atMax.Lines);
        Assert.Equal(economy.TaxRateMaxPercent, session.State.NationById("rome")!.TaxRatePercent);

        var unchanged = session.State.NationById("rome")!.TaxRatePercent;

        foreach (var badLine in new[]
                 {
                     $"set-tax {economy.TaxRateMinPercent - 1}",
                     $"set-tax {economy.TaxRateMaxPercent + 1}",
                 })
        {
            var output = session.Submit(badLine);
            Assert.Contains("economy.tax-rate-out-of-range", string.Join("\n", output.Lines));
            Assert.Equal(unchanged, session.State.NationById("rome")!.TaxRatePercent);
        }

        var nonInteger = session.Submit("set-tax x");
        Assert.Contains("Usage: set-tax", string.Join("\n", nonInteger.Lines));
        Assert.Equal(unchanged, session.State.NationById("rome")!.TaxRatePercent);
    }
}
