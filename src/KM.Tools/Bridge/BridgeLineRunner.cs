// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace KM.Tools.Bridge;

public sealed class BridgeLineRunner : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Preamble = [0xef, 0xbb, 0xbf];

    private readonly Func<ProjectBridgeDispatcher> dispatcherFactory;
    private ProjectBridgeDispatcher? dispatcher;
    private int disposed;

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        return await RunOnceCoreAsync(
            new BoundedBridgeTextLineReader(input),
            output,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunOnceAsync(Stream input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        return await RunOnceCoreAsync(
            new BoundedBridgeStreamLineReader(input),
            output,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        return await RunCoreAsync(
            new BoundedBridgeTextLineReader(input),
            output,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunAsync(Stream input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        return await RunCoreAsync(
            new BoundedBridgeStreamLineReader(input),
            output,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunOnceCoreAsync(
        IBoundedBridgeLineReader lineReader,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var responseJson = DispatchRequest(
                request ?? new BoundedBridgeLine(
                    Text: string.Empty,
                    IsTooLarge: false,
                    HasInvalidEncoding: false));

            await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            return 0;
        }
        finally
        {
            Dispose();
        }
    }

    private async Task<int> RunCoreAsync(
        IBoundedBridgeLineReader lineReader,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } request)
            {
                var responseJson = DispatchRequest(request);
                await output.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var retired = Interlocked.Exchange(ref dispatcher, null);
        retired?.Dispose();
    }

    private string DispatchRequest(BoundedBridgeLine request)
    {
        if (request.IsTooLarge)
        {
            return ProjectBridgeDispatcher.SerializeRequestTooLargeFailure();
        }

        return request.HasInvalidEncoding
            ? ProjectBridgeDispatcher.SerializeInvalidRequestEncodingFailure()
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
            var retired = dispatcher;
            dispatcher = null;
            retired?.Dispose();
        }

        return Encoding.UTF8.GetByteCount(result.ResponseJson)
                <= ProjectBridgeDispatcher.MaximumBridgeResponseBytes
            ? result.ResponseJson
            : ProjectBridgeDispatcher.SerializeResponseTooLargeFailure(requestId: null);
    }

    private static ProjectBridgeDispatcher CreateDispatcher()
    {
        return new ProjectBridgeDispatcher();
    }

    private sealed record BoundedBridgeLine(
        string? Text,
        bool IsTooLarge,
        bool HasInvalidEncoding);

    private interface IBoundedBridgeLineReader
    {
        ValueTask<BoundedBridgeLine?> ReadLineAsync(CancellationToken cancellationToken);
    }

    private sealed class BoundedBridgeTextLineReader : IBoundedBridgeLineReader
    {
        private const int InitialCapacity = 8 * 1024;

        private readonly TextReader input;
        private readonly char[] buffer = new char[1];
        private int bufferOffset;
        private int bufferLength;
        private bool skipLeadingLineFeed;

        public BoundedBridgeTextLineReader(TextReader input)
        {
            this.input = input;
        }

        public async ValueTask<BoundedBridgeLine?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new StringBuilder(
                Math.Min(InitialCapacity, ProjectBridgeDispatcher.MaximumBridgeRequestCharacters));
            var hasCharacters = false;
            var isTooLarge = false;

            while (true)
            {
                if (bufferOffset >= bufferLength)
                {
                    // A StreamReader bulk read can copy a complete short tail (including the line
                    // feed) into its destination and then block trying to fill the requested count.
                    // Request one character on the compatibility TextReader path so framing always
                    // observes a terminator before another underlying read. The production console
                    // path uses the chunked raw-byte reader below.
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
            return new BoundedBridgeLine(
                isTooLarge ? null : line.ToString(),
                isTooLarge,
                HasInvalidEncoding: false);
        }
    }

    private sealed class BoundedBridgeStreamLineReader : IBoundedBridgeLineReader
    {
        private const int BufferSize = 8 * 1024;

        private readonly Stream input;
        private readonly byte[] buffer = new byte[BufferSize];
        private int bufferOffset;
        private int bufferLength;
        private bool isFirstLine = true;
        private bool skipLeadingLineFeed;

        public BoundedBridgeStreamLineReader(Stream input)
        {
            this.input = input;
        }

        public async ValueTask<BoundedBridgeLine?> ReadLineAsync(CancellationToken cancellationToken)
        {
            using var line = new MemoryStream(
                Math.Min(BufferSize, ProjectBridgeDispatcher.MaximumBridgeRequestBytes));
            var hasBytes = false;
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
                        return hasBytes || isTooLarge
                            ? CreateResult(line, isTooLarge)
                            : null;
                    }
                }

                if (skipLeadingLineFeed)
                {
                    skipLeadingLineFeed = false;
                    if (buffer[bufferOffset] == (byte)'\n')
                    {
                        bufferOffset++;
                        if (bufferOffset >= bufferLength)
                        {
                            continue;
                        }
                    }
                }

                var segmentStart = bufferOffset;
                while (bufferOffset < bufferLength)
                {
                    var value = buffer[bufferOffset++];
                    if (value is not ((byte)'\r' or (byte)'\n'))
                    {
                        continue;
                    }

                    AppendSegment(
                        line,
                        buffer.AsSpan(segmentStart, bufferOffset - segmentStart - 1),
                        ref hasBytes,
                        ref isTooLarge);
                    if (value == (byte)'\r')
                    {
                        if (bufferOffset < bufferLength)
                        {
                            if (buffer[bufferOffset] == (byte)'\n')
                            {
                                bufferOffset++;
                            }
                        }
                        else
                        {
                            skipLeadingLineFeed = true;
                        }
                    }

                    return CreateResult(line, isTooLarge);
                }

                AppendSegment(
                    line,
                    buffer.AsSpan(segmentStart, bufferOffset - segmentStart),
                    ref hasBytes,
                    ref isTooLarge);
            }
        }

        private static void AppendSegment(
            MemoryStream line,
            ReadOnlySpan<byte> segment,
            ref bool hasBytes,
            ref bool isTooLarge)
        {
            if (segment.IsEmpty)
            {
                return;
            }

            hasBytes = true;
            if (isTooLarge)
            {
                return;
            }

            var remaining = ProjectBridgeDispatcher.MaximumBridgeRequestBytes - checked((int)line.Length);
            if (segment.Length <= remaining)
            {
                line.Write(segment);
                return;
            }

            if (remaining > 0)
            {
                line.Write(segment[..remaining]);
            }

            isTooLarge = true;
        }

        private BoundedBridgeLine CreateResult(MemoryStream line, bool isTooLarge)
        {
            var stripPreamble = isFirstLine;
            isFirstLine = false;
            if (isTooLarge)
            {
                return new BoundedBridgeLine(
                    Text: null,
                    IsTooLarge: true,
                    HasInvalidEncoding: false);
            }

            var bytes = line.GetBuffer().AsSpan(0, checked((int)line.Length));
            if (stripPreamble && bytes.StartsWith(Utf8Preamble))
            {
                bytes = bytes[Utf8Preamble.Length..];
            }

            try
            {
                return new BoundedBridgeLine(
                    StrictUtf8.GetString(bytes),
                    IsTooLarge: false,
                    HasInvalidEncoding: false);
            }
            catch (DecoderFallbackException)
            {
                return new BoundedBridgeLine(
                    Text: null,
                    IsTooLarge: false,
                    HasInvalidEncoding: true);
            }
        }
    }
}

