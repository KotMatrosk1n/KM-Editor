// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Output;

public enum OutputMetadataSecurityFailure
{
    CurrentWindowsAccountUnavailable,
    OwnerMismatch,
    PermissionsMismatch,
    DirectoryPermissionsUnavailable,
    DirectoryCreationUnavailable,
    FilePermissionsUnavailable,
    WorkspaceStorageUnsafe,
}

/// <summary>Retains the failed security check without exposing filesystem paths.</summary>
public sealed class OutputMetadataSecurityException : OutputPathSecurityException
{
    public OutputMetadataSecurityException(OutputMetadataSecurityFailure failure)
        : base(Describe(failure)) => Failure = failure;

    public OutputMetadataSecurityException(OutputMetadataSecurityFailure failure, Exception innerException)
        : base(Describe(failure), innerException) => Failure = failure;

    public OutputMetadataSecurityFailure Failure { get; }

    private static string Describe(OutputMetadataSecurityFailure failure) => failure switch
    {
        OutputMetadataSecurityFailure.CurrentWindowsAccountUnavailable => "KM Editor could not identify the current Windows account.",
        OutputMetadataSecurityFailure.OwnerMismatch => "Output metadata is owned by a different Windows account.",
        OutputMetadataSecurityFailure.PermissionsMismatch => "Output metadata permissions could not be verified as private to the current Windows account.",
        OutputMetadataSecurityFailure.DirectoryPermissionsUnavailable => "Windows permissions for an output metadata directory could not be read or updated.",
        OutputMetadataSecurityFailure.DirectoryCreationUnavailable => "A private output metadata directory could not be created or verified.",
        OutputMetadataSecurityFailure.FilePermissionsUnavailable => "Windows permissions for an output metadata file could not be read or updated.",
        OutputMetadataSecurityFailure.WorkspaceStorageUnsafe => "The workspace storage path contains an unsafe or inaccessible filesystem entry.",
        _ => "The output path could not be proven safe.",
    };
}
