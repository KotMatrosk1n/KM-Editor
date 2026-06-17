// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SwSh.DynamaxAdventures;

public sealed record SwShDynamaxAdventuresEditResult(
    SwShDynamaxAdventuresWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShDynamaxAdventureDefaultField(
    string Field,
    string Value);

public sealed record SwShDynamaxAdventureDefaultPreview(
    IReadOnlyList<SwShDynamaxAdventureDefaultField> Changes,
    IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> AbilityOptions,
    IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> GigantamaxOptions,
    IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> MoveOptions,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
