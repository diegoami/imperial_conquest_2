using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;

namespace IC2.Engine.Tests.TestInfrastructure;

/// <summary>
/// Done-when 1's own enforcement: every test that starts a <c>dotnet run</c> script must sit in
/// <see cref="DotnetRunScriptCollection"/>, found by reflection rather than by a maintained list, so a
/// future test that starts one and forgets the collection fails here rather than reintroducing bug
/// #320's race.
/// </summary>
/// <remarks>
/// Two independent checks, because "starts a dotnet run script" has two shapes in this codebase:
/// <list type="bullet">
/// <item>every real dotnet-run test goes through the one shared choke point,
/// <see cref="DotnetRunScriptRunner.Run"/> -- <see cref="Every_caller_of_the_shared_dotnet_run_runner_sits_in_the_shared_collection"/>
/// finds every caller of it, including from a constructor, and fails if that caller's declaring type
/// (resolved past any compiler-generated async-state-machine or lambda-closure wrapper) is not in the
/// collection;</item>
/// <item><see cref="No_test_starts_dotnet_run_directly_outside_the_shared_runner"/> separately fails on
/// any method outside <see cref="DotnetRunScriptRunner"/> itself that both starts a process and, in the
/// same method body, loads a string constant mentioning "run" -- catching a test that bypasses the
/// runner and calls <see cref="Process.Start(ProcessStartInfo)"/> or constructs a
/// <see cref="ProcessStartInfo"/> directly.</item>
/// </list>
/// Both walk IL by each instruction's real operand size (a small table built from
/// <see cref="OpCodes"/>), not by skipping a fixed 4 bytes after every candidate opcode byte the way
/// <c>SkippableFactGuardTests</c> does elsewhere in this project -- skipping a fixed size can walk past
/// a real instruction that starts inside what was assumed to be an operand, and either check needs
/// every <c>call</c>/<c>callvirt</c>/<c>newobj</c>/<c>ldstr</c> in a method body found correctly, not
/// just the first one.
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
            foreach (var member in CandidateMembers(type))
            {
                if (!IlScanner.MethodCallsTarget(member, runnerMethod))
                {
                    continue;
                }

                foundAnyCaller = true;
                var outerType = OutermostUserType(type);
                var collectionName = CollectionNameOf(outerType);
                if (!string.Equals(collectionName, DotnetRunScriptCollection.Name, StringComparison.Ordinal))
                {
                    offenders.Add(DescribeOffender(type, member));
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
    /// The complement of the check above: a test that starts <c>dotnet run</c> by calling
    /// <see cref="Process.Start(ProcessStartInfo)"/> or constructing a <see cref="ProcessStartInfo"/>
    /// directly, bypassing <see cref="DotnetRunScriptRunner"/> entirely, would pass the first guard (it
    /// never calls the choke point) while still reintroducing bug #320's shared-build-output race. This
    /// flags any method outside <see cref="DotnetRunScriptRunner"/> whose body both starts a process
    /// and loads a string constant whose value, split on whitespace, has "run" as one whole token
    /// (case-insensitive) -- a cheap, deliberately over-inclusive heuristic for "this method looks like
    /// it runs `dotnet run ...`", not a claim that every flagged method is provably one. Matching a
    /// whole token rather than a substring means a string like "runtime" does not count (review round
    /// 2, non-blocking): a token match still catches both <c>"run script.cs"</c> in one literal and
    /// <c>ArgumentList.Add("run")</c> as its own literal.
    /// </summary>
    [Fact]
    public void No_test_starts_dotnet_run_directly_outside_the_shared_runner()
    {
        var assembly = typeof(DotnetRunScriptRunner).Assembly;
        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            if (OutermostUserType(type) == typeof(DotnetRunScriptRunner))
            {
                continue; // the choke point itself is allowed to do exactly this.
            }

            foreach (var member in CandidateMembers(type))
            {
                if (!IlScanner.MethodStartsAProcess(member))
                {
                    continue;
                }

                if (!IlScanner.MethodLoadsWholeArgumentToken(member, "run"))
                {
                    continue;
                }

                offenders.Add(DescribeOffender(type, member));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The following methods start a process and mention \"run\" in a string constant -- they "
            + "look like they start `dotnet run` directly instead of going through "
            + "DotnetRunScriptRunner.Run, so they would not be caught by the collection membership "
            + "check above: " + string.Join(", ", offenders));
    }

    /// <summary>Every method and constructor declared directly on <paramref name="type"/>.</summary>
    private static IEnumerable<MethodBase> CandidateMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(flags))
        {
            yield return method;
        }

        foreach (var ctor in type.GetConstructors(flags))
        {
            yield return ctor;
        }
    }

    /// <summary>
    /// Walks up through compiler-generated wrappers (an async state machine, an iterator, a lambda's
    /// display class) to the type a person actually wrote. Without this, a caller inside an
    /// <see langword="async"/> test method or a lambda is reported against its generated
    /// <c>&lt;Method&gt;d__0</c> or <c>&lt;&gt;c</c> type, which never carries the test class's own
    /// <c>[Collection]</c> attribute and would always be misreported as an offender (review round 1,
    /// N1).
    /// </summary>
    private static Type OutermostUserType(Type type)
    {
        var current = type;
        while (current.IsNested && current.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
        {
            current = current.DeclaringType!;
        }

        return current;
    }

    /// <summary>
    /// A human-readable "Type.Method" name for an offender, with the method name resolved past any
    /// compiler-generated wrapper the same way <see cref="OutermostUserType"/> resolves the type.
    /// </summary>
    /// <remarks>
    /// Review round 2, non-blocking: reporting <paramref name="member"/>.Name directly named an async
    /// caller's generated state machine method, always <c>MoveNext</c>, which identifies nothing to a
    /// reader. <see cref="AsyncStateMachineAttribute"/> is how the compiler itself records which
    /// user-written method a given state machine type belongs to, so it is used here to recover the
    /// real name rather than guessing from the generated type's own name.
    /// </remarks>
    private static string DescribeOffender(Type declaringType, MethodBase member)
    {
        var outerType = OutermostUserType(declaringType);
        var methodName = OutermostUserMethodName(declaringType, member, outerType);
        return $"{outerType.FullName}.{methodName}";
    }

    private static string OutermostUserMethodName(Type declaringType, MethodBase member, Type outerType)
    {
        if (declaringType == outerType)
        {
            return member.Name;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var candidate in outerType.GetMethods(flags))
        {
            var stateMachine = candidate.GetCustomAttribute<AsyncStateMachineAttribute>();
            if (stateMachine?.StateMachineType == declaringType)
            {
                return candidate.Name;
            }
        }

        // Not resolvable to a single async method (a lambda's display class, or a state machine
        // nested more than one level deep) -- name both the generated type and the member so the
        // offender is still findable in the source, just less friendly than a real method name.
        return $"{declaringType.Name}.{member.Name}";
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
}

/// <summary>
/// A minimal IL reader shared by both guard tests above: decodes an instruction stream by each
/// instruction's real operand size, built from <see cref="OpCodes"/> rather than hard-coded, and
/// resolves the metadata tokens the two checks above care about (method/constructor references for
/// <c>call</c>/<c>callvirt</c>/<c>newobj</c>, and string literals for <c>ldstr</c>).
/// </summary>
internal static class IlScanner
{
    private static readonly Dictionary<short, OpCode> OneByteOpCodes = BuildTable(twoByte: false);
    private static readonly Dictionary<short, OpCode> TwoByteOpCodes = BuildTable(twoByte: true);

    public static bool MethodCallsTarget(MethodBase caller, MethodBase target)
    {
        return AnyInstruction(caller, (opCode, il, operandStart, module) =>
        {
            if (opCode != OpCodes.Call && opCode != OpCodes.Callvirt && opCode != OpCodes.Newobj)
            {
                return false;
            }

            var resolved = TryResolveMethod(module, il, operandStart, caller);
            return resolved is not null
                   && resolved.Name == target.Name
                   && resolved.DeclaringType == target.DeclaringType;
        });
    }

    /// <summary>
    /// Whether <paramref name="caller"/>'s body calls <see cref="Process.Start(ProcessStartInfo)"/> (or
    /// any other <c>Process.Start</c> overload) or constructs a <see cref="ProcessStartInfo"/>.
    /// </summary>
    public static bool MethodStartsAProcess(MethodBase caller)
    {
        return AnyInstruction(caller, (opCode, il, operandStart, module) =>
        {
            if (opCode != OpCodes.Call && opCode != OpCodes.Callvirt && opCode != OpCodes.Newobj)
            {
                return false;
            }

            var resolved = TryResolveMethod(module, il, operandStart, caller);
            if (resolved is null)
            {
                return false;
            }

            if (resolved.DeclaringType == typeof(Process) && resolved.Name == nameof(Process.Start))
            {
                return true;
            }

            return resolved is ConstructorInfo && resolved.DeclaringType == typeof(ProcessStartInfo);
        });
    }

    /// <summary>
    /// Whether any <c>ldstr</c> literal in <paramref name="caller"/>, split on whitespace, has
    /// <paramref name="wholeToken"/> as one of its whole parts (ordinal, case-insensitive) -- not
    /// merely as a substring. Review round 2, non-blocking: a substring match on "run" also matched
    /// "runtime", which is not a command word.
    /// </summary>
    public static bool MethodLoadsWholeArgumentToken(MethodBase caller, string wholeToken)
    {
        return AnyInstruction(caller, (opCode, il, operandStart, module) =>
        {
            if (opCode != OpCodes.Ldstr)
            {
                return false;
            }

            try
            {
                var token = BitConverter.ToInt32(il, operandStart);
                var literal = module.ResolveString(token);
                return literal
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => string.Equals(part, wholeToken, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        });
    }

    private static MethodBase? TryResolveMethod(Module module, byte[] il, int operandStart, MethodBase caller)
    {
        try
        {
            var token = BitConverter.ToInt32(il, operandStart);
            var typeArgs = caller.DeclaringType?.IsGenericType == true
                ? caller.DeclaringType.GetGenericArguments()
                : Type.EmptyTypes;
            var methodArgs = caller is MethodInfo { IsGenericMethod: true } generic
                ? generic.GetGenericArguments()
                : Type.EmptyTypes;
            return module.ResolveMethod(token, typeArgs, methodArgs);
        }
        catch
        {
            return null; // unresolvable token in this context -- not a match.
        }
    }

    private static bool AnyInstruction(MethodBase method, Func<OpCode, byte[], int, Module, bool> predicate)
    {
        try
        {
            var body = method.GetMethodBody();
            var il = body?.GetILAsByteArray();
            var module = method.DeclaringType?.Module;
            if (il is null || il.Length == 0 || module is null)
            {
                return false;
            }

            var i = 0;
            while (i < il.Length)
            {
                OpCode opCode;
                if (il[i] == 0xFE)
                {
                    if (i + 1 >= il.Length || !TwoByteOpCodes.TryGetValue((short)(0xFE00 | il[i + 1]), out opCode))
                    {
                        return false; // malformed or unknown -- stop rather than misread the rest.
                    }

                    i += 2;
                }
                else
                {
                    if (!OneByteOpCodes.TryGetValue(il[i], out opCode))
                    {
                        return false;
                    }

                    i += 1;
                }

                var operandStart = i;
                if (predicate(opCode, il, operandStart, module))
                {
                    return true;
                }

                i += OperandSize(opCode, il, operandStart);
            }
        }
        catch
        {
            // If reflection fails for this method, it cannot be proven to match.
        }

        return false;
    }

    private static int OperandSize(OpCode opCode, byte[] il, int operandStart)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                if (operandStart + 4 > il.Length)
                {
                    return il.Length - operandStart;
                }

                var count = BitConverter.ToInt32(il, operandStart);
                return 4 + (count * 4);
            default:
                return 0;
        }
    }

    private static Dictionary<short, OpCode> BuildTable(bool twoByte)
    {
        var table = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            if ((opCode.Size == 2) != twoByte)
            {
                continue;
            }

            table[opCode.Value] = opCode;
        }

        return table;
    }
}
