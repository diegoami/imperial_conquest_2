using IC2.Engine.Calendar;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T55 Done-when 4: "Readiness determines the unit's permanent quality", <c>quality = state / 4</c>, and
/// the two seats' thresholds — "the player may mobilize from 16; the AI only at exactly 24. Assert the
/// ladder at its boundaries, not just one point."
/// </summary>
public sealed class MobilizationReadinessTests
{
    private static RecruitmentRules Rules => RecruitmentTestbed.Ruleset.Recruitment;

    /// <summary>
    /// The whole ladder from <c>decompiled-mobilization-and-mercenary-restock.md</c> §2's own name
    /// table at DAT <c>0x1F6CA</c>: indices 0–3 all read <c>not ready</c>, then <c>very poor</c>,
    /// <c>poor</c>, <c>average</c>, <c>good</c>, <c>very good</c>, <c>elite</c>. Every even state code
    /// the weekly tick can produce, plus the odd ones either side of each quality boundary.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 2)]
    [InlineData(12, 3)]
    [InlineData(14, 3)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(18, 4)]
    [InlineData(19, 4)]
    [InlineData(20, 5)]
    [InlineData(22, 5)]
    [InlineData(23, 5)]
    [InlineData(24, 6)]
    public void Quality_is_the_state_code_over_four(int stateCode, int expectedQuality) =>
        Assert.Equal(expectedQuality, MobilizationReadiness.QualityFor(stateCode, Rules));

    /// <summary>
    /// "<c>state &gt; 15</c> is exactly <c>quality &gt;= 4</c>, which is exactly 'the slot no longer
    /// reads <em>not ready</em>'" — the two halves of §2's claim, checked against each other rather
    /// than each restated on its own.
    /// </summary>
    [Fact]
    public void The_human_threshold_is_exactly_the_first_state_that_is_no_longer_not_ready()
    {
        const int firstQualityThatIsNotNotReady = 4; // the DAT name table's first non-"not ready" index.

        for (var stateCode = 0; stateCode <= RecruitmentTestbed.Ruleset.Calendar.CityUnitStateCodeCap; stateCode++)
        {
            var readsNotReady = MobilizationReadiness.QualityFor(stateCode, Rules) < firstQualityThatIsNotNotReady;
            var belowThreshold = stateCode < Rules.MobilizationMinStateCodeHumanSeat;

            Assert.Equal(belowThreshold, readsNotReady);
        }
    }

    /// <summary>
    /// The negative side of the truncation the original writes as <c>if (v &lt; 0) v += 3; v &gt;&gt; 2</c>.
    /// No engine path produces a negative state code — <see cref="CityUnitStateCode.Advance"/> only
    /// climbs, and the report's §8 leaves "whether state is ever negative" unresolved — so this visits
    /// the edge the implementation's own comment claims, and nothing else does.
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-3, 0)]
    [InlineData(-4, -1)]
    [InlineData(-5, -1)]
    [InlineData(-8, -2)]
    public void Quality_truncates_toward_zero_for_a_negative_state_code(int stateCode, int expectedQuality) =>
        Assert.Equal(expectedQuality, MobilizationReadiness.QualityFor(stateCode, Rules));

    /// <summary>A human seat mobilizes from 16 — 14 and 15 are refused, 16 and up are not.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(14, false)]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(24, true)]
    public void A_human_seat_mobilizes_from_sixteen(int stateCode, bool expected) =>
        Assert.Equal(
            expected,
            MobilizationReadiness.IsReady(stateCode, SeatControl.Human, Rules, SeatAsymmetryModel.Faithful));

    /// <summary>
    /// An AI seat mobilizes only at the cap — 16, 22 and 23 are all refused where a human seat would
    /// accept 16 and 22. The asymmetry's whole content is between these two theories.
    /// </summary>
    [Theory]
    [InlineData(16, false)]
    [InlineData(22, false)]
    [InlineData(23, false)]
    [InlineData(24, true)]
    public void An_ai_seat_mobilizes_only_at_the_cap(int stateCode, bool expected) =>
        Assert.Equal(
            expected,
            MobilizationReadiness.IsReady(stateCode, SeatControl.Ai, Rules, SeatAsymmetryModel.Faithful));

    /// <summary>
    /// The AI's threshold is written as a minimum and the original's is an equality; on this engine
    /// they are the same set, because the weekly tick holds the counter at the cap and never above it.
    /// </summary>
    [Fact]
    public void The_ai_minimum_and_the_originals_equality_select_the_same_slots()
    {
        var calendar = RecruitmentTestbed.Ruleset.Calendar;
        Assert.Equal(calendar.CityUnitStateCodeCap, Rules.MobilizationMinStateCodeAiSeat);

        var stateCode = 0;
        for (var week = 0; week < 40; week++)
        {
            stateCode = CityUnitStateCode.Advance(stateCode, calendar);
            Assert.True(stateCode <= calendar.CityUnitStateCodeCap);
            Assert.Equal(
                stateCode == calendar.CityUnitStateCodeCap,
                MobilizationReadiness.IsReady(stateCode, SeatControl.Ai, Rules, SeatAsymmetryModel.Faithful));
        }
    }

    /// <summary>
    /// Under <c>improved</c> (<see cref="SeatAsymmetryModel.Normalized"/>) both seats use the human
    /// threshold, so an AI seat mobilizes from 16 — the edge
    /// <see cref="MobilizationReadiness.MinStateCode"/>'s <c>[designed]</c> remark claims.
    /// </summary>
    [Fact]
    public void Normalized_gives_an_ai_seat_the_human_threshold()
    {
        Assert.False(MobilizationReadiness.IsReady(16, SeatControl.Ai, Rules, SeatAsymmetryModel.Faithful));
        Assert.True(MobilizationReadiness.IsReady(16, SeatControl.Ai, Rules, SeatAsymmetryModel.Normalized));
        Assert.Equal(
            Rules.MobilizationMinStateCodeHumanSeat,
            MobilizationReadiness.MinStateCode(SeatControl.Ai, Rules, SeatAsymmetryModel.Normalized));
    }

    /// <summary>
    /// The ladder tied to the clock it is measured in: a slot ordered at week 0 is
    /// <c>not ready</c> for seven weekly ticks, <c>very poor</c> at the eighth, <c>poor</c> at the
    /// tenth and <c>average</c> from the twelfth — the report's own table, stepped through
    /// <see cref="CityUnitStateCode.Advance"/> rather than restated.
    /// </summary>
    [Fact]
    public void Mobilizing_at_week_eight_is_permanently_very_poor_and_week_twelve_average()
    {
        var calendar = RecruitmentTestbed.Ruleset.Calendar;
        var stateCode = 0;
        var qualityByWeek = new int[13];
        var readyByWeek = new bool[13];

        qualityByWeek[0] = MobilizationReadiness.QualityFor(stateCode, Rules);
        readyByWeek[0] = MobilizationReadiness.IsReady(stateCode, SeatControl.Human, Rules, SeatAsymmetryModel.Faithful);
        for (var week = 1; week <= 12; week++)
        {
            stateCode = CityUnitStateCode.Advance(stateCode, calendar);
            qualityByWeek[week] = MobilizationReadiness.QualityFor(stateCode, Rules);
            readyByWeek[week] = MobilizationReadiness.IsReady(stateCode, SeatControl.Human, Rules, SeatAsymmetryModel.Faithful);
        }

        // weeks 0-7: quality 0-3, every one of which prints "not ready", and none mobilizable.
        for (var week = 0; week <= 7; week++)
        {
            Assert.InRange(qualityByWeek[week], 0, 3);
            Assert.False(readyByWeek[week]);
        }

        Assert.Equal(4, qualityByWeek[8]);    // very poor -- permanently
        Assert.Equal(4, qualityByWeek[9]);
        Assert.Equal(5, qualityByWeek[10]);   // poor
        Assert.Equal(5, qualityByWeek[11]);
        Assert.Equal(6, qualityByWeek[12]);   // average, and the AI's first mobilizable week
        Assert.True(readyByWeek[8]);
        Assert.True(MobilizationReadiness.IsReady(
            24, SeatControl.Ai, Rules, SeatAsymmetryModel.Faithful));
    }
}
