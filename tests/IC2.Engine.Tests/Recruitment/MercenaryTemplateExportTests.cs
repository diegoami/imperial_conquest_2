using System.Buffers.Binary;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;
using Xunit;
using Xunit.Extensions;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T56 Done-when 10: the exported mercenary template table reproduces from the original DAT — all 201
/// records, in DAT order, field for field — so the committed world file is provably the DAT's own
/// table and not a transcription. Mirrors <see cref="ExportScriptReproducibilityTests"/>'s
/// <c>SkippableFact</c> pattern for the identical reason: the DAT is a local-only original file, never
/// committed.
/// </summary>
public sealed class MercenaryTemplateExportTests
{
    // decompiled-new-game-mercenary-fill.md §4, [confirmed] by the loader's sequential reads and the
    // count-backwards pin (0x1FCD6 + 0xBC4 + 0x1380 + 0x988 = 0x225A2, the DAT's length).
    private const int MercenaryBlockOffset = 0x1FCD6;
    private const int MercenaryRecordLength = 12;
    private const int MercenaryTemplateCount = 201;

    private static readonly Lazy<World> LazyWorld = new(
        () => GameDataLoader.LoadFile<World>(Path.Combine(ModelTestPaths.DataRoot, "worlds", "classical-mediterranean.json")));

    [SkippableFact]
    public void The_exported_template_table_reproduces_from_the_DAT_record_for_record()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        var dat = File.ReadAllBytes(OriginalFilesAvailability.DatPath!);
        var world = LazyWorld.Value;

        var templates = world.MercenaryTemplates;
        Assert.NotNull(templates);
        Assert.Equal(MercenaryTemplateCount, templates!.Count);

        var duplicateKeys = templates.GroupBy(t => (t.X, t.Y, t.Label, t.UnitTypeId)).Count(g => g.Count() > 1);
        Assert.Equal(4, duplicateKeys); // [confirmed: DAT] — and why templates are keyed by index.

        for (var i = 0; i < MercenaryTemplateCount; i++)
        {
            var at = MercenaryBlockOffset + i * MercenaryRecordLength;
            int Word(int offset) => BinaryPrimitives.ReadInt16LittleEndian(dat.AsSpan(at + offset, 2));

            var template = templates[i];
            Assert.True(
                template.X == Word(0) && template.Y == Word(2) && template.Label == Word(4)
                && template.TroopsBase == Word(8) && template.QualityBase == Word(10)
                && template.UnitTypeId == UnitTypeIdFor(Word(6)),
                $"Template {i} does not reproduce from the DAT: world says ({template.X},{template.Y}) label {template.Label} "
                + $"{template.UnitTypeId} base {template.TroopsBase} q {template.QualityBase}.");
        }
    }

    [SkippableFact]
    public void Template_200_is_carried_even_though_the_draw_can_never_select_it()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        // decompiled-new-game-mercenary-fill.md §2/§4: the refill draws Random(200), so record 200 is
        // data the fill never uses, and the export carries it anyway so indices match the DAT.
        var world = LazyWorld.Value;
        var last = world.MercenaryTemplates![world.MercenaryTemplates.Count - 1];

        Assert.Equal(200, world.MercenaryTemplates.Count - 1);
        Assert.Equal("light_cavalry", last.UnitTypeId);
    }

    private static string UnitTypeIdFor(int typeCode) => typeCode switch
    {
        0 => "light_infantry",
        1 => "heavy_infantry",
        2 => "archers",
        3 => "light_cavalry",
        4 => "heavy_cavalry",
        _ => throw new InvalidOperationException($"Unknown unit type code {typeCode}."),
    };
}
