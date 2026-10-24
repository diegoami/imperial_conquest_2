using System.Reflection;
using IC2.Engine.Import;
using Xunit;

namespace IC2.Engine.Tests.Import;

public sealed class OriginalSaveExportFieldMappingTests
{
    [Fact]
    public void Every_native_and_original_property_has_exactly_one_declared_export_policy()
    {
        Check(OriginalSaveExportFieldMapping.NativeTypes, OriginalSaveExportFieldMapping.Native);
        Check(OriginalSaveExportFieldMapping.OriginalTypes, OriginalSaveExportFieldMapping.Original);
    }

    [Fact]
    public void Every_export_policy_names_a_counterpart_and_reason()
    {
        foreach (var rule in OriginalSaveExportFieldMapping.Native.Concat(OriginalSaveExportFieldMapping.Original))
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Counterpart), $"{rule.Name} has no counterpart or omission target.");
            Assert.False(string.IsNullOrWhiteSpace(rule.Reason), $"{rule.Name} has no reason.");
        }
        Assert.Contains(OriginalSaveExportFieldMapping.Native,
            rule => rule.Name == "SaveGame.ArmyTravelTargets" && rule.Policy == OriginalSaveExportFieldMapping.Kind.Omitted);
        Assert.Contains(OriginalSaveExportFieldMapping.Native,
            rule => rule.Name == "SaveGame.Battle" && rule.Policy == OriginalSaveExportFieldMapping.Kind.Rejected);
        Assert.DoesNotContain(OriginalSaveExportFieldMapping.Original,
            rule => rule.Policy is OriginalSaveExportFieldMapping.Kind.Omitted or OriginalSaveExportFieldMapping.Kind.Rejected);
    }

    private static void Check(IReadOnlyList<Type> types, IReadOnlyList<OriginalSaveExportFieldMapping.Rule> rules)
    {
        foreach (var type in types)
        {
            var actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var declared = rules.Where(rule => rule.Type == type).Select(rule => rule.Property)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(actual, declared);
        }
        Assert.All(rules, rule => Assert.Contains(rule.Type, types));
    }
}
