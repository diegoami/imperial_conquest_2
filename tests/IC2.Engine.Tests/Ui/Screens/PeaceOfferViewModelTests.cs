using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

/// <summary>
/// <c>docs/tasks/T139.md</c> Done-when 3: the Offer of peace window's model, pinned without the Godot SDK.
/// </summary>
public sealed class PeaceOfferViewModelTests
{
    private static PendingPeaceOffer Offer(params string[] lines) =>
        new("north", "south", "north", Array.AsReadOnly(lines));

    [Fact]
    public void TheTitle_IsOfferOfPeace()
    {
        Assert.Equal("Offer of peace", PeaceOfferViewModel.FromOffer(Offer("x")).Title);
    }

    [Fact]
    public void TheBody_IsExactlyTheOffersLines_InOrder()
    {
        var lines = new[] { "second-line-first", "A", "", "  odd spacing  ", "last" };

        var model = PeaceOfferViewModel.FromOffer(Offer(lines));

        Assert.Equal(lines, model.Body);
    }

    [Fact]
    public void TheBody_IsTheEnginesOwnLines()
    {
        var offer = Offer(
            "After defeating you in battle Southern League are willing to end their war with you, if you agree to the terms below.",
            "An honourable peace with no reparations or penalties",
            "If the peace terms are acceptable click YES.",
            "Otherwise to continue the war click NO.");

        Assert.Equal(offer.Lines, PeaceOfferViewModel.FromOffer(offer).Body);
    }

    [Fact]
    public void TheButtons_AreExactlyYesAndNo()
    {
        var model = PeaceOfferViewModel.FromOffer(Offer("x"));

        Assert.Equal(
            new[] { new PeaceOfferButton("Yes", "peace-yes"), new PeaceOfferButton("No", "peace-no") },
            model.Buttons);
    }
}
