// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.Core.Concurrency;

/// <summary>
/// Describes the upper CPU and memory budget shared by every bounded managed
/// workload in this process. The values are admission limits, not allocations.
/// </summary>
public readonly record struct BoundedConcurrencyHostBudgetSnapshot(
    int CpuLimit,
    long MemoryBytes,
    long DetectedAvailableMemoryBytes,
    bool CpuOverrideApplied,
    bool MemoryOverrideApplied,
    bool InvalidOverrideIgnored);

/// <summary>
/// Resolves the process-wide managed concurrency budget once. A sidecar host
/// may divide machine resources between processes with the documented
/// environment variables before starting each process.
/// </summary>
public static class BoundedConcurrencyHostBudget
{
    public const string CpuLimitEnvironmentVariable = "KM_MANAGED_CONCURRENCY_CPU_LIMIT";
    public const string MemoryBytesEnvironmentVariable = "KM_MANAGED_CONCURRENCY_MEMORY_BYTES";
    public const string ReadWorkerEnvironmentVariable = "KM_MANAGED_READ_WORKER";

    private const int MaximumCpuLimit = 64;
    private const long MinimumMemoryOverrideBytes = 64L * 1024L * 1024L;
    private const long MinimumAdaptiveMemoryBudgetBytes = 1024L * 1024L * 1024L;
    private const long UnknownMemoryBudgetBytes = MinimumAdaptiveMemoryBudgetBytes;
    private const int DefaultMemoryBudgetDivisor = 4;

    private static readonly Lazy<BoundedConcurrencyHostBudgetSnapshot> CurrentBudget = new(
        static () => CreateSnapshot(
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            Environment.GetEnvironmentVariable),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<bool> CurrentReadWorkerMode = new(
        static () => ParseReadWorkerMode(Environment.GetEnvironmentVariable),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static BoundedConcurrencyHostBudgetSnapshot Current => CurrentBudget.Value;

    /// <summary>
    /// True only in isolated native read sidecars. These processes may consume
    /// persistent caches but must never publish, prune, clear, touch, or repair them.
    /// Unknown nonempty values fail closed into read-worker mode.
    /// </summary>
    public static bool IsReadWorker => CurrentReadWorkerMode.Value;

    internal static bool ParseReadWorkerMode(Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        var value = readEnvironmentVariable(ReadWorkerEnvironmentVariable);
        return value switch
        {
            null or "" or "0" => false,
            "1" => true,
            _ => true,
        };
    }

    internal static BoundedConcurrencyHostBudgetSnapshot CreateSnapshot(
        int processorCount,
        long detectedAvailableMemoryBytes,
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        var detectedCpuLimit = Math.Clamp(processorCount, 1, MaximumCpuLimit);
        var detectedMemoryBudget = detectedAvailableMemoryBytes > 0
            ? Math.Max(
                Math.Min(MinimumAdaptiveMemoryBudgetBytes, detectedAvailableMemoryBytes),
                detectedAvailableMemoryBytes / DefaultMemoryBudgetDivisor)
            : UnknownMemoryBudgetBytes;

        var invalidOverrideIgnored = false;
        var cpuLimit = detectedCpuLimit;
        var cpuOverrideApplied = false;
        var cpuOverride = readEnvironmentVariable(CpuLimitEnvironmentVariable);
        if (!string.IsNullOrEmpty(cpuOverride))
        {
            if (int.TryParse(cpuOverride, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCpuLimit)
                && parsedCpuLimit >= 1
                && parsedCpuLimit <= detectedCpuLimit)
            {
                cpuLimit = parsedCpuLimit;
                cpuOverrideApplied = true;
            }
            else
            {
                invalidOverrideIgnored = true;
            }
        }

        var memoryBytes = detectedMemoryBudget;
        var memoryOverrideApplied = false;
        var memoryOverride = readEnvironmentVariable(MemoryBytesEnvironmentVariable);
        if (!string.IsNullOrEmpty(memoryOverride))
        {
            if (long.TryParse(
                    memoryOverride,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedMemoryBytes)
                && parsedMemoryBytes >= MinimumMemoryOverrideBytes
                && (detectedAvailableMemoryBytes <= 0 || parsedMemoryBytes <= detectedAvailableMemoryBytes))
            {
                memoryBytes = parsedMemoryBytes;
                memoryOverrideApplied = true;
            }
            else
            {
                invalidOverrideIgnored = true;
            }
        }

        return new BoundedConcurrencyHostBudgetSnapshot(
            cpuLimit,
            memoryBytes,
            detectedAvailableMemoryBytes,
            cpuOverrideApplied,
            memoryOverrideApplied,
            invalidOverrideIgnored);
    }
}
