// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.ExceptionServices;

namespace KM.Core.Concurrency;

public enum BoundedWorkloadKind
{
    Read,
    Decode,
    Hash,
    Map,
}

public enum BoundedConcurrencyAdmission
{
    NoWork,
    SerialItemCount,
    SerialCallerLimit,
    SerialCpuLimit,
    SerialMemoryLimit,
    SerialIndexedFailureLimit,
    SerialNestedExecution,
    ParallelCpuAndMemory,
    ParallelMemoryUnknownFallback,
}

/// <summary>
/// Classifies a pure indexed workload and states a conservative upper bound
/// for the working set retained by one active worker.
/// </summary>
public sealed class BoundedConcurrencyPolicy
{
    public const int MaximumSupportedParallelism = 64;

    public BoundedConcurrencyPolicy(
        string name,
        BoundedWorkloadKind workloadKind,
        long maximumBytesPerWorker,
        int maximumDegreeOfParallelism = 8,
        int memoryBudgetDivisor = 8,
        int degreeOfParallelismWhenMemoryUnknown = 1,
        int minimumParallelItemCount = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }

        if (!Enum.IsDefined(workloadKind))
        {
            throw new ArgumentOutOfRangeException(nameof(workloadKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytesPerWorker);
        if (maximumDegreeOfParallelism is < 1 or > MaximumSupportedParallelism)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDegreeOfParallelism));
        }

        if (memoryBudgetDivisor is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryBudgetDivisor));
        }

        if (degreeOfParallelismWhenMemoryUnknown < 1
            || degreeOfParallelismWhenMemoryUnknown > maximumDegreeOfParallelism)
        {
            throw new ArgumentOutOfRangeException(nameof(degreeOfParallelismWhenMemoryUnknown));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(minimumParallelItemCount, 2);

        Name = name;
        WorkloadKind = workloadKind;
        MaximumBytesPerWorker = maximumBytesPerWorker;
        MaximumDegreeOfParallelism = maximumDegreeOfParallelism;
        MemoryBudgetDivisor = memoryBudgetDivisor;
        DegreeOfParallelismWhenMemoryUnknown = degreeOfParallelismWhenMemoryUnknown;
        MinimumParallelItemCount = minimumParallelItemCount;
    }

    public string Name { get; }

    public BoundedWorkloadKind WorkloadKind { get; }

    public long MaximumBytesPerWorker { get; }

    public int MaximumDegreeOfParallelism { get; }

    public int MemoryBudgetDivisor { get; }

    public int DegreeOfParallelismWhenMemoryUnknown { get; }

    public int MinimumParallelItemCount { get; }
}

public readonly record struct BoundedConcurrencyPlan(
    int ItemCount,
    int DegreeOfParallelism,
    long PerCallMemoryBudgetBytes,
    BoundedConcurrencyAdmission Admission)
{
    public bool IsParallel => DegreeOfParallelism > 1;
}

public sealed class BoundedWorkItemException : Exception
{
    internal BoundedWorkItemException(
        BoundedConcurrencyPolicy policy,
        int itemIndex,
        Exception innerException)
        : base(
            $"Bounded {policy.WorkloadKind.ToString().ToLowerInvariant()} workload '{policy.Name}' failed at item index {itemIndex}.",
            innerException)
    {
        WorkloadName = policy.Name;
        WorkloadKind = policy.WorkloadKind;
        ItemIndex = itemIndex;
    }

    public string WorkloadName { get; }

    public BoundedWorkloadKind WorkloadKind { get; }

    public int ItemIndex { get; }
}

public sealed class BoundedConcurrencyResourceException : InvalidOperationException
{
    internal BoundedConcurrencyResourceException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Executes classified pure work with adaptive per-call admission and a
/// process-wide CPU/memory lease. Callbacks must not mutate shared state;
/// writing only to the callback's unique preallocated result index is allowed.
/// </summary>
public static class BoundedParallel
{
    private const int MaximumIndexedFailureSlots = 1_000_000;
    private static readonly ProcessWideCoordinator Coordinator = new(BoundedConcurrencyHostBudget.Current);

    public static BoundedConcurrencyPlan Plan(int itemCount, BoundedConcurrencyPolicy policy)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentNullException.ThrowIfNull(policy);

        if (itemCount == 0)
        {
            return new BoundedConcurrencyPlan(0, 0, 0, BoundedConcurrencyAdmission.NoWork);
        }

        var hostBudget = BoundedConcurrencyHostBudget.Current;
        if (policy.MaximumBytesPerWorker > hostBudget.MemoryBytes)
        {
            throw new BoundedConcurrencyResourceException(
                $"Bounded workload '{policy.Name}' requires more memory per worker than the process budget permits.");
        }

        if (Coordinator.IsNestedExecution)
        {
            return new BoundedConcurrencyPlan(
                itemCount,
                1,
                policy.MaximumBytesPerWorker,
                BoundedConcurrencyAdmission.SerialNestedExecution);
        }

        if (itemCount > MaximumIndexedFailureSlots)
        {
            return new BoundedConcurrencyPlan(
                itemCount,
                1,
                policy.MaximumBytesPerWorker,
                BoundedConcurrencyAdmission.SerialIndexedFailureLimit);
        }

        if (itemCount < policy.MinimumParallelItemCount)
        {
            return new BoundedConcurrencyPlan(
                itemCount,
                1,
                policy.MaximumBytesPerWorker,
                BoundedConcurrencyAdmission.SerialItemCount);
        }

        var callerLimit = Math.Min(itemCount, policy.MaximumDegreeOfParallelism);
        if (callerLimit <= 1)
        {
            return new BoundedConcurrencyPlan(
                itemCount,
                1,
                policy.MaximumBytesPerWorker,
                BoundedConcurrencyAdmission.SerialCallerLimit);
        }

        var cpuLimit = Math.Min(callerLimit, hostBudget.CpuLimit);
        if (cpuLimit <= 1)
        {
            return new BoundedConcurrencyPlan(
                itemCount,
                1,
                policy.MaximumBytesPerWorker,
                BoundedConcurrencyAdmission.SerialCpuLimit);
        }

        if (hostBudget.DetectedAvailableMemoryBytes <= 0)
        {
            var unknownMemoryDegree = Math.Min(
                cpuLimit,
                policy.DegreeOfParallelismWhenMemoryUnknown);
            return new BoundedConcurrencyPlan(
                itemCount,
                unknownMemoryDegree,
                hostBudget.MemoryBytes,
                unknownMemoryDegree > 1
                    ? BoundedConcurrencyAdmission.ParallelMemoryUnknownFallback
                    : BoundedConcurrencyAdmission.SerialMemoryLimit);
        }

        var perCallMemoryBudget = Math.Min(
            hostBudget.MemoryBytes,
            hostBudget.DetectedAvailableMemoryBytes / policy.MemoryBudgetDivisor);
        var memoryLimit = Math.Max(1, perCallMemoryBudget / policy.MaximumBytesPerWorker);
        var degreeOfParallelism = (int)Math.Min(cpuLimit, memoryLimit);
        return new BoundedConcurrencyPlan(
            itemCount,
            degreeOfParallelism,
            perCallMemoryBudget,
            degreeOfParallelism > 1
                ? BoundedConcurrencyAdmission.ParallelCpuAndMemory
                : BoundedConcurrencyAdmission.SerialMemoryLimit);
    }

    public static BoundedConcurrencyPlan For(
        int itemCount,
        BoundedConcurrencyPolicy policy,
        Action<int> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ForCore(
            itemCount,
            policy,
            (index, _) => action(index),
            failureIndexOffset: 0,
            cancellationToken);
    }

    public static BoundedConcurrencyPlan For(
        int itemCount,
        BoundedConcurrencyPolicy policy,
        Action<int, CancellationToken> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ForCore(itemCount, policy, action, failureIndexOffset: 0, cancellationToken);
    }

    public static TResult[] MapOrdered<TResult>(
        int itemCount,
        BoundedConcurrencyPolicy policy,
        Func<int, TResult> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentNullException.ThrowIfNull(policy);
        if (itemCount > MaximumIndexedFailureSlots)
        {
            throw new BoundedConcurrencyResourceException(
                $"Ordered workload '{policy.Name}' has {itemCount} items; use bounded deferred publication for more than {MaximumIndexedFailureSlots} items.");
        }

        ArgumentNullException.ThrowIfNull(map);
        cancellationToken.ThrowIfCancellationRequested();
        var results = new TResult[itemCount];
        _ = ForCore(
            itemCount,
            policy,
            (index, _) => results[index] = map(index),
            failureIndexOffset: 0,
            cancellationToken);
        return results;
    }

    public static TResult[] MapOrdered<TResult>(
        int itemCount,
        BoundedConcurrencyPolicy policy,
        Func<int, CancellationToken, TResult> map,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentNullException.ThrowIfNull(policy);
        if (itemCount > MaximumIndexedFailureSlots)
        {
            throw new BoundedConcurrencyResourceException(
                $"Ordered workload '{policy.Name}' has {itemCount} items; use bounded deferred publication for more than {MaximumIndexedFailureSlots} items.");
        }

        ArgumentNullException.ThrowIfNull(map);
        cancellationToken.ThrowIfCancellationRequested();
        var results = new TResult[itemCount];
        _ = ForCore(
            itemCount,
            policy,
            (index, token) => results[index] = map(index, token),
            failureIndexOffset: 0,
            cancellationToken);
        return results;
    }

    public static TResult[] MapOrdered<TSource, TResult>(
        IReadOnlyList<TSource> source,
        BoundedConcurrencyPolicy policy,
        Func<TSource, int, TResult> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        return MapOrdered(
            source.Count,
            policy,
            index => map(source[index], index),
            cancellationToken);
    }

    public static TResult[] MapOrdered<TSource, TResult>(
        IReadOnlyList<TSource> source,
        BoundedConcurrencyPolicy policy,
        Func<TSource, int, CancellationToken, TResult> map,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        return MapOrdered(
            source.Count,
            policy,
            (index, token) => map(source[index], index, token),
            cancellationToken);
    }

    /// <summary>
    /// Maps one explicitly bounded batch at a time, then publishes that batch
    /// serially in source order. Use this when results should not all remain in
    /// memory. A later batch failure can occur after earlier batches published.
    /// </summary>
    public static void MapDeferredOrdered<TSource, TResult>(
        IReadOnlyList<TSource> source,
        BoundedConcurrencyPolicy policy,
        int maximumBufferedItems,
        Func<TSource, int, TResult> map,
        Action<TResult, int> publish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        MapDeferredOrdered(
            source,
            policy,
            maximumBufferedItems,
            (item, index, _) => map(item, index),
            publish,
            cancellationToken);
    }

    public static void MapDeferredOrdered<TSource, TResult>(
        IReadOnlyList<TSource> source,
        BoundedConcurrencyPolicy policy,
        int maximumBufferedItems,
        Func<TSource, int, CancellationToken, TResult> map,
        Action<TResult, int> publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBufferedItems);
        if (maximumBufferedItems > MaximumIndexedFailureSlots)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBufferedItems),
                maximumBufferedItems,
                $"A deferred output batch cannot exceed {MaximumIndexedFailureSlots} items.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var batchStart = 0;
        while (batchStart < source.Count)
        {
            var batchCount = Math.Min(maximumBufferedItems, source.Count - batchStart);
            var batchResults = new TResult[batchCount];
            _ = ForCore(
                batchCount,
                policy,
                (localIndex, token) =>
                {
                    var sourceIndex = batchStart + localIndex;
                    batchResults[localIndex] = map(source[sourceIndex], sourceIndex, token);
                },
                failureIndexOffset: batchStart,
                cancellationToken);

            for (var localIndex = 0; localIndex < batchCount; localIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                publish(batchResults[localIndex], batchStart + localIndex);
            }

            batchStart += batchCount;
        }
    }

    private static BoundedConcurrencyPlan ForCore(
        int itemCount,
        BoundedConcurrencyPolicy policy,
        Action<int, CancellationToken> action,
        int failureIndexOffset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Plan(itemCount, policy);
        if (itemCount == 0)
        {
            return plan;
        }

        if (!plan.IsParallel)
        {
            using var lease = Coordinator.Acquire(
                policy,
                nested: Coordinator.IsNestedExecution,
                cancellationToken);
            for (var index = 0; index < itemCount; index++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    action(index, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new BoundedWorkItemException(policy, failureIndexOffset + index, exception);
                }
            }

            return plan;
        }

        var failures = new ExceptionDispatchInfo?[itemCount];
        try
        {
            Parallel.For(
                0,
                itemCount,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = plan.DegreeOfParallelism,
                    CancellationToken = cancellationToken,
                },
                index =>
                {
                    try
                    {
                        using var lease = Coordinator.Acquire(
                            policy,
                            nested: false,
                            cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        action(index, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (Exception exception)
                    {
                        failures[index] = ExceptionDispatchInfo.Capture(exception);
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Inspect completed slots below so a fatal memory failure retains
            // precedence over cooperative cancellation.
        }

        for (var index = 0; index < failures.Length; index++)
        {
            if (failures[index]?.SourceException is OutOfMemoryException)
            {
                failures[index]!.Throw();
            }
        }

        // Cancellation has stable precedence over per-item failures. This avoids
        // scheduler timing deciding whether a not-yet-started lower index reports
        // cancellation before a higher index reports a work failure.
        cancellationToken.ThrowIfCancellationRequested();

        for (var index = 0; index < failures.Length; index++)
        {
            if (failures[index] is not { } failure)
            {
                continue;
            }

            try
            {
                failure.Throw();
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BoundedWorkItemException(policy, failureIndexOffset + index, exception);
            }
        }

        return plan;
    }

    private sealed class ProcessWideCoordinator
    {
        private static readonly AsyncLocal<int> ExecutionDepth = new();

        private readonly object gate = new();
        private int availableCpu;
        private readonly long memoryCapacityBytes;
        private long availableMemoryBytes;

        public ProcessWideCoordinator(BoundedConcurrencyHostBudgetSnapshot hostBudget)
        {
            availableCpu = hostBudget.CpuLimit;
            memoryCapacityBytes = hostBudget.MemoryBytes;
            availableMemoryBytes = hostBudget.MemoryBytes;
        }

        public bool IsNestedExecution => ExecutionDepth.Value > 0;

        public IDisposable Acquire(
            BoundedConcurrencyPolicy policy,
            bool nested,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (nested)
                {
                    if (policy.MaximumBytesPerWorker > availableMemoryBytes)
                    {
                        throw new BoundedConcurrencyResourceException(
                            $"Nested bounded workload '{policy.Name}' cannot reserve its declared memory without exceeding the process budget.");
                    }

                    availableMemoryBytes -= policy.MaximumBytesPerWorker;
                    ExecutionDepth.Value++;
                    return new ResourceLease(this, policy.MaximumBytesPerWorker, ownsCpu: false);
                }

                EnsureSingleWorkerFits(policy);
                while (availableCpu == 0 || availableMemoryBytes < policy.MaximumBytesPerWorker)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _ = Monitor.Wait(gate, millisecondsTimeout: 50);
                }

                availableCpu--;
                availableMemoryBytes -= policy.MaximumBytesPerWorker;
                ExecutionDepth.Value++;
                return new ResourceLease(this, policy.MaximumBytesPerWorker, ownsCpu: true);
            }
        }

        private void EnsureSingleWorkerFits(BoundedConcurrencyPolicy policy)
        {
            if (policy.MaximumBytesPerWorker > memoryCapacityBytes)
            {
                throw new BoundedConcurrencyResourceException(
                    $"Bounded workload '{policy.Name}' requires more memory per worker than the process budget permits.");
            }
        }

        private void Release(long memoryBytes, bool ownsCpu)
        {
            lock (gate)
            {
                ExecutionDepth.Value--;
                availableMemoryBytes = checked(availableMemoryBytes + memoryBytes);
                if (ownsCpu)
                {
                    availableCpu++;
                }

                Monitor.PulseAll(gate);
            }
        }

        private sealed class ResourceLease : IDisposable
        {
            private ProcessWideCoordinator? owner;
            private readonly long memoryBytes;
            private readonly bool ownsCpu;

            public ResourceLease(ProcessWideCoordinator owner, long memoryBytes, bool ownsCpu)
            {
                this.owner = owner;
                this.memoryBytes = memoryBytes;
                this.ownsCpu = ownsCpu;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref owner, null)?.Release(memoryBytes, ownsCpu);
            }
        }
    }
}
