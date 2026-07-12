// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Tools.Bridge;

public sealed class BridgeLineRunner
{
    private readonly ProjectBridgeDispatcher dispatcher;

    public BridgeLineRunner(ProjectBridgeDispatcher? dispatcher = null)
    {
        this.dispatcher = dispatcher ?? new ProjectBridgeDispatcher();
    }

    public async Task<int> RunOnceAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var requestJson = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var responseJson = dispatcher.Dispatch(requestJson ?? string.Empty);

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
            var responseJson = dispatcher.Dispatch(requestJson);
            await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }
}

