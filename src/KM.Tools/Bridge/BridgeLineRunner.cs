// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

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

        var lineReader = new BoundedBridgeLineReader(input);
        var request = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var responseJson = DispatchRequest(request ?? new BoundedBridgeLine(string.Empty, IsTooLarge: false));

        await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);

        return 0;
    }

    public async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var lineReader = new BoundedBridgeLineReader(input);
        while (await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } request)
        {
            var responseJson = DispatchRequest(request);
            await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private string DispatchRequest(BoundedBridgeLine request)
    {
        return request.IsTooLarge
            ? ProjectBridgeDispatcher.SerializeRequestTooLargeFailure()
            : DispatchRequest(request.Text ?? string.Empty);
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

    private sealed record BoundedBridgeLine(string? Text, bool IsTooLarge);

    private sealed class BoundedBridgeLineReader
    {
        private const int BufferSize = 8 * 1024;

        private readonly TextReader input;
        private readonly char[] buffer = new char[BufferSize];
        private int bufferOffset;
        private int bufferLength;
        private bool skipLeadingLineFeed;

        public BoundedBridgeLineReader(TextReader input)
        {
            this.input = input;
        }

        public async ValueTask<BoundedBridgeLine?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new StringBuilder(
                Math.Min(BufferSize, ProjectBridgeDispatcher.MaximumBridgeRequestCharacters));
            var hasCharacters = false;
            var isTooLarge = false;

            while (true)
            {
                if (bufferOffset >= bufferLength)
                {
                    bufferLength = await input
                        .ReadAsync(buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    bufferOffset = 0;
                    if (bufferLength == 0)
                    {
                        return hasCharacters || isTooLarge
                            ? CreateResult(line, isTooLarge)
                            : null;
                    }
                }

                if (skipLeadingLineFeed)
                {
                    skipLeadingLineFeed = false;
                    if (buffer[bufferOffset] == '\n')
                    {
                        bufferOffset++;
                        if (bufferOffset >= bufferLength)
                        {
                            continue;
                        }
                    }
                }

                while (bufferOffset < bufferLength)
                {
                    var character = buffer[bufferOffset++];
                    if (character == '\n')
                    {
                        return CreateResult(line, isTooLarge);
                    }

                    if (character == '\r')
                    {
                        if (bufferOffset < bufferLength)
                        {
                            if (buffer[bufferOffset] == '\n')
                            {
                                bufferOffset++;
                            }
                        }
                        else
                        {
                            skipLeadingLineFeed = true;
                        }

                        return CreateResult(line, isTooLarge);
                    }

                    hasCharacters = true;
                    if (isTooLarge)
                    {
                        continue;
                    }

                    if (line.Length >= ProjectBridgeDispatcher.MaximumBridgeRequestCharacters)
                    {
                        isTooLarge = true;
                        continue;
                    }

                    line.Append(character);
                }
            }
        }

        private static BoundedBridgeLine CreateResult(StringBuilder line, bool isTooLarge)
        {
            return new BoundedBridgeLine(isTooLarge ? null : line.ToString(), isTooLarge);
        }
    }
}

