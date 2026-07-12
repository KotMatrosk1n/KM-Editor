// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;

namespace KM.Formats.SV;

public sealed class SvCompressionRuntimeLibrary : IDisposable
{
    private readonly NativeLibrarySafeHandle libraryHandle;
    private readonly RuntimeDecompress decompress;
    private bool disposed;

    private SvCompressionRuntimeLibrary(NativeLibrarySafeHandle libraryHandle, RuntimeDecompress decompress)
    {
        this.libraryHandle = libraryHandle;
        this.decompress = decompress;
    }

    public static SvCompressionRuntimeLibrary LoadFromFolder(string? supportFolderPath)
    {
        var libraryPath = SvCompressionRuntime.ResolveRequiredFilePath(supportFolderPath);
        var rawLibraryHandle = NativeLibrary.Load(libraryPath);
        NativeLibrarySafeHandle? libraryHandle = null;

        try
        {
            libraryHandle = new NativeLibrarySafeHandle(rawLibraryHandle);
            var export = NativeLibrary.GetExport(libraryHandle.DangerousGetHandle(), string.Concat("Oodle", "LZ_Decompress"));
            var decompress = Marshal.GetDelegateForFunctionPointer<RuntimeDecompress>(export);
            return new SvCompressionRuntimeLibrary(libraryHandle, decompress);
        }
        catch
        {
            if (libraryHandle is null)
            {
                NativeLibrary.Free(rawLibraryHandle);
            }
            else
            {
                libraryHandle.Dispose();
            }

            throw;
        }
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressedData, int decompressedSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decompressedSize);

        nint input = 0;
        nint output = 0;
        var libraryReferenceAdded = false;

        try
        {
            libraryHandle.DangerousAddRef(ref libraryReferenceAdded);
            input = Marshal.AllocHGlobal(compressedData.Length);
            output = Marshal.AllocHGlobal(decompressedSize);
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
            if (input != 0)
            {
                Marshal.FreeHGlobal(input);
            }

            if (output != 0)
            {
                Marshal.FreeHGlobal(output);
            }

            if (libraryReferenceAdded)
            {
                libraryHandle.DangerousRelease();
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        libraryHandle.Dispose();
        disposed = true;
    }

    private sealed class NativeLibrarySafeHandle : SafeHandle
    {
        public NativeLibrarySafeHandle(nint libraryHandle)
            : base(nint.Zero, ownsHandle: true)
        {
            SetHandle(libraryHandle);
        }

        public override bool IsInvalid => handle == nint.Zero;

        protected override bool ReleaseHandle()
        {
            NativeLibrary.Free(handle);
            return true;
        }
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
