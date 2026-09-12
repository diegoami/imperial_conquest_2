using System;
using System.IO;

namespace IC2.Data;

/// <summary>Local, user-specific location of original Imperial Conquest 2 files.</summary>
public sealed class AssetSettings
{
    private AssetSettings(string directoryPath) => DirectoryPath = directoryPath;

    public string DirectoryPath { get; }
    public string DatPath => Path.Combine(DirectoryPath, "Imperial Conquest 2.dat");

    public string ResolveSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
            throw new ArgumentException("Save path cannot be empty.", nameof(savePath));
        return Path.GetFullPath(Path.IsPathRooted(savePath)
            ? savePath
            : Path.Combine(DirectoryPath, savePath));
    }

    public static AssetSettings Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path cannot be empty.", nameof(configPath));
        var fullConfigPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullConfigPath))
            throw new FileNotFoundException("Local asset config is missing. Copy assets.example.ini to assets.local.ini and set [assets] directory.", fullConfigPath);

        var inAssets = false;
        string? directory = null;
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(fullConfigPath))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inAssets = string.Equals(line.Substring(1, line.Length - 2).Trim(), "assets", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inAssets) continue;
            var equals = line.IndexOf('=');
            if (equals < 0) throw new InvalidDataException($"Invalid INI line {lineNumber}: expected key = value.");
            if (!string.Equals(line.Substring(0, equals).Trim(), "directory", StringComparison.OrdinalIgnoreCase)) continue;
            if (directory is not null) throw new InvalidDataException("[assets] directory is specified more than once.");
            directory = line.Substring(equals + 1).Trim();
            if (directory.Length >= 2 && directory[0] == '"' && directory[^1] == '"')
                directory = directory.Substring(1, directory.Length - 2);
        }
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidDataException("Set [assets] directory in assets.local.ini.");

        var expanded = Environment.ExpandEnvironmentVariables(directory);
        var basePath = Path.GetDirectoryName(fullConfigPath)!;
        var resolved = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(basePath, expanded));
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException($"Asset directory does not exist: {resolved}");

        var settings = new AssetSettings(resolved);
        if (!File.Exists(settings.DatPath))
            throw new FileNotFoundException("Imperial Conquest 2.dat was not found in the configured asset directory.", settings.DatPath);
        return settings;
    }
}

