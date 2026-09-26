using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// Guard test that ensures no [Fact] or [Theory] methods call Skip.If or Skip.IfNot.
/// Such methods must be marked [SkippableFact] or [SkippableTheory] instead.
/// </summary>
/// <remarks>
/// Follow-up #342 (T77's non-blocking finding): <see cref="MethodCallsSkip"/> used to scan the IL
/// byte array for a literal <c>0x28</c> (call) or <c>0x6F</c> (callvirt) without regard to
/// instruction boundaries, so a coincidental byte of either value sitting inside an <em>earlier</em>
/// instruction's operand could be mistaken for the start of a call, throwing the walk out of sync
/// with the real instruction stream -- and in principle skipping past a genuine
/// <c>Skip.If</c>/<c>Skip.IfNot</c> call that follows. It now decodes one real instruction at a time
/// using the CLI opcode table (<see cref="OpCodes"/>) to look up each opcode's operand size, so a
/// byte inside an operand is never read as the next opcode.
/// </remarks>
public class SkippableFactGuardTests
{
    [Fact]
    public void No_Fact_or_Theory_methods_call_Skip_If_or_IfNot()
    {
        var assembly = typeof(SkippableFactGuardTests).Assembly;
        var problematicMethods = new List<string>();

        // Find the Xunit.SkippableFact assembly to get the attribute types
        var skippableFactAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Xunit.SkippableFact");

        Type? skippableFactType = skippableFactAssembly?.GetType("Xunit.SkippableFactAttribute");
        Type? skippableTheoryType = skippableFactAssembly?.GetType("Xunit.SkippableTheoryAttribute");

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                // Check if method has [Fact] or [Theory] attribute (but not Skippable variants)
                var factAttr = method.GetCustomAttribute<FactAttribute>();
                var theoryAttr = method.GetCustomAttribute<TheoryAttribute>();

                var skippableFactAttr = skippableFactType != null ? method.GetCustomAttribute(skippableFactType) : null;
                var skippableTheoryAttr = skippableTheoryType != null ? method.GetCustomAttribute(skippableTheoryType) : null;

                if ((factAttr != null || theoryAttr != null) && skippableFactAttr == null && skippableTheoryAttr == null)
                {
                    // Check if this method calls Skip.If or Skip.IfNot
                    if (MethodCallsSkip(method))
                    {
                        problematicMethods.Add($"{type.FullName}.{method.Name}");
                    }
                }
            }
        }

        if (problematicMethods.Count > 0)
        {
            Assert.Fail($"The following [Fact] or [Theory] methods call Skip.If/IfNot and must be [SkippableFact] or [SkippableTheory]:\n" +
                        string.Join("\n", problematicMethods));
        }
    }

    private static bool MethodCallsSkip(MethodInfo method)
    {
        try
        {
            var methodBody = method.GetMethodBody();
            if (methodBody == null)
            {
                return false;
            }

            var ilBytes = methodBody.GetILAsByteArray();
            if (ilBytes == null || ilBytes.Length == 0)
            {
                return false;
            }

            var module = method.DeclaringType?.Module;
            if (module == null)
            {
                return false;
            }

            // Walk one real instruction at a time -- never byte by byte -- so a call/callvirt is
            // only ever read where an instruction boundary actually puts one.
            int i = 0;
            while (i < ilBytes.Length)
            {
                OpCode opCode;
                if (ilBytes[i] == TwoBytePrefix)
                {
                    if (i + 1 >= ilBytes.Length || !TwoByteOpCodes.TryGetValue(ilBytes[i + 1], out opCode))
                    {
                        break; // unrecognised encoding; stop rather than risk mis-parsing further
                    }

                    i += 2;
                }
                else if (!OneByteOpCodes.TryGetValue(ilBytes[i], out opCode))
                {
                    break;
                }
                else
                {
                    i += 1;
                }

                int operandSize = GetOperandSize(opCode, ilBytes, i);
                if (operandSize < 0 || i + operandSize > ilBytes.Length)
                {
                    break;
                }

                if (opCode.Value == OpCodes.Call.Value || opCode.Value == OpCodes.Callvirt.Value)
                {
                    int methodToken = BitConverter.ToInt32(ilBytes, i);

                    try
                    {
                        var resolvedMethod = module.ResolveMethod(methodToken);
                        if (resolvedMethod?.DeclaringType?.FullName == "Xunit.Skip" &&
                            (resolvedMethod.Name == "If" || resolvedMethod.Name == "IfNot"))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore resolution failures (e.g. tokens that need generic type arguments)
                    }
                }

                i += operandSize;
            }
        }
        catch
        {
            // If reflection fails, we can't validate, so don't report an error
        }

        return false;
    }

    /// <summary>The number of bytes an operand occupies right after the opcode at <paramref name="position"/>,
    /// or -1 for an operand type this walk doesn't know how to size (the caller then stops the walk
    /// rather than guess).</summary>
    private static int GetOperandSize(OpCode opCode, byte[] il, int position)
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
                if (position + 4 > il.Length)
                {
                    return -1;
                }

                int caseCount = BitConverter.ToInt32(il, position);
                return 4 + (caseCount * 4);
            default:
                return -1;
        }
    }

    private const byte TwoBytePrefix = 0xFE;

    private static readonly Dictionary<byte, OpCode> OneByteOpCodes = BuildOpCodeMap(size: 1, keySelector: value => (byte)value);
    private static readonly Dictionary<byte, OpCode> TwoByteOpCodes = BuildOpCodeMap(size: 2, keySelector: value => (byte)(value & 0xFF));

    private static Dictionary<byte, OpCode> BuildOpCodeMap(int size, Func<short, byte> keySelector)
    {
        var map = new Dictionary<byte, OpCode>();

        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
            {
                continue;
            }

            var opCode = (OpCode)field.GetValue(null)!;
            if (opCode.Size == size)
            {
                map[keySelector(opCode.Value)] = opCode;
            }
        }

        return map;
    }
}
