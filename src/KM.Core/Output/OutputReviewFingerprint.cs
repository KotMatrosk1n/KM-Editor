// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Editing;
using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>
/// Produces framed SHA-256 review fingerprints without retaining file content.
/// Collection order is significant.
/// </summary>
public static class OutputReviewFingerprint
{
    private const int MaximumFramedTextBytes = 1_048_576;
    private const int MaximumSourcesPerWrite = 4_096;

    public static string FromChangePlan(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Writes is null
            || plan.Writes.Count > OutputLimits.MaximumMutationsPerApply)
        {
            throw new ArgumentException("The change plan write collection is invalid or out of bounds.", nameof(plan));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var budget = new FramingBudget(MaximumFramedTextBytes);
        AppendText(hash, budget, "km-output-change-plan-v1");
        AppendText(hash, budget, plan.SessionId.Value);
        AppendInt32(hash, plan.Writes.Count);
        foreach (var write in plan.Writes)
        {
            if (write is null || write.Sources is null || write.Sources.Count > MaximumSourcesPerWrite)
            {
                throw new ArgumentException("A change plan write is invalid or out of bounds.", nameof(plan));
            }

            var target = new RelativeOutputPath(write.TargetRelativePath);
            AppendText(hash, budget, target.CanonicalKey);
            AppendBoolean(hash, write.ReplacesExistingOutput);
            AppendText(hash, budget, ValidateText(write.Reason, 8_192, nameof(plan)));
            AppendNullableText(hash, budget, ValidateOptionalText(write.SourceFingerprint, 1_024, nameof(plan)));
            AppendInt32(hash, write.Sources.Count);
            foreach (var source in write.Sources)
            {
                if (source is null || !Enum.IsDefined(source.Layer))
                {
                    throw new ArgumentException("A change plan source is invalid.", nameof(plan));
                }

                AppendInt32(hash, (int)source.Layer);
                AppendText(hash, budget, ValidateText(source.RelativePath, 4_096, nameof(plan)));
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string FromMutations(IEnumerable<OutputMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        var builder = new List<OutputMutation>();
        foreach (var mutation in mutations)
        {
            if (builder.Count == OutputLimits.MaximumMutationsPerApply)
            {
                throw new ArgumentException("The mutation collection is empty or out of bounds.", nameof(mutations));
            }

            builder.Add(mutation);
        }

        var materialized = builder.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("The mutation collection is empty or out of bounds.", nameof(mutations));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var budget = new FramingBudget(MaximumFramedTextBytes);
        AppendText(hash, budget, "km-output-mutations-v2");
        AppendInt32(hash, materialized.Length);
        foreach (var mutation in materialized)
        {
            if (mutation is null)
            {
                throw new ArgumentException("The mutation collection cannot contain null entries.", nameof(mutations));
            }

            AppendInt32(hash, (int)mutation.Kind);
            AppendText(hash, budget, mutation.Path.CanonicalKey);
            AppendState(hash, budget, mutation.ExpectedPreimage);
            AppendState(hash, budget, mutation.PlannedPostimage);
            AppendNullableText(hash, budget, mutation.OwnershipOutputMode);
            AppendNullableBoolean(hash, mutation.RestoredFileDeleteEligibility);
            AppendInt32(hash, mutation.OwnershipClaims.Length);
            foreach (var claim in mutation.OwnershipClaims)
            {
                AppendOwnership(hash, budget, claim);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendOwnership(
        IncrementalHash hash,
        FramingBudget budget,
        OwnedTarget claim)
    {
        AppendInt32(hash, (int)claim.GameFamily);
        AppendText(hash, budget, claim.Address.File.CanonicalKey);
        AppendInt32(hash, (int)claim.Address.ScopeKind);
        AppendNullableText(hash, budget, claim.Address.ArchiveMember?.Value);
        if (claim.Address.Record is { } record)
        {
            AppendBoolean(hash, true);
            AppendInt32(hash, (int)record.GameFamily);
            AppendText(hash, budget, record.Domain.Value);
            AppendText(hash, budget, record.RecordKind.Key);
            AppendInt32(hash, record.RecordKind.SchemaVersion);
            AppendText(hash, budget, record.RecordId.Value);
            AppendNullableText(hash, budget, record.SubrecordId?.Value);
        }
        else
        {
            AppendBoolean(hash, false);
        }

        if (claim.Address.ByteRange is { } range)
        {
            AppendBoolean(hash, true);
            AppendInt64(hash, range.Offset);
            AppendInt64(hash, range.Length);
        }
        else
        {
            AppendBoolean(hash, false);
        }

        AppendText(hash, budget, claim.OwnerId.Value);
        AppendText(hash, budget, claim.PreservationRule.Key);
        AppendInt32(hash, claim.PreservationRule.SchemaVersion);
        AppendBoolean(hash, claim.PreservationRule.PreservesUnownedData);
        AppendBoolean(hash, claim.PreservationRule.RequiresPreimage);
    }

    private static void AppendState(IncrementalHash hash, FramingBudget budget, OutputFileState state)
    {
        AppendBoolean(hash, state.Exists);
        AppendNullableText(hash, budget, state.Sha256);
        AppendInt64(hash, state.LengthBytes);
    }

    private static string ValidateText(string value, int maximumCharacters, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumCharacters
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Review fingerprint text is invalid or out of bounds.", parameterName);
        }

        return value.Normalize(NormalizationForm.FormC);
    }

    private static string? ValidateOptionalText(string? value, int maximumCharacters, string parameterName)
    {
        return value is null ? null : ValidateText(value, maximumCharacters, parameterName);
    }

    private static void AppendNullableText(IncrementalHash hash, FramingBudget budget, string? value)
    {
        AppendBoolean(hash, value is not null);
        if (value is not null)
        {
            AppendText(hash, budget, value);
        }
    }

    private static void AppendText(IncrementalHash hash, FramingBudget budget, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        budget.Consume(byteCount);
        AppendInt32(hash, byteCount);
        if (byteCount == 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(bytes);
    }

    private static void AppendBoolean(IncrementalHash hash, bool value)
    {
        hash.AppendData([value ? (byte)1 : (byte)0]);
    }

    private static void AppendNullableBoolean(IncrementalHash hash, bool? value)
    {
        AppendBoolean(hash, value.HasValue);
        if (value.HasValue)
        {
            AppendBoolean(hash, value.Value);
        }
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private sealed class FramingBudget
    {
        private readonly int maximumBytes;
        private int consumedBytes;

        public FramingBudget(int maximumBytes)
        {
            this.maximumBytes = maximumBytes;
        }

        public void Consume(int byteCount)
        {
            if (byteCount < 0 || consumedBytes > maximumBytes - byteCount)
            {
                throw new OutputLimitExceededException("Review fingerprint text exceeds its aggregate size limit.");
            }

            consumedBytes += byteCount;
        }
    }
}
