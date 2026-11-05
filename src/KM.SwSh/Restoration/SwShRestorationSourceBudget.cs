// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.Restoration;

internal static class SwShRestorationSourceBudget
{
    // Common.bnk aggregates every installed voice, unlike an individual model
    // resource. Keep it within the coordinator's default per-file write limit.
    // All other sources retain their existing 64 MiB read ceiling.
    internal static int MaximumBytes(string relativePath) => relativePath == SwShRestorationComposer.VoicesPath
        ? 128 * 1024 * 1024 : 64 * 1024 * 1024;
}
