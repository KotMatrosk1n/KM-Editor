// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Output;

/// <summary>Preserves output failure reasons without exposing host paths from I/O exceptions.</summary>
public static class OutputFailureDiagnostics
{
    public const string MigrationSummary = "Output metadata could not be migrated safely.";
    public const string RecoverySummary = "Output recovery status could not be verified. Applying changes remains blocked until it can be checked.";

    public static string Describe(Exception exception) => exception switch
    {
        OutputRootLockTimeoutException => "The output workspace lock could not be acquired before the timeout.",
        OutputCoordinatorException => exception.Message,
        IOException { HResult: unchecked((int)0x80070020) or unchecked((int)0x80070021) } =>
            "A required output metadata file is locked or in use.",
        _ => ProjectFileFailureClassifier.Classify(exception).Message,
    };

    public static string? TechnicalDetail(Exception exception)
    {
        string? securityCheck = null;
        string? detail = null;
        var depth = 0;
        for (Exception? cause = exception; cause is not null && depth++ < 8; cause = cause.InnerException)
        {
            if (cause is OutputMetadataSecurityException security)
                securityCheck = $"{nameof(OutputMetadataSecurityException)} ({security.Failure})";
            if (cause is IOException or UnauthorizedAccessException or System.Security.SecurityException
                or System.ComponentModel.Win32Exception)
            {
                var nativeError = cause is System.ComponentModel.Win32Exception native
                    ? $", Win32 {native.NativeErrorCode}" : "";
                detail = $"{cause.GetType().Name} (0x{cause.HResult:X8}{nativeError})";
            }
        }
        var description = securityCheck is null ? detail : detail is null ? securityCheck : $"{securityCheck}: {detail}";
        if (OutputDiagnosticJournal.FindFailure(exception) is { } context)
            description = $"{(description is null ? "" : description + "; ")}Stage={context.Stage}; Object={context.ObjectRole}; Owner={context.ObjectOwner}; TokenOwner={context.TokenOwner}; Elevated={context.Elevated}";
        return description;
    }
}

public enum OutputWorkspaceMigrationFailure
{
    PreviousFailureUnavailable,
    PortableLegacyOwnership,
    OtherGameTransactions,
    PreservedCopyMissing,
    PublishedStoreMissing,
    MetadataChanged,
    PreservedCopyMismatch,
    InvalidHistory,
    InvalidJson,
    InvalidMetadata,
    IncompatibleGame,
    ConflictingTransactions,
    OutputStateMismatch,
    UnboundWorkingMetadata,
    WorkingBindingMismatch,
    MetadataDocumentTooLarge,
    TransactionMismatch,
    UnexpectedTransactionFiles,
    MetadataDepthLimit,
    MetadataEntryLimit,
    MetadataSizeLimit,
    DestinationExists,
    CopyVerificationFailed,
}

public sealed class OutputWorkspaceMigrationException : OutputCoordinatorException
{
    public OutputWorkspaceMigrationException(OutputWorkspaceMigrationFailure failure = OutputWorkspaceMigrationFailure.PreviousFailureUnavailable)
        : base(Describe(failure))
    {
        Failure = failure;
    }

    public OutputWorkspaceMigrationFailure Failure { get; }

    private static string Describe(OutputWorkspaceMigrationFailure failure) => failure switch
    {
        OutputWorkspaceMigrationFailure.PortableLegacyOwnership => "Ownership of existing .km metadata cannot be verified on this FAT or exFAT volume.",
        OutputWorkspaceMigrationFailure.OtherGameTransactions => "Another game has unfinished output transactions in this workspace.",
        OutputWorkspaceMigrationFailure.PreservedCopyMissing => "The preserved metadata copy needed to resume migration is missing.",
        OutputWorkspaceMigrationFailure.PublishedStoreMissing => "The metadata store needed to finish migration is missing.",
        OutputWorkspaceMigrationFailure.MetadataChanged => "Output metadata changed during migration and no longer matches its preserved copy.",
        OutputWorkspaceMigrationFailure.PreservedCopyMismatch => "A preserved metadata copy failed its integrity check.",
        OutputWorkspaceMigrationFailure.InvalidHistory => "Output history contains invalid, duplicate, or unsupported receipts.",
        OutputWorkspaceMigrationFailure.InvalidJson => "Output metadata contains invalid or incomplete JSON.",
        OutputWorkspaceMigrationFailure.InvalidMetadata => "Output metadata failed validation.",
        OutputWorkspaceMigrationFailure.IncompatibleGame => "Output metadata belongs to a different game or game family.",
        OutputWorkspaceMigrationFailure.ConflictingTransactions => "Both metadata copies contain unfinished transactions, so neither can be selected safely.",
        OutputWorkspaceMigrationFailure.OutputStateMismatch => "Neither metadata copy matches the current output files.",
        OutputWorkspaceMigrationFailure.UnboundWorkingMetadata => "The output working folder contains metadata without a workspace binding.",
        OutputWorkspaceMigrationFailure.WorkingBindingMismatch => "The output working folder is bound to a different workspace and still contains data.",
        OutputWorkspaceMigrationFailure.MetadataDocumentTooLarge => "An output metadata document exceeds its size limit.",
        OutputWorkspaceMigrationFailure.TransactionMismatch => "Stored and working transaction data do not match.",
        OutputWorkspaceMigrationFailure.UnexpectedTransactionFiles => "The stored transaction folder contains unexpected files.",
        OutputWorkspaceMigrationFailure.MetadataDepthLimit => "Output metadata exceeds the migration folder depth limit.",
        OutputWorkspaceMigrationFailure.MetadataEntryLimit => "Output metadata exceeds the migration file count limit.",
        OutputWorkspaceMigrationFailure.MetadataSizeLimit => "Output metadata exceeds the migration storage size limit.",
        OutputWorkspaceMigrationFailure.DestinationExists => "The metadata migration destination already exists.",
        OutputWorkspaceMigrationFailure.CopyVerificationFailed => "A metadata copy failed verification during migration.",
        _ => "A previous migration was blocked without recording the failure reason. Preserved metadata copies require review.",
    };
}
