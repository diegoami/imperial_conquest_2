using System.Reflection;
using Xunit;

namespace IC2.Engine.Tests.TestInfrastructure;

/// <summary>
/// Done-when 1's own enforcement: every test that starts a <c>dotnet run</c> script must sit in
/// <see cref="DotnetRunScriptCollection"/>, found by reflection rather than by a maintained list, so a
/// future test that starts one and forgets the collection fails here rather than reintroducing bug
/// #320's race.
/// </summary>
/// <remarks>
/// Every dotnet-run test goes through the one shared <see cref="DotnetRunScriptRunner.Run"/> method
/// (a deliberate choke point -- see its own remarks). This scans the test assembly's IL for calls to
/// that method, the same technique <c>SkippableFactGuardTests</c> already uses in this project to find
/// every <c>Skip.If</c>/<c>Skip.IfNot</c> call site, and fails on any caller whose declaring type is
/// not decorated with <c>[Collection(DotnetRunScriptCollection.Name)]</c>.
/// </remarks>
public class DotnetRunScriptCollectionGuardTests
{
    [Fact]
    public void Every_caller_of_the_shared_dotnet_run_runner_sits_in_the_shared_collection()
    {
        var runnerMethod = typeof(DotnetRunScriptRunner).GetMethod(nameof(DotnetRunScriptRunner.Run))!;
        var assembly = typeof(DotnetRunScriptRunner).Assembly;

        var offenders = new List<string>();
        var foundAnyCaller = false;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!MethodCalls(method, runnerMethod))
                {
                    continue;
                }

                foundAnyCaller = true;
                var collectionName = CollectionNameOf(type);
                if (!string.Equals(collectionName, DotnetRunScriptCollection.Name, StringComparison.Ordinal))
                {
                    offenders.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        // The guard's own regression case: if the shared runner is ever renamed or inlined away so
        // nothing calls it any more, this test would trivially pass with zero offenders -- which
        // would prove nothing. At least the known dotnet-run tests must still be found.
        Assert.True(foundAnyCaller, "No method in the test assembly calls DotnetRunScriptRunner.Run any more; this guard is no longer checking anything.");

        Assert.True(
            offenders.Count == 0,
            "The following methods start a dotnet run script (via DotnetRunScriptRunner.Run) but their "
            + "declaring class is not in the shared, non-parallel DotnetRunScriptCollection: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The collection name a type's <c>[Collection("...")]</c> attribute declares, read from the
    /// attribute's constructor argument rather than a <c>Name</c> property -- xUnit's own
    /// <c>CollectionAttribute</c> does not expose one directly. <see langword="null"/> if the type has
    /// no such attribute.
    /// </summary>
    private static string? CollectionNameOf(Type type)
    {
        var attributeData = type.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
        return attributeData?.ConstructorArguments.Count > 0
            ? attributeData.ConstructorArguments[0].Value as string
            : null;
    }

    /// <summary>
    /// Whether <paramref name="caller"/>'s IL contains a <c>call</c> or <c>callvirt</c> instruction
    /// whose operand resolves to <paramref name="target"/>. Byte-level, not a full instruction-stream
    /// walk (mirrors <c>SkippableFactGuardTests.MethodCallsSkip</c> in this same project): it scans for
    /// the <c>call</c> (0x28) and <c>callvirt</c> (0x6F) opcode bytes and resolves the 4-byte metadata
    /// token that follows each one, rather than decoding every intervening instruction's own operand
    /// length. A false match on a coincidental byte value would only ever make this guard more
    /// cautious (an extra method treated as a caller that is not one), never less.
    /// </summary>
    private static bool MethodCalls(MethodInfo caller, MethodInfo target)
    {
        try
        {
            var body = caller.GetMethodBody();
            if (body is null)
            {
                return false;
            }

            var il = body.GetILAsByteArray();
            if (il is null || il.Length == 0)
            {
                return false;
            }

            var module = caller.DeclaringType?.Module;
            if (module is null)
            {
                return false;
            }

            for (var i = 0; i < il.Length; i++)
            {
                var opcode = il[i];
                if (opcode != 0x28 && opcode != 0x6F) // call, callvirt
                {
                    continue;
                }

                if (i + 4 >= il.Length)
                {
                    continue;
                }

                var token = BitConverter.ToInt32(il, i + 1);
                try
                {
                    var generic = caller.DeclaringType?.IsGenericType == true
                        ? caller.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;
                    var resolved = module.ResolveMethod(token, generic, caller.GetGenericArguments());
                    if (resolved is not null
                        && resolved.Name == target.Name
                        && resolved.DeclaringType == target.DeclaringType)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Unresolvable token in this context -- not a match, keep scanning.
                }

                i += 4;
            }
        }
        catch
        {
            // If reflection fails for this method, it cannot be proven to call the runner.
        }

        return false;
    }
}
