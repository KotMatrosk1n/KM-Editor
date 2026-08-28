// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Formats.Executable;

var verifyOnly = args.Length == 3
    && string.Equals(args[0], "--verify", StringComparison.Ordinal);
if ((!verifyOnly && args.Length != 2)
    || (args.Length > 0
        && args[0].StartsWith("--", StringComparison.Ordinal)
        && !verifyOnly))
{
    Console.Error.WriteLine(
        "Usage: KM.NativeSettings.Pack <input.elf> <output.nso>\n" +
        "       KM.NativeSettings.Pack --verify <input.elf> <input.nso>");
    return 2;
}

var inputPath = Path.GetFullPath(args[verifyOnly ? 1 : 0]);
var outputPath = Path.GetFullPath(args[verifyOnly ? 2 : 1]);
if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("The ELF input and NSO output paths must be different.");
    return 3;
}

var elf = ReadStableBoundedFile(
    inputPath,
    Elf64NsoBuilder.MaximumElfBytes,
    "native runtime ELF");

if (verifyOnly)
{
    var existingNso = ReadStableBoundedFile(
        outputPath,
        Elf64NsoBuilder.MaximumNsoBytes,
        "native runtime NSO");
    Elf64NsoBuilder.Verify(elf, existingNso);
    Console.WriteLine(
        $"Verified {existingNso.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(existingNso))}.");
    return 0;
}

var nso = Elf64NsoBuilder.Build(elf);
var outputDirectory = Path.GetDirectoryName(outputPath)
    ?? throw new InvalidOperationException("The NSO output has no parent directory.");
Directory.CreateDirectory(outputDirectory);
var temporaryPath = Path.Combine(
    outputDirectory,
    $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
try
{
    using (var stream = new FileStream(
               temporaryPath,
               FileMode.CreateNew,
               FileAccess.Write,
               FileShare.None,
               bufferSize: 64 * 1024,
               FileOptions.SequentialScan))
    {
        stream.Write(nso);
        stream.Flush(flushToDisk: true);
    }

    var writtenNso = ReadStableBoundedFile(
        temporaryPath,
        Elf64NsoBuilder.MaximumNsoBytes,
        "temporary native runtime NSO");
    Elf64NsoBuilder.Verify(elf, writtenNso);
    if (!writtenNso.AsSpan().SequenceEqual(nso))
    {
        throw new InvalidDataException(
            "The native runtime NSO changed between generation and disk readback.");
    }

    File.Move(temporaryPath, outputPath, overwrite: true);
    var finalNso = ReadStableBoundedFile(
        outputPath,
        Elf64NsoBuilder.MaximumNsoBytes,
        "final native runtime NSO");
    Elf64NsoBuilder.Verify(elf, finalNso);
    if (!finalNso.AsSpan().SequenceEqual(nso))
    {
        throw new InvalidDataException(
            "The native runtime NSO changed during final publication.");
    }
}
finally
{
    try
    {
        File.Delete(temporaryPath);
    }
    catch (IOException)
    {
        // Preserve the primary build failure. A same-directory orphan is safe
        // and never embedded because it does not have the canonical name.
    }
    catch (UnauthorizedAccessException)
    {
        // Preserve the primary build failure.
    }
}

Console.WriteLine(
    $"Packed and verified {nso.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(nso))}.");
return 0;

static byte[] ReadStableBoundedFile(string path, int maximumBytes, string label)
{
    var input = new FileInfo(path);
    input.Refresh();
    if (!input.Exists
        || !string.IsNullOrEmpty(input.LinkTarget)
        || input.Attributes.HasFlag(FileAttributes.Directory)
        || input.Length is < 1
        || input.Length > maximumBytes)
    {
        throw new InvalidDataException($"The {label} is missing, linked, empty, or oversized.");
    }

    using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 64 * 1024,
        FileOptions.SequentialScan);
    if (stream.Length is < 1 || stream.Length > maximumBytes)
    {
        throw new InvalidDataException($"The {label} changed before it was read.");
    }

    var bytes = new byte[checked((int)stream.Length)];
    stream.ReadExactly(bytes);
    if (stream.Position != stream.Length)
    {
        throw new InvalidDataException($"The {label} changed while it was read.");
    }

    return bytes;
}
