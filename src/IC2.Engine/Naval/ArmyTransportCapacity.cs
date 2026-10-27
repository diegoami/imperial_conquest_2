using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// T232 admission policy: measured division-first capacity for Faithful humans;
/// exact capacity for Improved and AI callers. Faithful AI embark invokes its
/// separate whole-unit overflow allocator when this exact check is exceeded.
/// </summary>
public static class ArmyTransportCapacity
{
    public static bool Exceeds(long troops, int ships, Ruleset ruleset, SeatControl control)
    {
        var divisor = ruleset.Naval.TransportTroopsPerShip;
        return ruleset.Flags.SeatAsymmetry == SeatAsymmetryModel.Faithful && control == SeatControl.Human
            ? troops / divisor > ships
            : troops > (long)ships * divisor;
    }
}
