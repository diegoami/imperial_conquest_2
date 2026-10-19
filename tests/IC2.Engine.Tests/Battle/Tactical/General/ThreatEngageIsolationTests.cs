using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

public class ThreatEngageIsolationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Engage_and_public_box_keep_type_based_archer_radius_even_when_exhausted(int shots)
    {
        var state = ThreatModeTests.Blocked(shots);
        Assert.Equal((3, 3), GeneralLine.BoxCell(state, 0, D0, Context));
        var journal = new List<GeneralDecision>();
        var after = GeneralEngage.Engage(state, 0, D0, Context, NoDraws.Instance, journal);
        Assert.Equal(new GeneralDecision(0, GeneralStep.ApproachBox, D0, 3, 3), Assert.Single(journal));
        Assert.Equal(new[] { (1, 1), (2, 2), (3, 3) }, Path(after, 0));
        Assert.Empty(DrawsOn(after));
    }
}
