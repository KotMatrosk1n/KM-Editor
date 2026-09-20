// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using System.Text.Json;

namespace KM.Core.Files;

/// <summary>
/// Classifies project-file failures without exposing host paths or exception details.
/// </summary>
public static class ProjectFileFailureClassifier
{
    public const string AccessDeniedCode = "KM-BRIDGE-ACCESS-DENIED";
    public const string ResourceMissingCode = "KM-BRIDGE-RESOURCE-MISSING";
    public const string DataInvalidCode = "KM-BRIDGE-DATA-INVALID";
    public const string DataLayoutInvalidCode = "KM-BRIDGE-DATA-LAYOUT-INVALID";
    public const string DataSupportUnavailableCode = "KM-BRIDGE-SUPPORT-RUNTIME-UNAVAILABLE";
    public const string IoFailedCode = "KM-BRIDGE-IO-FAILED";
    public const string InternalFailureCode = "KM-BRIDGE-INTERNAL-FAILURE";
    public const string DataTruncatedCode = "KM-BRIDGE-DATA-TRUNCATED";
    public const string StoredJsonInvalidCode = "KM-BRIDGE-STORED-JSON-INVALID";
    public const string ResourceBusyCode = "KM-BRIDGE-RESOURCE-BUSY";
    public const string StorageFullCode = "KM-BRIDGE-STORAGE-FULL";
    public const string PathTooLongCode = "KM-BRIDGE-PATH-TOO-LONG";

    public static ProjectFileFailure Classify(
        Exception exception,
        bool fileContextUsesConfiguredDataSupport = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptions = EnumerateExceptions(exception);
        var fileContext = exceptions.OfType<ProjectFileOperationException>().FirstOrDefault();
        var configuredDataSupportCauses = fileContextUsesConfiguredDataSupport && fileContext is not null
            ? EnumerateExceptions(fileContext)
                .ToHashSet<Exception>(ReferenceEqualityComparer.Instance)
            : new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var category = exceptions
            .Select(candidate => ClassifySingleException(
                candidate,
                configuredDataSupportCauses.Contains(candidate)))
            .OrderBy(candidate => candidate.Priority)
            .First();

        return new ProjectFileFailure(
            category.Code,
            category.Message,
            AppendFileContext(category.Expected, fileContext),
            fileContext);
    }

    private static string AppendFileContext(
        string expected,
        ProjectFileOperationException? fileContext)
    {
        if (fileContext is null)
        {
            return expected;
        }

        var operationContext = fileContext.Operation switch
        {
            ProjectFileOperation.Read => "The selected project resource was being read.",
            ProjectFileOperation.Decode => "The selected project resource was being decoded.",
            ProjectFileOperation.Inspect => "A candidate project source was being inspected.",
            _ => string.Empty,
        };
        var sourceContext = CreateSourceContext(fileContext);

        return string.Join(
            ' ',
            new[] { expected, operationContext, sourceContext }
                .Where(value => value.Length > 0));
    }

    private static string CreateSourceContext(ProjectFileOperationException fileContext)
    {
        if (fileContext.Operation == ProjectFileOperation.Inspect)
        {
            return fileContext.Layer switch
            {
                ProjectFileLayer.Layered =>
                    "KM could not verify whether the output source contains this file, so no file copy was selected.",
                ProjectFileLayer.Base =>
                    "KM could not verify whether the base source contains this file, so no file copy was selected.",
                _ => "KM could not verify a candidate source, so no file copy was selected.",
            };
        }

        return (fileContext.Layer, fileContext.State) switch
        {
            (ProjectFileLayer.Layered, ProjectFileGraphEntryState.LayeredOverride) =>
                "The project file graph reports both base and output copies, and the output copy was selected.",
            (ProjectFileLayer.Base, ProjectFileGraphEntryState.LayeredOverride) =>
                "The project file graph reports both base and output copies, and the base copy was selected.",
            (ProjectFileLayer.Layered, ProjectFileGraphEntryState.LayeredOnly) =>
                "The project file graph reports only an output copy, and that copy was selected.",
            (ProjectFileLayer.Base, ProjectFileGraphEntryState.BaseOnly) =>
                "The project file graph reports only a base copy, and that copy was selected.",
            (ProjectFileLayer.Layered, _) => "The output copy was selected.",
            (ProjectFileLayer.Base, _) => "The base copy was selected.",
            (ProjectFileLayer.Pending, _) => "A pending project copy was selected.",
            (ProjectFileLayer.Generated, _) => "A generated project copy was selected.",
            (null, ProjectFileGraphEntryState.LayeredOverride) =>
                "The project file graph reports both base and output copies.",
            (null, ProjectFileGraphEntryState.BaseOnly) =>
                "The project file graph reports only a base copy.",
            (null, ProjectFileGraphEntryState.LayeredOnly) =>
                "The project file graph reports only an output copy.",
            _ => string.Empty,
        };
    }

    private static FailureCategory ClassifySingleException(
        Exception exception,
        bool canAttributeConfiguredDataSupport)
    {
        return exception switch
        {
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                when canAttributeConfiguredDataSupport =>
                FailureCategories.SupportRuntime,
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException =>
                FailureCategories.Internal,
            UnauthorizedAccessException or SecurityException => FailureCategories.AccessDenied,
            FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException =>
                FailureCategories.MissingResource,
            EndOfStreamException => FailureCategories.TruncatedData,
            JsonException => FailureCategories.InvalidStoredJson,
            InvalidDataException or FormatException =>
                FailureCategories.InvalidData,
            IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException =>
                FailureCategories.DataLayout,
            ProjectFileOperationException { InnerException: not null } =>
                FailureCategories.Internal,
            ProjectFileOperationException { Operation: ProjectFileOperation.Decode } =>
                FailureCategories.DataLayout,
            PathTooLongException => FailureCategories.PathTooLong,
            IOException { HResult: unchecked((int)0x80070020) or unchecked((int)0x80070021) } =>
                FailureCategories.ResourceBusy,
            IOException { HResult: unchecked((int)0x80070070) or unchecked((int)0x80070027) } =>
                FailureCategories.StorageFull,
            IOException => FailureCategories.Io,
            _ => FailureCategories.Internal,
        };
    }

    private static IReadOnlyList<Exception> EnumerateExceptions(Exception exception)
    {
        var exceptions = new List<Exception>();
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            exceptions.Add(current);
            if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }

            if (current is AggregateException aggregateException)
            {
                for (var index = aggregateException.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregateException.InnerExceptions[index]);
                }
            }
        }

        return exceptions;
    }

    private sealed record FailureCategory(
        int Priority,
        string Code,
        string Message,
        string Expected);

    private static class FailureCategories
    {
        public static readonly FailureCategory TruncatedData = new(
            3,
            DataTruncatedCode,
            "A project resource ended before all required data could be read.",
            "Verify the affected file is complete and matches the supported format, then retry.");

        public static readonly FailureCategory InvalidStoredJson = new(
            3,
            StoredJsonInvalidCode,
            "Stored JSON data could not be decoded.",
            "Check the affected stored document for invalid or incomplete JSON, then retry.");

        public static readonly FailureCategory ResourceBusy = new(
            6,
            ResourceBusyCode,
            "Another process has locked a required project resource.",
            "Close the process using the affected file, then retry the operation.");

        public static readonly FailureCategory StorageFull = new(
            6,
            StorageFullCode,
            "The storage device has insufficient free space for this operation.",
            "Free space on the affected storage device, then retry the operation.");

        public static readonly FailureCategory PathTooLong = new(
            6,
            PathTooLongCode,
            "A project resource path exceeds the supported length.",
            "Use a shorter project folder path, then retry the operation.");

        public static readonly FailureCategory SupportRuntime = new(
            0,
            DataSupportUnavailableCode,
            "Configured data support could not be loaded.",
            "Verify the configured data support folder is compatible and retry the operation.");

        public static readonly FailureCategory AccessDenied = new(
            1,
            AccessDeniedCode,
            "KM Editor could not access a required project resource.",
            "Confirm the selected project folders are accessible and retry the operation.");

        public static readonly FailureCategory MissingResource = new(
            2,
            ResourceMissingCode,
            "A required project resource is unavailable.",
            "Verify the selected project folders are complete and retry the operation.");

        public static readonly FailureCategory InvalidData = new(
            4,
            DataInvalidCode,
            "A project resource contains invalid or unsupported data.",
            "Restore the affected resource from a clean, supported source, then retry.");

        public static readonly FailureCategory DataLayout = new(
            5,
            DataLayoutInvalidCode,
            "A project resource does not match the supported data layout.",
            "Use files from a supported game version, then retry.");

        public static readonly FailureCategory Io = new(
            7,
            IoFailedCode,
            "A project resource could not be read or written.",
            "Check that the selected folders are available and not locked, then retry the operation.");

        public static readonly FailureCategory Internal = new(
            8,
            InternalFailureCode,
            "The project bridge could not complete this operation.",
            "Retry the operation. If it fails again, report the error code and operation details.");
    }
}

public sealed record ProjectFileFailure(
    string Code,
    string Message,
    string Expected,
    ProjectFileOperationException? FileContext);
