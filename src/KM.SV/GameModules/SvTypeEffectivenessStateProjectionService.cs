// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.TypeChart;
using KM.SV.Workflows;

namespace KM.SV.GameModules;

public sealed class SvTypeEffectivenessStateProjectionService
{
    private const int MaximumSourceBytes = 64 * 1024 * 1024;
    private const int MaximumDecompressedSegmentBytes = 64 * 1024 * 1024;
    private const long MaximumAggregateDecompressedBytes = 128L * 1024L * 1024L;
    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";
    private const string SourceIdentity = SvTypeChartWorkflowService.ExeFsMainPath;

    public SvTypeEffectivenessStateProjection LoadFreshBounded(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            throw new SvTypeEffectivenessUnsupportedSourceException(
                "Type-effectiveness state inspection requires a Scarlet or Violet project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            || string.IsNullOrWhiteSpace(paths.BaseExeFsPath)
            || !Directory.Exists(paths.BaseRomFsPath))
        {
            throw new InvalidDataException(
                "Type-effectiveness state inspection requires configured base RomFS and ExeFS sources.");
        }

        lock (SvWorkflowFileSource.OutputWriteSyncRoot)
        {
            var initial = CaptureObservation(paths);
            var final = CaptureObservation(paths);
            if (!ObservationsMatch(initial, final))
            {
                throw new SvTypeEffectivenessObservationChangedException();
            }

            return initial.Projection;
        }
    }

    private static CaptureResult CaptureObservation(ProjectPaths paths)
    {
        var resolved = ResolveEffectiveSource(paths);
        var bytes = ReadBounded(resolved.AbsolutePath);
        ValidateBoundedExecutableHeader(bytes, paths.SelectedGame);
        var analysis = SvTypeChartMainPatcher.Analyze(bytes, paths.SelectedGame);
        if (analysis.Kind is SvTypeChartMainKind.UnsupportedBuild
            or SvTypeChartMainKind.GameMismatch)
        {
            throw new SvTypeEffectivenessUnsupportedSourceException(
                "The effective type-effectiveness source is not an exact supported Scarlet/Violet build.");
        }

        if (analysis.Kind is SvTypeChartMainKind.MissingChart
            or SvTypeChartMainKind.AmbiguousChart
            or SvTypeChartMainKind.Conflict
            || analysis.ChartOffset is null
            || analysis.DetectedGame is null)
        {
            throw new InvalidDataException(
                "The effective type-effectiveness source does not contain one exact supported 18 by 18 table.");
        }

        if (analysis.EffectivenessValues.Count != SvTypeChartMainPatcher.ChartLength
            || SvTypeChartMainPatcher.VanillaChartValues.Count
                != SvTypeChartMainPatcher.ChartLength)
        {
            throw new InvalidDataException(
                "The effective type-effectiveness table has an invalid bounded shape.");
        }

        var cells = new SvTypeEffectivenessCell[SvTypeChartMainPatcher.ChartLength];
        var changedCellCount = 0;
        for (var attackTypeId = 0;
             attackTypeId < SvTypeChartMainPatcher.TypeCount;
             attackTypeId++)
        {
            for (var defenseTypeId = 0;
                 defenseTypeId < SvTypeChartMainPatcher.TypeCount;
                 defenseTypeId++)
            {
                var index = checked(
                    attackTypeId * SvTypeChartMainPatcher.TypeCount + defenseTypeId);
                var effectiveness = analysis.EffectivenessValues[index];
                var vanillaEffectiveness = SvTypeChartMainPatcher.VanillaChartValues[index];
                if (effectiveness != vanillaEffectiveness)
                {
                    changedCellCount = checked(changedCellCount + 1);
                }

                cells[index] = new SvTypeEffectivenessCell(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"type-effectiveness:{attackTypeId}:{defenseTypeId}"),
                    attackTypeId,
                    defenseTypeId,
                    effectiveness,
                    vanillaEffectiveness);
            }
        }

        var chartState = analysis.Kind == SvTypeChartMainKind.Modified
            ? SvTypeEffectivenessChartState.Modified
            : SvTypeEffectivenessChartState.Vanilla;
        var projection = new SvTypeEffectivenessStateProjection(
            new SvTypeEffectivenessSource(
                SourceIdentity,
                resolved.SourceLayer,
                resolved.FileState,
                analysis.BuildId,
                analysis.DetectedGame.Value,
                chartState),
            cells,
            changedCellCount);
        return new CaptureResult(projection, CaptureFingerprint(bytes, projection.Source));
    }

    private static byte[] ReadBounded(string absolutePath)
    {
        using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length < 1 || stream.Length > MaximumSourceBytes)
        {
            throw new InvalidDataException(
                "The effective type-effectiveness source exceeds its bounded file-size contract.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException(
                "The effective type-effectiveness source changed size during bounded inspection.");
        }

        return bytes;
    }

    private static void ValidateBoundedExecutableHeader(
        byte[] bytes,
        ProjectGame? selectedGame)
    {
        const uint nsoMagic = 0x304f534e;
        const int headerSize = 0x100;
        if (bytes.Length < headerSize
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x00, sizeof(uint)))
                != nsoMagic)
        {
            throw new InvalidDataException(
                "The effective type-effectiveness source is not an NSO executable.");
        }

        var expectedBuildId = selectedGame switch
        {
            ProjectGame.Scarlet => ScarletBuildId,
            ProjectGame.Violet => VioletBuildId,
            _ => throw new SvTypeEffectivenessUnsupportedSourceException(
                "Type-effectiveness state inspection requires a Scarlet or Violet project."),
        };
        var buildId = Convert.ToHexString(bytes.AsSpan(0x40, 20));
        if (!string.Equals(buildId, expectedBuildId, StringComparison.Ordinal))
        {
            throw new SvTypeEffectivenessUnsupportedSourceException(
                "The effective type-effectiveness source is not an exact supported Scarlet/Violet build.");
        }

        var decompressedSizes = new[]
        {
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x18, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x28, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x38, sizeof(int))),
        };
        if (decompressedSizes.Any(size =>
                size < 0 || size > MaximumDecompressedSegmentBytes)
            || decompressedSizes.Sum(size => (long)size)
                > MaximumAggregateDecompressedBytes)
        {
            throw new InvalidDataException(
                "The effective type-effectiveness source exceeds its bounded decompression contract.");
        }
    }

    private static ResolvedSource ResolveEffectiveSource(ProjectPaths paths)
    {
        var baseMain = ContainedPath(paths.BaseExeFsPath!, "main");
        if (!File.Exists(baseMain))
        {
            throw new FileNotFoundException(
                "The base type-effectiveness source is unavailable.",
                SourceIdentity);
        }

        if (!string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            var outputMain = ContainedPath(paths.OutputRootPath, "exefs", "main");
            if (File.Exists(outputMain))
            {
                return new ResolvedSource(
                    outputMain,
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride);
            }
        }

        return new ResolvedSource(
            baseMain,
            ProjectFileLayer.Base,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static string ContainedPath(string rootPath, params string[] components)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidDataException(
                "The type-effectiveness source root is unavailable.");
        }

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine([root, .. components]));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(root, candidate)))
        {
            throw new InvalidDataException(
                "The type-effectiveness source escaped its configured project root.");
        }

        return candidate;
    }

    private static string CaptureFingerprint(
        byte[] bytes,
        SvTypeEffectivenessSource source)
    {
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintText(fingerprint, source.SourceIdentity);
        AppendFingerprintText(
            fingerprint,
            ((int)source.SourceLayer).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(
            fingerprint,
            ((int)source.FileState).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(fingerprint, source.BuildId);
        AppendFingerprintText(
            fingerprint,
            ((int)source.Game).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(
            fingerprint,
            bytes.Length.ToString(CultureInfo.InvariantCulture));
        fingerprint.AppendData(bytes);
        return Convert.ToHexStringLower(fingerprint.GetHashAndReset());
    }

    private static void AppendFingerprintText(
        IncrementalHash fingerprint,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        fingerprint.AppendData(length);
        fingerprint.AppendData(bytes);
    }

    private static bool ObservationsMatch(CaptureResult left, CaptureResult right)
    {
        return string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.Ordinal)
            && left.Projection.Source == right.Projection.Source
            && left.Projection.ChangedCellCount == right.Projection.ChangedCellCount
            && left.Projection.Cells.SequenceEqual(right.Projection.Cells);
    }

    private sealed record CaptureResult(
        SvTypeEffectivenessStateProjection Projection,
        string Fingerprint);

    private sealed record ResolvedSource(
        string AbsolutePath,
        ProjectFileLayer SourceLayer,
        ProjectFileGraphEntryState FileState);
}
