// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Output;

public static class OutputLimits
{
    public const int StandardMaximumMutationsPerApply = 1_024;
    public const int MaximumMutationsPerApply = 2_048;
    public const int MaximumOwnershipClaimsPerMutation = 256;
    public const int MaximumOriginsPerApply = 64;
    public const int MaximumCreatedDirectoriesPerApply = 4_096;
    public const int MaximumHistoryReceipts = 1_024;
    public const int MaximumCheckpoints = 64;
    public const int MaximumIntegrityEntries = 250_000;
    public const int MaximumRecoveryTransactions = 1_024;
    public const int MaximumWriteBytesPerMutation = 256 * 1024 * 1024;
    public const long MaximumWriteBytesPerApply = 1024L * 1024L * 1024L;
    public const long MaximumFingerprintFileBytes = 16L * 1024L * 1024L * 1024L;
    public const long MaximumBackupBytesPerApply = 16L * 1024L * 1024L * 1024L;
    public const long MaximumCheckpointBytes = 4L * 1024L * 1024L * 1024L;
    public const int MaximumMetadataDocumentBytes = 32 * 1024 * 1024;
    public const int MaximumRevisionTokens = 2_000_000;
    public const int MaximumJournalRevisionTokens = 4_100_000;
    public const int MaximumOutputPathDepth = 256;
    public const int MaximumInventoryDirectories = 250_000;
    public const int MaximumMetadataTreeEntries = 10_000;
    public const int MaximumMetadataTreeDepth = 16;
}
