// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using System.Text.Json.Serialization;

namespace KM.Api.Bridge;

/// <summary>
/// Typed response envelope for local UI/backend bridge messages.
/// </summary>
/// <typeparam name="TPayload">The response payload contract.</typeparam>
public sealed record BridgeResponse<TPayload>(
    TPayload? Payload,
    ApiError? Error,
    string? RequestId = null)
{
    [JsonIgnore]
    public bool Succeeded => Error is null;

    public static BridgeResponse<TPayload> Success(TPayload payload, string? requestId = null)
    {
        return new BridgeResponse<TPayload>(payload, Error: null, requestId);
    }

    public static BridgeResponse<TPayload> Failure(ApiError error, string? requestId = null)
    {
        return new BridgeResponse<TPayload>(Payload: default, error, requestId);
    }
}

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public static ApiError Create(string code, string message)
    {
        return new ApiError(code, message, Array.Empty<ApiDiagnostic>());
    }
}
