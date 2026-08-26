// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;

namespace KM.SV.GameModules;

public enum SvTypeEffectivenessChartState
{
    Vanilla,
    Modified,
}

public sealed record SvTypeEffectivenessSource(
    string SourceIdentity,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState,
    string BuildId,
    ProjectGame Game,
    SvTypeEffectivenessChartState ChartState);

public sealed record SvTypeEffectivenessCell(
    string StableIdentity,
    int AttackTypeId,
    int DefenseTypeId,
    int Effectiveness,
    int VanillaEffectiveness);

public sealed record SvTypeEffectivenessStateProjection(
    SvTypeEffectivenessSource Source,
    IReadOnlyList<SvTypeEffectivenessCell> Cells,
    int ChangedCellCount);

public sealed class SvTypeEffectivenessObservationChangedException : IOException
{
    public SvTypeEffectivenessObservationChangedException()
        : base("The Scarlet/Violet type-effectiveness source changed during fresh inspection.")
    {
    }
}

public sealed class SvTypeEffectivenessUnsupportedSourceException : NotSupportedException
{
    public SvTypeEffectivenessUnsupportedSourceException(string message)
        : base(message)
    {
    }
}
