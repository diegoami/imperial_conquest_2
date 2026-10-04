using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>T132 Done-when 1: the right-hand column's shown/hidden state, key, caption and tooltip.</summary>
[Collection("SidePanelToggleState")]
public sealed class SidePanelToggleTests
{
    public SidePanelToggleTests() => new SidePanelToggle().Set(true);

    [Fact]
    public void InitialStateIsShown()
    {
        Assert.True(new SidePanelToggle().IsShown);
    }

    [Fact]
    public void ToggleFlipsAndSecondToggleRestores()
    {
        var toggle = new SidePanelToggle();
        Assert.False(toggle.Toggle());
        Assert.False(toggle.IsShown);
        Assert.True(toggle.Toggle());
        Assert.True(toggle.IsShown);
    }

    [Fact]
    public void OnlyUnmodifiedF12IsTheToggleKey()
    {
        Assert.True(SidePanelToggle.IsToggleKey("F12", false, false, false));
        Assert.False(SidePanelToggle.IsToggleKey("F12", true, false, false));
        Assert.False(SidePanelToggle.IsToggleKey("F12", false, true, false));
        Assert.False(SidePanelToggle.IsToggleKey("F12", false, false, true));
        Assert.False(SidePanelToggle.IsToggleKey("Escape", false, false, false));
        Assert.False(SidePanelToggle.IsToggleKey("X", false, true, false));
    }

    [Fact]
    public void CaptionAndTooltipMatchEachState()
    {
        var toggle = new SidePanelToggle();
        Assert.Equal("»", toggle.Caption);
        Assert.Equal("Hide the panel (F12)", toggle.Tooltip);
        toggle.Toggle();
        Assert.Equal("«", toggle.Caption);
        Assert.Equal("Show the panel (F12)", toggle.Tooltip);
    }

    [Fact]
    public void StateIsSharedByTwoInstances()
    {
        var first = new SidePanelToggle();
        var second = new SidePanelToggle();
        first.Toggle();
        Assert.False(second.IsShown);
        Assert.Equal("«", second.Caption);
    }

    [Fact]
    public void ChangedFiresOnEveryChangeAndNotOnANoOpSet()
    {
        var toggle = new SidePanelToggle();
        var count = 0;
        void Handler() => count++;
        SidePanelToggle.Changed += Handler;
        try
        {
            toggle.Toggle();
            toggle.Toggle();
            toggle.Set(true);
            Assert.Equal(2, count);
        }
        finally
        {
            SidePanelToggle.Changed -= Handler;
        }
    }
}
