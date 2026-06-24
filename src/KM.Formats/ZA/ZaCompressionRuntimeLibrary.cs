// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;

namespace KM.Formats.ZA;

public sealed class ZaCompressionRuntimeLibrary : IDisposable
{
    private readonly nint libraryHandle;
    private readonly RuntimeDecompress decompress;
    private bool disposed;

    private ZaCompressionRuntimeLibrary(nint libraryHandle, RuntimeDecompress decompress)
    {
        this.libraryHandle = libraryHandle;
        this.decompress = decompress;
    }

    public static ZaCompressionRuntimeLibrary LoadFromFolder(string? supportFolderPath)
    {
        var libraryPath = ZaCompressionRuntime.ResolveRequiredFilePath(supportFolderPath);
        var libraryHandle = NativeLibrary.Load(libraryPath);

        try
        {
            var export = NativeLibrary.GetExport(libraryHandle, string.Concat("Oodle", "LZ_Decompress"));
            var decompress = Marshal.GetDelegateForFunctionPointer<RuntimeDecompress>(export);
            return new ZaCompressionRuntimeLibrary(libraryHandle, decompress);
        }
        catch
        {
            NativeLibrary.Free(libraryHandle);
            throw;
        }
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressedData, int decompressedSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decompressedSize);

        var input = Marshal.AllocHGlobal(compressedData.Length);
        var output = Marshal.AllocHGlobal(decompressedSize);

        try
        {
            Marshal.Copy(compressedData.ToArray(), 0, input, compressedData.Length);
            var written = decompress(
                input,
                compressedData.Length,
                output,
                decompressedSize,
                fuzzSafe: 0,
                checkCrc: 0,
                verbosity: 0,
                decBufBase: nint.Zero,
                decBufSize: 0,
                fpCallback: nint.Zero,
                callbackUserData: nint.Zero,
                decoderMemory: nint.Zero,
                decoderMemorySize: 0,
                threadPhase: 0);

            if (written != decompressedSize)
            {
                throw new InvalidDataException(
                    $"Compressed data expanded to {written} bytes, but {decompressedSize} bytes were expected.");
            }

            var result = new byte[decompressedSize];
            Marshal.Copy(output, result, 0, decompressedSize);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            Marshal.FreeHGlobal(output);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        NativeLibrary.Free(libraryHandle);
        disposed = true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long RuntimeDecompress(
        nint buffer,
        long bufferSize,
        nint outputBuffer,
        long outputBufferSize,
        int fuzzSafe,
        int checkCrc,
        int verbosity,
        nint decBufBase,
        long decBufSize,
        nint fpCallback,
        nint callbackUserData,
        nint decoderMemory,
        long decoderMemorySize,
        int threadPhase);
}
