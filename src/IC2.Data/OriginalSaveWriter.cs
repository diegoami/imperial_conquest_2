using System.Security.Cryptography;

namespace IC2.Data;

/// <summary>Safe publication of validated identity plans. Changed-state serialization remains
/// blocked by <see cref="OriginalSaveWritePlan.StateChangeBlockers"/>; this API cannot accept it.</summary>
public static class OriginalSaveWriter
{
    public static OriginalSaveWriteReport Write(OriginalSaveWritePlan plan, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (comparer.Equals(destination, plan.TemplatePath))
            throw new IOException("Destination aliases the template path.");
        // Existing hardlinks, symlinks, case aliases and parent-directory aliases are also refused
        // by no-replace publication. A dangling link must not be mistaken for an available name.
        if (Path.Exists(destination) || File.Exists(destination) || Directory.Exists(destination)
            || new FileInfo(destination).LinkTarget is not null)
            throw new IOException("Destination already exists.");
        var bytes = plan.CopyBytes();
        OriginalSaveLayout.Validate(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (hash != plan.TemplateSha256) throw new InvalidDataException("Identity snapshot mismatch.");
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(directory, $".ic2-{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
            ownsTemporary = false;
            return new(destination, plan.TemplateSha256, hash, bytes.Length, plan.Layout);
        }
        finally
        {
            if (ownsTemporary) File.Delete(temporary);
        }
    }
}
