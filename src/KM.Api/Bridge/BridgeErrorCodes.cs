// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Bridge;

public static class BridgeErrorCodes
{
    public const string EmptyRequest = "KM-BRIDGE-EMPTY-REQUEST";
    public const string RequestTooLarge = "KM-BRIDGE-REQUEST-TOO-LARGE";
    public const string WorkspaceConflict = "KM-WORKSPACE-CONCURRENT-MODIFICATION";
    public const string OutputConcurrentModification = "KM-OUTPUT-CONCURRENT-MODIFICATION";
    public const string OutputRecoveryRequired = "KM-OUTPUT-RECOVERY-REQUIRED";
    public const string OutputOwnershipUnproven = "KM-OUTPUT-OWNERSHIP-UNPROVEN";
    public const string OutputRootBusy = "KM-OUTPUT-ROOT-BUSY";
    public const string OutputUnsafePath = "KM-OUTPUT-UNSAFE-PATH";
    public const string OutputLimitExceeded = "KM-OUTPUT-LIMIT-EXCEEDED";
    public const string OutputCheckpointNotFound = "KM-OUTPUT-CHECKPOINT-NOT-FOUND";
    public const string OutputCheckpointConflict = "KM-OUTPUT-CHECKPOINT-CONFLICT";
    public const string ProjectRelocationMismatch = "KM-PROJECT-RELOCATION-MISMATCH";
    public const string ProjectRelocationConflict = "KM-PROJECT-RELOCATION-CONFLICT";
    public const string MissingCommand = "KM-BRIDGE-MISSING-COMMAND";
    public const string UnsupportedCommand = "KM-BRIDGE-UNSUPPORTED-COMMAND";
    public const string InvalidJson = "KM-BRIDGE-INVALID-JSON";
    public const string GameMismatch = "KM-BRIDGE-GAME-MISMATCH";
    public const string Unexpected = "KM-BRIDGE-UNEXPECTED";
    public const string AccessDenied = "KM-BRIDGE-ACCESS-DENIED";
    public const string ResourceMissing = "KM-BRIDGE-RESOURCE-MISSING";
    public const string DataInvalid = "KM-BRIDGE-DATA-INVALID";
    public const string DataLayoutInvalid = "KM-BRIDGE-DATA-LAYOUT-INVALID";
    public const string DataSupportUnavailable = "KM-BRIDGE-SUPPORT-RUNTIME-UNAVAILABLE";
    public const string IoFailed = "KM-BRIDGE-IO-FAILED";
    public const string InternalFailure = "KM-BRIDGE-INTERNAL-FAILURE";
}
