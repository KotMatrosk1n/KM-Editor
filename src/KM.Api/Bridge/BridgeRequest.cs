// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Bridge;

/// <summary>
/// Typed request envelope for local UI/backend bridge messages.
/// </summary>
/// <typeparam name="TPayload">The command payload contract.</typeparam>
public sealed record BridgeRequest<TPayload>(
    string Command,
    TPayload Payload,
    string? RequestId = null);
