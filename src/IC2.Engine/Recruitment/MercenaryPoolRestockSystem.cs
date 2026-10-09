using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// T56: <see cref="MercenaryPoolRestock"/> subscribed to the quarter boundary — the quarterly
/// mercenary restock, <c>FUN_00449130</c>, which the original calls from <c>FUN_004514ec</c>'s
/// week-wrap branch <strong>immediately after the quarterly economy</strong> (<c>FUN_00451b40</c>)
/// and before anything else on that boundary
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §6]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Order 400, after the economy family.</strong> The original's single quarterly tick is one
/// function this engine splits into handlers — <c>economy.quarterly-billing</c> (0),
/// <c>economy.quarterly-city-tick</c> (100), <c>economy.quarterly-nation-tick</c> (200) and
/// <c>economy.ai-deposition</c> (300) — so "immediately after the quarterly economy" is expressed as
/// the order position after all of them, before nothing in particular (the thaw at order 0 already
/// ran). <c>MercenaryPoolRestockTests.The_restock_is_registered_on_the_quarter_boundary_after_every_economy_handler</c>
/// asserts this handler's position among
/// <see cref="SystemRegistry.QuarterBoundaryHandlers"/> rather than only its effect.
/// </para>
/// <para>
/// Draws come from the hook's own <c>quarter:recruitment.mercenary-restock</c> stream, folded with the
/// ending season, so the four boundaries of one year draw independently (see
/// <c>TurnCoordinator.QuarterStreamNameFor</c>). See <see cref="MercenaryPoolRestock"/>'s own remarks
/// for how many draws a quarter costs.
/// </para>
/// </remarks>
[QuarterBoundaryHandler("recruitment.mercenary-restock", Order = 400)]
public sealed class MercenaryPoolRestockSystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return MercenaryPoolRestock.Apply(context.State, context.Ruleset, context.World, context.Rng);
    }
}
