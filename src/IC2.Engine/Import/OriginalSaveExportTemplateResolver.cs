using IC2.Data;

namespace IC2.Engine.Import;

/// <summary>Selects an explicitly named private original SAV; never guesses from a folder's contents.</summary>
public static class OriginalSaveExportTemplateResolver
{
    /// <summary>An explicit path wins; otherwise [sav-export] template in the local asset config is required.</summary>
    public static string Resolve(string? explicitTemplate, string configPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitTemplate))
            return RequireFile(Path.GetFullPath(explicitTemplate));
        return ConfiguredTemplate(configPath)
            ?? throw new FileNotFoundException(
                "No original SAV template is configured. Pass --template, or set [sav-export] template in assets.local.ini.");
    }

    /// <summary>Returns null when no config/key exists, so the game can ask through a file dialog.</summary>
    public static string? ConfiguredTemplate(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        if (!File.Exists(configPath)) return null;

        var inExport = false;
        string? template = null;
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(configPath))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inExport = string.Equals(line[1..^1].Trim(), "sav-export", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inExport) continue;
            var equals = line.IndexOf('=');
            if (equals < 0) throw new InvalidDataException($"Invalid [sav-export] INI line {lineNumber}: expected key = value.");
            if (!string.Equals(line[..equals].Trim(), "template", StringComparison.OrdinalIgnoreCase)) continue;
            if (template is not null) throw new InvalidDataException("[sav-export] template is specified more than once.");
            template = line[(equals + 1)..].Trim();
            if (template.Length >= 2 && template[0] == '"' && template[^1] == '"') template = template[1..^1];
            if (string.IsNullOrWhiteSpace(template)) throw new InvalidDataException("[sav-export] template is empty.");
        }
        if (template is null) return null;

        // The existing asset resolver pins the original installation directory and DAT identity.
        var settings = AssetSettings.Load(configPath);
        return RequireFile(settings.ResolveSavePath(template));
    }

    private static string RequireFile(string path) => File.Exists(path)
        ? path : throw new FileNotFoundException($"Original SAV template does not exist: {path}", path);
}
