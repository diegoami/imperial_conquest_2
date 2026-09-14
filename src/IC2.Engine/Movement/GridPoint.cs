namespace IC2.Engine.Movement;

/// <summary>
/// One map cell, as this task's pure geometry and cost functions see it.
/// </summary>
/// <remarks>
/// A dedicated type rather than a bare <c>(int, int)</c> tuple so that every signature in this
/// namespace reads as "a cell" rather than "two numbers" — and so a caller building one from an
/// <see cref="Model.ArmyState"/>'s or <see cref="Model.FleetState"/>'s <c>X</c>/<c>Y</c> does so at one
/// explicit conversion point rather than by argument order convention.
/// </remarks>
public readonly record struct GridPoint(int X, int Y)
{
    /// <inheritdoc/>
    public override string ToString() => $"({X}, {Y})";
}
