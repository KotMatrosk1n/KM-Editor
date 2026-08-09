// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public enum ProjectFileOperation
{
    Read,
    Decode,
    Inspect,
}

/// <summary>
/// Adds safe project-file context to an I/O failure without retaining an absolute host path.
/// </summary>
public sealed class ProjectFileOperationException : IOException
{
    private const int MaximumVirtualPathLength = 512;

    public ProjectFileOperationException(
        ProjectFileOperation operation,
        string virtualPath,
        ProjectFileLayer? layer = null,
        ProjectFileGraphEntryState? state = null,
        Exception? innerException = null)
        : base(CreateMessage(operation), innerException)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown project file operation.");
        }

        if (layer is not null && !Enum.IsDefined(layer.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown project file layer.");
        }

        if (state is not null && !Enum.IsDefined(state.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown project file state.");
        }

        Operation = operation;
        VirtualPath = NormalizeVirtualPath(virtualPath);
        Layer = layer;
        State = state;
    }

    public ProjectFileOperation Operation { get; }

    public string VirtualPath { get; }

    public ProjectFileLayer? Layer { get; }

    public ProjectFileGraphEntryState? State { get; }

    private static string CreateMessage(ProjectFileOperation operation)
    {
        return operation switch
        {
            ProjectFileOperation.Read => "A project file could not be read.",
            ProjectFileOperation.Decode => "A project file could not be decoded.",
            ProjectFileOperation.Inspect => "A project file source could not be inspected.",
            _ => "A project file operation failed.",
        };
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var normalizedPath = virtualPath.Trim().Replace('\\', '/');
        if (normalizedPath.Length > MaximumVirtualPathLength)
        {
            throw new ArgumentException(
                $"Project file context cannot exceed {MaximumVirtualPathLength} characters.",
                nameof(virtualPath));
        }

        if (normalizedPath.StartsWith("/", StringComparison.Ordinal)
            || HasWindowsDrivePrefix(normalizedPath)
            || normalizedPath.Contains(':')
            || normalizedPath.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Project file context must use a safe relative or virtual path.",
                nameof(virtualPath));
        }

        var segments = normalizedPath.Split('/');
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
        {
            throw new ArgumentException(
                "Project file context cannot contain empty or dot path segments.",
                nameof(virtualPath));
        }

        return string.Join('/', segments);
    }

    private static bool HasWindowsDrivePrefix(string path)
    {
        return path.Length >= 2
            && char.IsAsciiLetter(path[0])
            && path[1] == ':';
    }
}
