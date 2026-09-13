using System.Globalization;
using System.Reflection;

namespace IC2.Engine.Model;

/// <summary>One gameplay-governed number the <see cref="Ruleset"/> exposes.</summary>
/// <param name="Path">
/// The dotted/indexed path to the value, matching the JSON document's own shape — e.g.
/// <c>combat.winnerCasualtyNumerator</c> or <c>unitTypes[2].combatPowerWeight</c>.
/// </param>
/// <param name="Value">The value, as a double so one enumeration covers integral and fractional fields.</param>
public sealed record RulesetNumber(string Path, double Value)
{
    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Path} = {Value}");
}

/// <summary>
/// Enumerates every gameplay-governed number a loaded <see cref="Ruleset"/> exposes.
/// </summary>
/// <remarks>
/// This exists so the claim "no gameplay constant is hardcoded in C#" is <em>checkable</em> rather than
/// asserted: a test walks this enumeration, resolves each path in the ruleset's own JSON document, and
/// fails if any exposed number differs from what the file says — including after the file's numbers are
/// deliberately changed. A value that came from a C# literal could not track the file and would be
/// caught. See <c>tests/IC2.Engine.Tests/Model/NoHardcodedConstantsTests.cs</c>.
/// </remarks>
public static class RulesetNumbers
{
    /// <summary>
    /// The schema version is a file-contract identifier, not a gameplay rule, so it is excluded from the
    /// enumeration (and from the mutation test that drives it).
    /// </summary>
    private const string SchemaVersionMember = nameof(Ruleset.SchemaVersion);

    /// <summary>Walks a loaded ruleset and returns every numeric leaf with its path.</summary>
    public static ValueList<RulesetNumber> Enumerate(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var results = new List<RulesetNumber>();
        Walk(ruleset, prefix: string.Empty, results);
        return ValueList.From(results);
    }

    private static void Walk(object node, string prefix, List<RulesetNumber> results)
    {
        var contract = JsonContract.For(node.GetType());
        if (contract is null)
        {
            return;
        }

        foreach (var member in contract.Members)
        {
            if (string.Equals(member.ClrName, SchemaVersionMember, StringComparison.Ordinal)
                || string.Equals(member.ClrName, nameof(World.Provenance), StringComparison.Ordinal))
            {
                continue;
            }

            var property = node.GetType().GetProperty(member.ClrName, BindingFlags.Public | BindingFlags.Instance);
            var value = property?.GetValue(node);
            if (value is null)
            {
                continue;
            }

            var path = prefix.Length == 0 ? member.JsonName : $"{prefix}.{member.JsonName}";
            WalkValue(value, member.Type, path, results);
        }
    }

    private static void WalkValue(object value, Type declaredType, string path, List<RulesetNumber> results)
    {
        if (JsonContract.IsNumeric(declaredType))
        {
            results.Add(new RulesetNumber(path, Convert.ToDouble(value, CultureInfo.InvariantCulture)));
            return;
        }

        var elementType = JsonContract.ValueListElementType(declaredType);
        if (elementType is not null)
        {
            var list = (System.Collections.IEnumerable)value;
            var index = 0;
            foreach (var item in list)
            {
                if (item is not null)
                {
                    WalkValue(item, elementType, $"{path}[{index}]", results);
                }

                index++;
            }

            return;
        }

        if (JsonContract.For(declaredType) is not null)
        {
            Walk(value, path, results);
        }
    }
}
