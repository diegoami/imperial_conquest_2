using System.Security.Cryptography;
using System.Text;

namespace IC2.Data;

/// <summary>Validate before exclusive, no-replace publication. Outputs are not native compatibility proof.</summary>
public static class OriginalSaveWriter
{
    public static OriginalSaveWriteReport Write(OriginalSaveWritePlan plan, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destination = Path.GetFullPath(destinationPath);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (comparer.Equals(destination, plan.TemplatePath)) throw new IOException("Destination aliases the template path.");
        if (Occupied(destination)) throw new IOException("Destination already exists.");
        var bytes = plan.Materialize();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var changed = new List<int>();
        if (plan.TaxEdit is { } edit && !plan.IsIdentity)
        {
            var offset = edit.Baseline.TaxOffset;
            if ((edit.OldTax & 255) != (edit.NewTax & 255)) changed.Add(offset);
            if ((edit.OldTax >> 8) != (edit.NewTax >> 8)) changed.Add(offset + 1);
        }
        var directory = Path.GetDirectoryName(destination)!;
        // Deterministic, bounded names; a stale or concurrently owned name is never deleted.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(destination)))[..16];
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var temporary = Path.Combine(directory, $".ic2-{key}-{attempt}.tmp");
            FileStream output;
            try { output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (Occupied(temporary)) { continue; }
            var ownsTemporary = true;
            try
            {
                using (output) { output.Write(bytes); output.Flush(flushToDisk: true); }
                File.Move(temporary, destination, overwrite: false);
                ownsTemporary = false;
                return new(destination, plan.TemplateSha256, hash, bytes.Length, plan.Layout,
                    plan.ProfileId, plan.IsIdentity, plan.TaxEdit, changed.AsReadOnly());
            }
            finally
            {
                // We reached this block only after successfully creating our exclusive artifact.
                // On successful move this name is gone; never remove any other suffix.
                if (ownsTemporary) File.Delete(temporary);
            }
        }
        throw new IOException("All 64 exclusive temporary names are occupied.");
    }

    private static bool Occupied(string path) => Path.Exists(path) || File.Exists(path) || Directory.Exists(path)
        || new FileInfo(path).LinkTarget is not null;
}
