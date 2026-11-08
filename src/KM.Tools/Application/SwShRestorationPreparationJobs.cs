// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Restoration;
using KM.Core.Concurrency;
using KM.Core.Projects;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed class SwShRestorationPreparationJobs : IDisposable
{
    private static readonly BoundedConcurrencyPolicy Policy = new("Pokemon restoration preparation", BoundedWorkloadKind.Decode,
        768L * 1024 * 1024, maximumDegreeOfParallelism: 1);
    private readonly object gate = new();
    private readonly SwShRestorationDonorCache donors = new();
    private Job? current;
    private readonly Dictionary<(ProjectPaths Paths, string Definition), (FileStream Lease, string? RemovalId)> preparedLeases = [];
    private bool disposed;
    private sealed class Job(ProjectPaths paths, string definition, string? removalId) : IProgress<SwShRestorationPreparationProgress>
    {
        internal readonly ProjectPaths Paths = paths;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly System.Diagnostics.Stopwatch Timer = System.Diagnostics.Stopwatch.StartNew();
        internal RestorationPreparationDto Status = new(Guid.NewGuid().ToString("N"), definition, "preparing", "sources", 0,
            SwShRestorationDefinitions.Get(definition).Identities.Count, null, new Dictionary<int, int[]>(), []) { RemovalId = removalId };
        private readonly Dictionary<string, Queue<string>> recentByPhase = new(StringComparer.Ordinal);
        internal FileStream? Lease;
        public void Report(SwShRestorationPreparationProgress value)
        {
            lock (this)
            {
                var recent = Status.RecentResources;
                if (value.CurrentResource is { } path)
                {
                    var phase = value.Activity == "textures" ? "textures" : value.Phase;
                    if (!recentByPhase.TryGetValue(phase, out var resources))
                        recentByPhase.Add(phase, resources = new Queue<string>());
                    if (!resources.Contains(path))
                    {
                        if (resources.Count == 6) resources.Dequeue();
                        resources.Enqueue(path);
                        recent = recentByPhase.Values.SelectMany(group => group).Distinct(StringComparer.Ordinal).TakeLast(24).ToArray();
                    }
                }
                Status = Status with { Phase = value.Phase, CompletedSpecies = value.CompletedSpecies, TotalSpecies = value.TotalSpecies,
                    Activity = value.Activity, Species = value.Species ?? Status.Species, CompletedUnits = value.CompletedUnits,
                    TotalUnits = value.TotalUnits, CurrentResource = value.CurrentResource, RecentResources = recent };
            }
        }
    }
    public RestorationDonorResponse InspectDonor(string donor, string root, string? support)
    {
        if (!Enum.TryParse<SwShRestorationDonor>(donor, out var game) || !Enum.IsDefined(game)) throw new InvalidDataException("The donor game is not supported.");
        using var lease = donors.Acquire(game, root, support);
        return new(lease.Session.Mode);
    }
    public RestorationPreparationDto Start(ProjectPaths paths, string definition, string donor, string? support)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is not null && Read(current).Status == "preparing") throw new InvalidDataException("A restoration preparation is already running. Wait for it or cancel it.");
            if (current is { Lease: { } lease } completed)
            {
                var key = (completed.Paths, completed.Status.DefinitionId);
                if (preparedLeases.Remove(key, out var previous)) previous.Lease.Dispose();
                if (preparedLeases.Count >= 256)
                {
                    var oldest = preparedLeases.First(); preparedLeases.Remove(oldest.Key); oldest.Value.Lease.Dispose();
                }
                preparedLeases.Add(key, (lease, completed.Status.RemovalId)); completed.Lease = null;
            }
            current?.Cancellation.Dispose();
            var job = current = new(paths, definition, SwShRestorationStateStore.Read(paths).State.Selections.SingleOrDefault(s => s.DefinitionId == definition)?.RemovalId);
            _ = Run(job, donor, support);
            return Read(job);
        }
    }
    public IReadOnlySet<string> ActivePreparedHashes(ProjectPaths paths, string? definition = null)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(paths.OutputRootPath)) return new HashSet<string>();
            var output = Path.GetFullPath(paths.OutputRootPath!).TrimEnd(Path.DirectorySeparatorChar);
            bool Same(ProjectPaths other) => other.SelectedGame == paths.SelectedGame && string.Equals(output,
                Path.GetFullPath(other.OutputRootPath!).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            var hashes = preparedLeases.Where(pair => Same(pair.Key.Paths) && (definition is null || pair.Key.Definition == definition)).Select(pair => Path.GetFileNameWithoutExtension(pair.Value.Lease.Name)).ToHashSet(StringComparer.Ordinal);
            if (current is { Lease: { } lease } job && Same(job.Paths) && (definition is null || job.Status.DefinitionId == definition)) hashes.Add(Path.GetFileNameWithoutExtension(lease.Name));
            return hashes;
        }
    }
    public void ReleaseRemoved(ProjectPaths paths, string definition, string removalId)
    {
        lock (gate)
        {
            bool SameOutput(ProjectPaths other) => other.SelectedGame == paths.SelectedGame
                && string.Equals(Path.GetFullPath(other.OutputRootPath!).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(paths.OutputRootPath!).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            foreach (var key in preparedLeases.Keys.Where(k => k.Definition == definition && SameOutput(k.Paths)).ToArray())
                if (preparedLeases[key].RemovalId != removalId) { preparedLeases[key].Lease.Dispose(); preparedLeases.Remove(key); }
            if (current is { } job && job.Status.DefinitionId == definition && SameOutput(job.Paths) && job.Status.RemovalId != removalId)
            {
                if (Read(job).Status == "preparing") job.Cancellation.Cancel();
                else { job.Lease?.Dispose(); job.Lease = null; lock (job) job.Status = job.Status with { Status = "canceled", PreparedHash = null }; }
            }
        }
    }
    public RestorationPreparationDto Get(ProjectPaths paths, string id, bool cancel = false)
    {
        lock (gate)
        {
            if (current is null || current.Paths != paths || current.Status.Id != id) throw new InvalidDataException("Restoration preparation is no longer available for this project.");
            if (cancel) current.Cancellation.Cancel();
            return Read(current);
        }
    }
    private static RestorationPreparationDto Read(Job job) { lock (job) return job.Status with {
        ElapsedMilliseconds = job.Timer.ElapsedMilliseconds, CancellationRequested = job.Cancellation.IsCancellationRequested }; }
    private async Task Run(Job job, string donor, string? support)
    {
        try
        {
            await BoundedParallel.RunAsync(Policy, token =>
            {
                using var source = donors.Acquire(SwShRestorationDefinitions.Get(job.Status.DefinitionId).Donor, donor, support);
                lock (job) job.Status = job.Status with { CacheMode = source.Session.Mode };
                var prepared = new SwShRestorationPreparationService().Prepare(job.Paths, donor, support, job.Status.DefinitionId, job, token, source.Session);
                token.ThrowIfCancellationRequested();
                job.Report(new("saving", job.Status.TotalSpecies, job.Status.TotalSpecies));
                var stored = SwShRestorationStateStore.Store(job.Paths, prepared, token);
                lock (job)
                {
                    job.Lease = stored.Lease;
                    job.Status = job.Status with { Status = "prepared", Phase = "complete", PreparedHash = stored.Hash, UnavailableSourceMoves = prepared.UnavailableSourceMoves };
                }
            }, job.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var cause = exception is BoundedWorkItemException { InnerException: { } inner } ? inner : exception;
            var canceled = job.Cancellation.IsCancellationRequested || cause is OperationCanceledException;
            lock (job) job.Status = job.Status with { Status = canceled ? "canceled" : "failed",
                Diagnostics = canceled ? [] : [new(ApiDiagnosticSeverity.Error, cause.Message, Domain: SwShRestorationDefinitions.Domain) { Code = SwShRestorationService.SourceCode }] };
        }
        finally { job.Timer.Stop(); lock (gate) { if (disposed) job.Lease?.Dispose(); } }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; current?.Cancellation.Cancel(); current?.Lease?.Dispose();
            foreach (var lease in preparedLeases.Values) lease.Lease.Dispose(); preparedLeases.Clear();
            donors.Dispose();
        }
    }
}
