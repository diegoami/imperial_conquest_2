using System.Runtime.InteropServices;
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
        Publish(bytes, destination);
        return new(destination, plan.TemplateSha256, hash, bytes.Length, plan.Layout,
            plan.ProfileId, plan.IsIdentity, plan.TaxEdit, changed.AsReadOnly());
    }

    /// <summary>
    /// T184/T185: writes <paramref name="document"/> in the canonical from-scratch form to a new file. The
    /// gate-2 policy (<see cref="OriginalSaveExportPolicy"/>, from-scratch mode) and the checked encoding run
    /// first; any violation refuses before a byte is written. The encoded bytes are then re-validated through
    /// the complete framing and the production readers, and published exclusively without replacing anything.
    /// </summary>
    public static OriginalSaveDocumentWriteReport WriteDocument(OriginalSaveDocument document, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var violations = OriginalSaveExportPolicy.Check(document, fromScratch: true);
        if (violations.Count > 0)
            throw new OriginalSavePolicyException(violations);
        var bytes = OriginalSaveCodec.Encode(document);
        var layout = OriginalSaveLayout.Validate(bytes);
        var destination = Path.GetFullPath(destinationPath);
        if (Occupied(destination)) throw new IOException("Destination already exists.");
        Publish(bytes, destination);
        return new(destination, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, layout);
    }

    private static void Publish(byte[] bytes, string destination)
    {
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
                PublishNoReplace(temporary, destination);
                ownsTemporary = false;
                return;
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

    /// <summary>
    /// Bug #1298: publishes <paramref name="temporary"/> as <paramref name="destination"/> only if nothing is there,
    /// atomically. On Unix <see cref="File.Move(string, string, bool)"/> with <c>overwrite: false</c> checks and then
    /// <c>rename(2)</c>s, so two concurrent writers could both succeed, the second replacing the first. <c>link(2)</c>
    /// fails with <c>EEXIST</c> atomically instead; the temporary name is removed once the link exists. Windows'
    /// no-replace move is already atomic. A filesystem without hard links falls back to the move.
    /// </summary>
    private static void PublishNoReplace(string temporary, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Move(temporary, destination, overwrite: false);
            return;
        }

        if (link(temporary, destination) == 0)
        {
            File.Delete(temporary);
            return;
        }

        var errno = Marshal.GetLastPInvokeError();
        if (errno == EEXIST) throw new IOException("Destination already exists.");
        File.Move(temporary, destination, overwrite: false);
    }

    /// <summary><c>EEXIST</c>: 17 on Linux and macOS.</summary>
    private const int EEXIST = 17;

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    private static bool Occupied(string path) => Path.Exists(path) || File.Exists(path) || Directory.Exists(path)
        || new FileInfo(path).LinkTarget is not null;
}
