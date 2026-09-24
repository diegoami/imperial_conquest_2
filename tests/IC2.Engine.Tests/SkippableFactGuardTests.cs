using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace IC2.Engine.Tests;

/// <summary>
/// Guard test that ensures no [Fact] or [Theory] methods call Skip.If or Skip.IfNot.
/// Such methods must be marked [SkippableFact] or [SkippableTheory] instead.
/// </summary>
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

            // Parse IL and look for call/callvirt instructions to Skip.If or Skip.IfNot
            for (int i = 0; i < ilBytes.Length; i++)
            {
                byte opcode = ilBytes[i];

                // 0x28 = call, 0x6F = callvirt
                if (opcode == 0x28 || opcode == 0x6F)
                {
                    if (i + 4 < ilBytes.Length)
                    {
                        // Read 4-byte method token
                        int methodToken = BitConverter.ToInt32(ilBytes, i + 1);

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
                            // Ignore resolution failures
                        }

                        i += 4; // Skip the 4-byte operand
                    }
                }
            }
        }
        catch
        {
            // If reflection fails, we can't validate, so don't report an error
        }

        return false;
    }
}
