using System.Reflection;
using IC2.Engine.Import;
using Xunit;
using static IC2.Engine.Import.OriginalSaveFieldMapping;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// Makes Done-when 2's "zero unmapped fields" claim a checkable fact rather than a hard-coded empty
/// list (review B1, PR #319 round 1): reflects over every <c>IC2.Data</c> record type
/// <see cref="OriginalSaveFieldMapping.Types"/> names, and checks every public instance property has
/// exactly one entry in <see cref="OriginalSaveFieldMapping.All"/> — so a field <c>IC2.Data</c> starts
/// or stops exposing fails this test, not silently drifts.
/// </summary>
public class OriginalSaveFieldMappingTests
{
    private static IEnumerable<PropertyInfo> RealProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Fact]
    public void Every_public_property_of_every_declared_type_has_exactly_one_mapping_entry()
    {
        foreach (var type in Types)
        {
            var declaredNames = RealProperties(type).Select(p => p.Name).ToHashSet();
            var mappedNames = All.Where(m => m.Type == type).Select(m => m.Property).ToList();

            foreach (var propertyName in declaredNames)
            {
                var count = mappedNames.Count(n => n == propertyName);
                Assert.True(
                    count == 1,
                    $"{type.Name}.{propertyName} has {count} mapping entries in OriginalSaveFieldMapping.All; expected exactly 1.");
            }
        }
    }

    [Fact]
    public void No_mapping_entry_names_a_property_that_no_longer_exists()
    {
        // The other half of "a dropped field fails the test" -- a stale entry for a renamed or removed
        // IC2.Data property would otherwise sit here forever, silently describing nothing.
        foreach (var mapping in All)
        {
            var stillExists = RealProperties(mapping.Type).Any(p => p.Name == mapping.Property);
            Assert.True(stillExists, $"{mapping.QualifiedName} is declared in OriginalSaveFieldMapping.All but no longer exists on {mapping.Type.Name}.");
        }
    }

    [Fact]
    public void No_mapping_entry_names_a_type_outside_the_declared_set()
    {
        foreach (var mapping in All)
        {
            Assert.Contains(mapping.Type, Types);
        }
    }

    [Fact]
    public void Every_mapping_entry_states_a_nonempty_reason()
    {
        foreach (var mapping in All)
        {
            Assert.False(string.IsNullOrWhiteSpace(mapping.Reason), $"{mapping.QualifiedName} has no stated reason.");
        }
    }

    [Fact]
    public void The_declared_unmapped_set_is_exactly_the_users_narrow_mercenary_waiver()
    {
        // docs/tasks/T21.md "Mercenary position": the user's waiver of Done-when 2's "zero unmapped
        // fields" is narrow -- MercenaryRecord.X/Y only. A third DeclaredUnmapped entry must fail this
        // test rather than silently widen the waiver.
        var declaredUnmapped = All
            .Where(m => m.Kind == FieldMappingKind.DeclaredUnmapped)
            .Select(m => m.QualifiedName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "MercenaryRecord.X", "MercenaryRecord.Y" }, declaredUnmapped);
    }

    [Fact]
    public void UnmappedFieldNames_matches_the_declared_unmapped_entries_exactly()
    {
        var expected = All
            .Where(m => m.Kind == FieldMappingKind.DeclaredUnmapped)
            .Select(m => m.QualifiedName)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(expected, UnmappedFieldNames.OrderBy(n => n, StringComparer.Ordinal));
    }
}
