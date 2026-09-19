using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TUnitMap_DisbandArmy</c>. See <see cref="DisbandArmyCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class DisbandArmyCommandHandler : ICommandHandler<DisbandArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(DisbandArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                DisbandArmyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                DisbandArmyRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (army.IsEmbarked)
        {
            return CommandOutcome.Reject(
                DisbandArmyRejections.ArmyEmbarked, $"Army '{army.Id}' is aboard a fleet and cannot be disbanded.");
        }

        var nearbyOwnCity = FindOwnCityNear(state, army.Nation, army.X, army.Y);
        if (nearbyOwnCity is null)
        {
            return CommandOutcome.Reject(
                DisbandArmyRejections.NotNearOwnCity,
                $"Army '{army.Id}' must be near one of its own nation's cities to disband.");
        }

        var updatedCity = nearbyOwnCity with { SupplyTons = nearbyOwnCity.SupplyTons + army.SupplyTons };
        var updatedNation = context.IssuingNation with { Treasury = context.IssuingNation.Treasury + army.Money };

        var updatedArmies = state.Armies.Where(a => !string.Equals(a.Id, army.Id, StringComparison.Ordinal));
        var updatedCities = state.Cities.Select(c =>
            string.Equals(c.Id, updatedCity.Id, StringComparison.Ordinal) ? updatedCity : c);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Cities = ValueList.From(updatedCities),
            Nations = ValueList.From(updatedNations),
        });
    }

    /// <summary>
    /// The nation's own city the army is <em>near</em> — adjoining tiles included (T54 Done-when 4; see
    /// <see cref="DisbandArmyCommand"/>'s remarks for why this widened from exact co-location).
    /// </summary>
    /// <remarks>
    /// Scans <see cref="GameState.Cities"/> in list order and takes the first match, so two of the
    /// nation's own cities adjoining the same tile resolve the same way every run — the supplies always
    /// go to the same one. Order, not proximity: nothing in the evidence ranks two equally-near cities,
    /// and inventing a tie-break would be inventing a rule.
    /// </remarks>
    private static CityState? FindOwnCityNear(GameState state, string nationId, int x, int y)
    {
        foreach (var city in state.Cities)
        {
            if (Math.Max(Math.Abs(city.X - x), Math.Abs(city.Y - y)) <= 1
                && string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                return city;
            }
        }

        return null;
    }
}
