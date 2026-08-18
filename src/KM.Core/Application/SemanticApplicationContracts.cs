// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Semantics;

namespace KM.Core.Application;

/// <summary>
/// A bounded cursor window for lazy semantic operations. Continuation tokens are
/// opaque provider values and must never be interpreted by the common layer.
/// </summary>
public sealed record SemanticPageWindow
{
    public const int MaximumPageSize = 200;
    private const int MaximumContinuationTokenLength = 2_048;

    public SemanticPageWindow(int limit, string? continuationToken = null)
    {
        if (limit is <= 0 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                $"A semantic page must request between 1 and {MaximumPageSize} items.");
        }

        if (continuationToken is { Length: > MaximumContinuationTokenLength }
            || continuationToken?.Any(char.IsControl) is true)
        {
            throw new ArgumentException(
                "A semantic continuation token is invalid or too large.",
                nameof(continuationToken));
        }

        Limit = limit;
        ContinuationToken = continuationToken;
    }

    public int Limit { get; }

    public string? ContinuationToken { get; }
}

public sealed record RevisionBoundResult<TResult>
    where TResult : notnull
{
    public RevisionBoundResult(ProjectSourceRevision revision, TResult result)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    public ProjectSourceRevision Revision { get; }

    public TResult Result { get; }
}

public sealed record RevisionBoundPage<TItem>
    where TItem : class
{
    public RevisionBoundPage(
        ProjectSourceRevision revision,
        IEnumerable<TItem> items,
        string? continuationToken = null)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        ArgumentNullException.ThrowIfNull(items);

        var itemBuilder = ImmutableArray.CreateBuilder<TItem>();
        foreach (var item in items)
        {
            if (itemBuilder.Count == SemanticPageWindow.MaximumPageSize)
            {
                throw new ArgumentException(
                    $"A semantic result page cannot contain more than {SemanticPageWindow.MaximumPageSize} items.",
                    nameof(items));
            }

            if (item is null)
            {
                throw new ArgumentException("A semantic result page cannot contain null items.", nameof(items));
            }

            itemBuilder.Add(item);
        }

        if (continuationToken is { Length: > 2_048 }
            || continuationToken?.Any(char.IsControl) is true)
        {
            throw new ArgumentException(
                "A semantic continuation token is invalid or too large.",
                nameof(continuationToken));
        }

        Items = itemBuilder.ToImmutable();
        ContinuationToken = continuationToken;
    }

    public ProjectSourceRevision Revision { get; }

    public ImmutableArray<TItem> Items { get; }

    public string? ContinuationToken { get; }
}

/// <summary>
/// Shared shape for typed, cancellable, revision-bound application queries.
/// Specific feature interfaces retain their own typed request and result models.
/// </summary>
public interface ISemanticQueryModule<in TQuery, TResult> : ISemanticApplicationModule
    where TQuery : class
    where TResult : notnull
{
    ValueTask<RevisionBoundResult<TResult>> QueryAsync(
        ProjectSourceRevision expectedRevision,
        TQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared shape for large semantic queries that must page rather than attach
/// their result corpus to an existing editor workflow payload.
/// </summary>
public interface IPagedSemanticQueryModule<in TQuery, TItem> : ISemanticApplicationModule
    where TQuery : class
    where TItem : class
{
    ValueTask<RevisionBoundPage<TItem>> QueryAsync(
        ProjectSourceRevision expectedRevision,
        TQuery query,
        SemanticPageWindow page,
        CancellationToken cancellationToken = default);
}
