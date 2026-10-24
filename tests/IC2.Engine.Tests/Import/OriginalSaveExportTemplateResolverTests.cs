using IC2.Engine.Import;
using Xunit;

namespace IC2.Engine.Tests.Import;

public sealed class OriginalSaveExportTemplateResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-export-template-" + Guid.NewGuid().ToString("N"));

    public OriginalSaveExportTemplateResolverTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Explicit_template_wins_even_when_the_config_is_missing()
    {
        var template = Path.Combine(_root, "explicit.sav");
        File.WriteAllBytes(template, [1]);
        Assert.Equal(template, OriginalSaveExportTemplateResolver.Resolve(template, Path.Combine(_root, "missing.ini")));
    }

    [Fact]
    public void Configured_template_is_explicitly_named_relative_to_the_original_asset_directory()
    {
        var assets = Path.Combine(_root, "original assets");
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, "Imperial Conquest 2.dat"), [1]);
        var template = Path.Combine(assets, "chosen.sav");
        File.WriteAllBytes(template, [1]);
        File.WriteAllBytes(Path.Combine(assets, "newer.sav"), [1]);
        var config = Path.Combine(_root, "assets.local.ini");
        File.WriteAllText(config, "[assets]\ndirectory = \"original assets\"\n[sav-export]\ntemplate = chosen.sav\n");

        Assert.Equal(template, OriginalSaveExportTemplateResolver.ConfiguredTemplate(config));
        Assert.Equal(template, OriginalSaveExportTemplateResolver.Resolve(null, config));
    }

    [Fact]
    public void Missing_ambiguous_and_invalid_config_never_select_an_arbitrary_save()
    {
        var config = Path.Combine(_root, "assets.local.ini");
        Assert.Null(OriginalSaveExportTemplateResolver.ConfiguredTemplate(config));
        Assert.Throws<FileNotFoundException>(() => OriginalSaveExportTemplateResolver.Resolve(null, config));

        File.WriteAllText(config, "[sav-export]\ntemplate = a.sav\ntemplate = b.sav\n");
        Assert.Throws<InvalidDataException>(() => OriginalSaveExportTemplateResolver.ConfiguredTemplate(config));
        File.WriteAllText(config, "[sav-export]\ntemplate = \n");
        Assert.Throws<InvalidDataException>(() => OriginalSaveExportTemplateResolver.ConfiguredTemplate(config));
    }
}
