using System.Reflection;

namespace IC2.Engine.Core;

/// <summary>
/// The shared, deterministic half of attribute-based registration: turning a set of assemblies into a
/// stable, ordered list of candidate types.
/// </summary>
/// <remarks>
/// <para>
/// Reflection is a determinism hazard that is easy to overlook. Neither
/// <see cref="Assembly.GetTypes"/> nor the order of an <see cref="IEnumerable{T}"/> of assemblies is
/// specified to be stable — the CLR is free to return types in metadata order, which can change when a
/// file is recompiled, and a caller can hand assemblies over in any order at all. Left alone, that would
/// make "which system runs first" depend on the build, which is the quietest possible way to lose
/// reproducibility.
/// </para>
/// <para>
/// So everything is sorted here, once, by ordinal name: assemblies by full name, then types by full
/// name. Registration then applies its own total ordering on top (phase, then order, then id), and the
/// two together mean scan order cannot influence anything.
/// </para>
/// </remarks>
internal static class AssemblyScan
{
    /// <summary>
    /// Every public and non-public concrete-or-abstract type in <paramref name="assemblies"/>, ordered
    /// by assembly full name and then type full name. Compiler-generated and generic-definition types
    /// are skipped: neither can carry a usable registration.
    /// </summary>
    public static IReadOnlyList<Type> CandidateTypes(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var ordered = assemblies.ToList();
        foreach (var assembly in ordered)
        {
            ArgumentNullException.ThrowIfNull(assembly);
        }

        ordered.Sort(static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));

        var seen = new List<Assembly>(ordered.Count);
        var types = new List<Type>();
        foreach (var assembly in ordered)
        {
            // Membership test only, over a short ordered list: a duplicate assembly in the caller's list
            // must not register the same system twice.
            if (seen.Contains(assembly))
            {
                continue;
            }

            seen.Add(assembly);

            var assemblyTypes = assembly.GetTypes();
            Array.Sort(assemblyTypes, static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));

            foreach (var type in assemblyTypes)
            {
                if (type.IsGenericTypeDefinition)
                {
                    continue;
                }

                types.Add(type);
            }
        }

        return types;
    }

    /// <summary>
    /// Creates the single shared instance of a registered type, with the error message a later task
    /// will actually need when it forgets that registration requires a parameterless constructor.
    /// </summary>
    public static T Instantiate<T>(Type type, string registrationKind)
        where T : class
    {
        if (type.IsAbstract || type.IsInterface)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' is declared as {registrationKind} but is abstract; only a concrete "
                + "class can be registered.");
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' is declared as {registrationKind} but has no public parameterless "
                + "constructor. Registered types are created by the registry and must be stateless — "
                + "everything they need arrives on the context they are handed.");
        }

        return (T)Activator.CreateInstance(type)!;
    }
}
