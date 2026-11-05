using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Ai;

/// <summary>The walker reached D734's hostile-target check, before DBA8's no-progress fallback.</summary>
[DomainEvent("ai.original-walk-contact")]
public sealed record OriginalArmyWalkContactReached(string ArmyId, int TargetX, int TargetY,
    OriginalWalkEndCheck EndCheck) : DomainEvent;

/// <summary>
/// #1724, research07965cd: five city contacts (F4C7/F43F), one army contact (F409).
/// #1729, research46f1b20: EBE8's garrison walk (ECD6) also reaches the city end check.
/// #1775 adds the separately measured non-hostile-city support tail to original AI walks.
/// Hostile contact remains opt-in; human/Improved movement is not extended.
/// </summary>
internal static class OriginalArmyWalkContact
{
    internal static GameState Resupply(GameState state, Ruleset ruleset, World world, string nationId,
        string armyId, GridPoint target, GridPoint at, int moves)
    {
        if (!ClassicalArmyNavigation.Applies(world, ruleset)
            || state.NationById(nationId) is not { Control: SeatControl.Ai } owner
            || state.ArmyById(armyId) is not { IsEmbarked: false, CoveredTileCode: not null } army
            || army.Nation != nationId || AiView.Distance(at.X, at.Y, target.X, target.Y) != 1
            || (uint)target.X >= (uint)world.Width || (uint)target.Y >= (uint)world.Height
            || state.Fleets.Any(f => !f.IsUnderConstruction && f.X == target.X && f.Y == target.Y)
            || state.Armies.Any(a => !a.IsEmbarked && a.CoveredTileCode is not null
                && a.X == target.X && a.Y == target.Y)) return state;
        var city = state.Cities.FirstOrDefault(c => c.X == target.X && c.Y == target.Y);
        if (city is null || state.NationById(city.Owner) is not { } provider
            || state.Relations.IndexOf(city.Owner) < 0
            || state.Relations.Get(nationId, city.Owner) == ruleset.Diplomacy.StateCodes.War) return state;
        // D734's non-hostile arm calls F6D8 directly: unlike combat there is no
        // moves > 0 guard, and unlike E41C no entry-purse/stock eligibility gate.
        var supplied = AutomaticResupply.ForArmy(army with { X = at.X, Y = at.Y, Moves = moves }, city, owner, provider, ruleset);
        return state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == armyId ? supplied.Army : a)),
            Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id ? supplied.City : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == nationId ? supplied.ArmyNation
                : n.Id == city.Owner ? supplied.CityNation : n)),
        };
    }

    internal static ICommand? Find(GameState state, Ruleset ruleset, World world, string nationId,
        string armyId, GridPoint target, OriginalWalkEndCheck kind, GridPoint? at = null, int? moves = null)
    {
        if (kind == OriginalWalkEndCheck.None || state.NationById(nationId)?.Control != SeatControl.Ai
            || !ClassicalArmyNavigation.Applies(world, ruleset)
            || (uint)target.X >= (uint)world.Width || (uint)target.Y >= (uint)world.Height
            || state.ArmyById(armyId) is not { IsEmbarked: false, CoveredTileCode: not null } army
            || army.Nation != nationId) return null;
        var here = at ?? new GridPoint(army.X, army.Y);
        var remaining = moves ?? army.Moves;
        if (remaining <= 0 || AiView.Distance(here.X, here.Y, target.X, target.Y) != 1) return null;
        // A fleet marker is not a city/army target in the original's live map.
        if (state.Fleets.Any(f => !f.IsUnderConstruction && f.X == target.X && f.Y == target.Y)) return null;
        var live = army with { X = here.X, Y = here.Y, Moves = remaining };
        var projected = at is null && moves is null ? state : state with
            { Armies = ValueList.From(state.Armies.Select(a => a.Id == armyId ? live : a)) };
        if (kind == OriginalWalkEndCheck.City)
        {
            var city = state.Cities.FirstOrDefault(c => c.X == target.X && c.Y == target.Y);
            if (city is null || state.Armies.Any(a => !a.IsEmbarked && a.CoveredTileCode is not null
                    && a.X == target.X && a.Y == target.Y)) return null;
            var siege = new BesiegeCityCommand(nationId, armyId, city.Id);
            return AttackLegality.IsLegal(projected, ruleset, siege) ? siege : null;
        }
        if (kind == OriginalWalkEndCheck.Army)
        {
            var enemy = state.Armies.FirstOrDefault(a => a.Id != armyId && !a.IsEmbarked && a.CoveredTileCode is not null
                && a.X == target.X && a.Y == target.Y);
            if (enemy is null) return null;
            var attack = new AttackArmyCommand(nationId, armyId, enemy.Id);
            return AttackLegality.IsLegal(projected, ruleset, attack) ? attack : null;
        }
        return null;
    }
}
