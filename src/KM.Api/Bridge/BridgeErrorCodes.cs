// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Bridge;

public static class BridgeErrorCodes
{
    public const string EmptyRequest = "KM-BRIDGE-EMPTY-REQUEST";
    public const string MissingCommand = "KM-BRIDGE-MISSING-COMMAND";
    public const string UnsupportedCommand = "KM-BRIDGE-UNSUPPORTED-COMMAND";
    public const string InvalidJson = "KM-BRIDGE-INVALID-JSON";
    public const string GameMismatch = "KM-BRIDGE-GAME-MISMATCH";
    public const string Unexpected = "KM-BRIDGE-UNEXPECTED";
}
