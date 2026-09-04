// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using System.Security.Cryptography;
using KM.Core.Diagnostics;
using KM.Core.Files;

namespace KM.Core.Editing;

/// <summary>
/// Retains safe source context when a review cannot capture its source evidence.
/// </summary>
public static class ChangePlanSourceDiagnostics
{
    public const string ReviewField = "changePlanReview";
    public const string SourceFingerprintField = "changePlanSourceFingerprint";

    public static ValidationDiagnostic CreateFailure(
        Exception exception,
        EditSession session,
        string fallbackDomain)
    {
        ArgumentNullException.ThrowIfNull(session);
        var failure = ProjectFileFailureClassifier.Classify(
            exception,
            fileContextUsesConfiguredDataSupport: true);
        var fingerprintFailure = IsSourceFingerprintFailure(exception);
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var domain = domains is [var candidate]
            && candidate is { Length: > 9 and <= 96 }
            && candidate.StartsWith("workflow.", StringComparison.Ordinal)
            && candidate.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_')
                ? candidate
                : fallbackDomain;
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            fingerprintFailure
                ? "KM could not review this change because its source fingerprint is invalid."
                : failure.Message,
            failure.FileContext?.VirtualPath,
            domain,
            fingerprintFailure ? SourceFingerprintField : ReviewField,
            fingerprintFailure
                ? "Each planned output must have valid source-verification metadata. Stage the affected changes again and review. If this repeats, report the error code and affected file."
                : failure.Expected)
        {
            Code = failure.Code,
        };
    }

    public static T WithFileContext<T>(
        Func<T> capture,
        string relativePath,
        ProjectFileLayer? layer = null,
        ProjectFileOperation operation = ProjectFileOperation.Inspect)
    {
        ArgumentNullException.ThrowIfNull(capture);
        try
        {
            return capture();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or CryptographicException)
        {
            if (exception is ProjectFileOperationException)
            {
                throw;
            }

            ProjectFileOperationException? contextual = null;
            try
            {
                contextual = new ProjectFileOperationException(
                    operation, relativePath, layer, innerException: exception);
            }
            catch (ArgumentException)
            {
                // Invalid source identities must not enter diagnostic context.
            }

            if (contextual is null)
            {
                throw;
            }

            throw contextual;
        }
    }

    private static bool IsSourceFingerprintFailure(Exception exception)
    {
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < 8; depth++)
        {
            if (current is ChangePlanSourceFingerprintException)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}

/// <summary>
/// A producer supplied invalid review metadata, rather than unreadable game data.
/// </summary>
public sealed class ChangePlanSourceFingerprintException : IOException
{
    public ChangePlanSourceFingerprintException()
        : base("A planned source fingerprint is invalid.", new InvalidDataException())
    {
    }
}
