// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Tools.Bridge;

public sealed class BridgeLineRunner
{
    private readonly Func<ProjectBridgeDispatcher> dispatcherFactory;
    private ProjectBridgeDispatcher? dispatcher;

    public BridgeLineRunner(
        ProjectBridgeDispatcher? dispatcher = null,
        Func<ProjectBridgeDispatcher>? dispatcherFactory = null)
    {
        this.dispatcherFactory = dispatcherFactory ?? CreateDispatcher;
        this.dispatcher = dispatcher ?? this.dispatcherFactory();
    }

    public async Task<int> RunOnceAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var requestJson = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var responseJson = DispatchRequest(requestJson ?? string.Empty);

        await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);

        return 0;
    }

    public async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } requestJson)
        {
            var responseJson = DispatchRequest(requestJson);
            await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private string DispatchRequest(string requestJson)
    {
        dispatcher ??= dispatcherFactory();
        var result = dispatcher.DispatchForLongLivedRunner(requestJson);
        if (result.RequiresDispatcherReset)
        {
            // Unexpected command failures may occur after a mutable workflow or edit session has
            // changed. Retire the complete dispatcher before another request can observe that state.
            dispatcher = null;
        }

        return result.ResponseJson;
    }

    private static ProjectBridgeDispatcher CreateDispatcher()
    {
        return new ProjectBridgeDispatcher();
    }
}

