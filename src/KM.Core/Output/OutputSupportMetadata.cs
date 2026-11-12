// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text.Json;

namespace KM.Core.Output;

public sealed partial class OutputTransactionCoordinator
{
    // Diagnostic reads deliberately bypass acquisition, recovery and migration. These
    // are recorded metadata, not an integrity scan of the installed game files.
    public Task<OutputOwnershipInventory?> ReadSupportOwnershipAsync(CancellationToken token = default) =>
        MissingSupportStore ? Task.FromResult<OutputOwnershipInventory?>(null) : metadata.ReadSupportJsonAsync<OutputOwnershipInventory>(
            paths.GetContainedMetadataPath(paths.MetadataRoot, "ownership.json"), token);

    public async Task<IReadOnlyList<OutputApplyReceipt>?> ReadSupportHistoryAsync(CancellationToken token = default)
    {
        if (MissingSupportStore) return null;
        var document = await metadata.ReadSupportJsonAsync<OutputApplyHistoryDocument>(
            paths.GetContainedMetadataPath(paths.MetadataRoot, "history.json"), token).ConfigureAwait(false);
        if (document is null) return null;
        if (document.SchemaVersion != OutputApplyHistoryDocument.CurrentSchemaVersion
            || document.Receipts.IsDefault || document.Receipts.Length > OutputLimits.MaximumHistoryReceipts
            || document.Receipts.Any(receipt => receipt is null)
            || document.Receipts.Select(receipt => receipt.TransactionId).Distinct().Count() != document.Receipts.Length)
            throw new InvalidDataException("Invalid recorded history.");
        foreach (var receipt in document.Receipts) receipt.HistoryDetails?.Validate();
        return document.Receipts;
    }

    private bool MissingSupportStore
    {
        get
        {
            // Exists suppresses permission and I/O failures, which diagnostics
            // must distinguish from metadata that has never been recorded.
            try { _ = File.GetAttributes(paths.MetadataRoot); return false; }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
        }
    }

    public async Task<byte[]> ReadSupportRestorationStateAsync(string hash, CancellationToken token = default)
    {
        // Only the small immutable selection document is needed, never model, sound
        // or texture bundles. Reuse the normal containment, ACL and digest checks.
        await using var stream = metadata.OpenSupportFile(PreservationArtifactPath(hash));
        if (stream.Length is 0 or > 1024 * 1024) throw new OutputLimitExceededException("Restoration state is too large.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("Restoration state changed.");
        return bytes;
    }
}

internal sealed partial class OutputMetadataStore
{
    internal async Task<T?> ReadSupportJsonAsync<T>(string path, CancellationToken token) where T : class
    {
        FileStream stream;
        try { stream = OpenSupportFile(path); }
        catch (FileNotFoundException) { return null; }
        await using var lifetime = stream;
        if (stream.Length > OutputLimits.MaximumMetadataDocumentBytes)
            throw new OutputLimitExceededException("Recorded metadata exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<T>(bytes, serializerOptions);
    }

    internal FileStream OpenSupportFile(string path)
    {
        paths.ValidateMetadataFile(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try { paths.ValidateSupportMetadataFile(stream); return stream; }
        catch { stream.Dispose(); throw; }
    }
}
