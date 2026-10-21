namespace IC2.Slice.UI;

/// <summary>
/// Bug #1283 (D3 of the v0.6.2 play-through #1280): the version the About box shows and the
/// diagnostics header logs. Godot always defines <c>application/config/version</c>, as an empty string
/// when <c>godot/project.godot</c> names none, so a <c>GetSetting(..., "development")</c> default never
/// applied and About read "Version" with nothing after it. Godot-free so the tests can link it.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// The project's version when it names one; otherwise <c>development</c>, with the commit the build
    /// was stamped with (the part after <c>+</c> in the assembly's informational version) when there is one.
    /// </summary>
    /// <param name="configVersion">Godot's <c>application/config/version</c>, possibly empty.</param>
    /// <param name="informationalVersion">The assembly's informational version, e.g. <c>1.0.0+4253d8a…</c>.</param>
    public static string Describe(string? configVersion, string? informationalVersion)
    {
        if (!string.IsNullOrWhiteSpace(configVersion))
        {
            return configVersion.Trim();
        }

        var plus = informationalVersion?.IndexOf('+') ?? -1;
        if (plus < 0 || plus == informationalVersion!.Length - 1)
        {
            return "development";
        }

        var commit = informationalVersion[(plus + 1)..];
        return $"development ({(commit.Length > 7 ? commit[..7] : commit)})";
    }
}
