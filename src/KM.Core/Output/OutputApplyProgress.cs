// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Output;

public sealed record OutputApplyProgress(string Phase, int Completed, int Total, string? CurrentPath)
{
    private static readonly AsyncLocal<Action<OutputApplyProgress>?> Current = new();

    public static IDisposable Observe(Action<OutputApplyProgress> report)
    {
        var previous = Current.Value;
        Current.Value = report;
        return new Observation(previous);
    }

    internal static void Report(string phase, int completed = 0, int total = 0, string? path = null)
    {
        Current.Value?.Invoke(new(phase, completed, total, path));
    }

    private sealed class Observation(Action<OutputApplyProgress>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
