// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.GameDump;
using KM.Api.Projects;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.GameDump;
using KM.Core.Projects;

namespace KM.Tools.Bridge;

public static class ProjectBridgeMapper
{
    public static ProjectPaths ToCore(ProjectPathsDto paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new ProjectPaths(
            paths.BaseRomFsPath,
            paths.BaseExeFsPath,
            paths.OutputRootPath,
            paths.SaveFilePath,
            paths.ScarletVioletSupportFolderPath,
            ToCore(paths.SelectedGame))
        {
            GameTextLanguage = paths.GameTextLanguage,
        };
    }

    public static ProjectHealthDto ToDto(ProjectHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        return new ProjectHealthDto(
            ToDto(health.State),
            health.CanOpenReadOnlyWorkflows,
            health.CanOpenEditableWorkflows,
            health.Paths.Select(ToDto).ToArray(),
            ToDto(health.FileGraph),
            health.Diagnostics.Select(ToDto).ToArray());
    }

    public static ProjectFileGraphDto ToDto(ProjectFileGraph fileGraph)
    {
        ArgumentNullException.ThrowIfNull(fileGraph);

        return new ProjectFileGraphDto(
            fileGraph.Entries.Select(ToDto).ToArray(),
            ToDto(fileGraph.ToSummary()));
    }

    private static ProjectPathValidationDto ToDto(ProjectPathValidation path)
    {
        return new ProjectPathValidationDto(
            ToDto(path.Role),
            path.Path,
            ToDto(path.Status),
            path.IsRequired,
            path.Diagnostics.Select(ToDto).ToArray());
    }

    public static ApiDiagnostic ToDto(ValidationDiagnostic diagnostic)
    {
        return new ApiDiagnostic(
            ToDto(diagnostic.Severity),
            diagnostic.Message,
            diagnostic.File,
            diagnostic.Domain,
            diagnostic.Field,
            diagnostic.Expected);
    }

    public static GameDumpWorkflowDto ToDto(GameDumpWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new GameDumpWorkflowDto(
            workflow.Categories.Select(ToDto).ToArray(),
            workflow.Diagnostics.Select(ToDto).ToArray());
    }

    public static GameDumpResultDto ToDto(GameDumpResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new GameDumpResultDto(
            result.DestinationFolder,
            result.WrittenFiles.Select(ToDto).ToArray(),
            result.Diagnostics.Select(ToDto).ToArray(),
            result.Succeeded);
    }

    public static IReadOnlyList<GameDumpSelection> ToCore(IReadOnlyList<GameDumpSelectionDto> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        return selections
            .Select(selection => new GameDumpSelection(
                selection.CategoryId,
                ToCore(selection.Format)))
            .ToArray();
    }

    private static GameDumpCategoryDto ToDto(GameDumpCategory category)
    {
        return new GameDumpCategoryDto(
            category.Id,
            category.Label,
            category.Description,
            ToDto(category.Kind),
            category.Formats.Select(ToDto).ToArray(),
            ToDto(category.DefaultFormat),
            category.IsAvailable,
            category.Diagnostics.Select(ToDto).ToArray());
    }

    private static GameDumpWrittenFileDto ToDto(GameDumpWrittenFile file)
    {
        return new GameDumpWrittenFileDto(file.CategoryId, file.RelativePath, file.SizeBytes);
    }

    private static GameDumpCategoryKindDto ToDto(GameDumpCategoryKind kind)
    {
        return kind switch
        {
            GameDumpCategoryKind.Table => GameDumpCategoryKindDto.Table,
            GameDumpCategoryKind.Text => GameDumpCategoryKindDto.Text,
            GameDumpCategoryKind.Raw => GameDumpCategoryKindDto.Raw,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static GameDumpFormatDto ToDto(GameDumpFormat format)
    {
        return format switch
        {
            GameDumpFormat.Tsv => GameDumpFormatDto.Tsv,
            GameDumpFormat.Csv => GameDumpFormatDto.Csv,
            GameDumpFormat.Json => GameDumpFormatDto.Json,
            GameDumpFormat.TsvAndJson => GameDumpFormatDto.TsvAndJson,
            GameDumpFormat.Txt => GameDumpFormatDto.Txt,
            GameDumpFormat.TxtAndJson => GameDumpFormatDto.TxtAndJson,
            GameDumpFormat.Raw => GameDumpFormatDto.Raw,
            GameDumpFormat.RawAndJson => GameDumpFormatDto.RawAndJson,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    private static GameDumpFormat ToCore(GameDumpFormatDto format)
    {
        return format switch
        {
            GameDumpFormatDto.Tsv => GameDumpFormat.Tsv,
            GameDumpFormatDto.Csv => GameDumpFormat.Csv,
            GameDumpFormatDto.Json => GameDumpFormat.Json,
            GameDumpFormatDto.TsvAndJson => GameDumpFormat.TsvAndJson,
            GameDumpFormatDto.Txt => GameDumpFormat.Txt,
            GameDumpFormatDto.TxtAndJson => GameDumpFormat.TxtAndJson,
            GameDumpFormatDto.Raw => GameDumpFormat.Raw,
            GameDumpFormatDto.RawAndJson => GameDumpFormat.RawAndJson,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    private static ProjectFileGraphEntryDto ToDto(ProjectFileGraphEntry entry)
    {
        return new ProjectFileGraphEntryDto(
            entry.RelativePath,
            entry.BaseFile is null ? null : ToDto(entry.BaseFile),
            entry.LayeredFile is null ? null : ToDto(entry.LayeredFile),
            ToDto(entry.State));
    }

    private static ProjectFileReferenceDto ToDto(ProjectFileReference reference)
    {
        return new ProjectFileReferenceDto(ToDto(reference.Layer), reference.RelativePath);
    }

    private static ProjectFileGraphSummaryDto ToDto(ProjectFileGraphSummary summary)
    {
        return new ProjectFileGraphSummaryDto(
            summary.BaseFileCount,
            summary.LayeredFileCount,
            summary.OverrideCount,
            summary.LayeredOnlyCount);
    }

    private static ApiDiagnosticSeverity ToDto(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Info => ApiDiagnosticSeverity.Info,
            DiagnosticSeverity.Warning => ApiDiagnosticSeverity.Warning,
            DiagnosticSeverity.Error => ApiDiagnosticSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
        };
    }

    public static ProjectFileGraphEntryStateDto ToDto(ProjectFileGraphEntryState state)
    {
        return state switch
        {
            ProjectFileGraphEntryState.BaseOnly => ProjectFileGraphEntryStateDto.BaseOnly,
            ProjectFileGraphEntryState.LayeredOverride => ProjectFileGraphEntryStateDto.LayeredOverride,
            ProjectFileGraphEntryState.LayeredOnly => ProjectFileGraphEntryStateDto.LayeredOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    public static ProjectFileLayerDto ToDto(ProjectFileLayer layer)
    {
        return layer switch
        {
            ProjectFileLayer.Base => ProjectFileLayerDto.Base,
            ProjectFileLayer.Layered => ProjectFileLayerDto.Layered,
            ProjectFileLayer.Pending => ProjectFileLayerDto.Pending,
            ProjectFileLayer.Generated => ProjectFileLayerDto.Generated,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
        };
    }

    public static ProjectGameDto ToDto(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => ProjectGameDto.Sword,
            ProjectGame.Shield => ProjectGameDto.Shield,
            ProjectGame.Scarlet => ProjectGameDto.Scarlet,
            ProjectGame.Violet => ProjectGameDto.Violet,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static ProjectHealthStateDto ToDto(ProjectHealthState state)
    {
        return state switch
        {
            ProjectHealthState.NeedsPaths => ProjectHealthStateDto.NeedsPaths,
            ProjectHealthState.ReadOnlyReady => ProjectHealthStateDto.ReadOnlyReady,
            ProjectHealthState.EditableReady => ProjectHealthStateDto.EditableReady,
            ProjectHealthState.Blocked => ProjectHealthStateDto.Blocked,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    private static ProjectGame? ToCore(ProjectGameDto? game)
    {
        return game switch
        {
            ProjectGameDto.Sword => ProjectGame.Sword,
            ProjectGameDto.Shield => ProjectGame.Shield,
            ProjectGameDto.Scarlet => ProjectGame.Scarlet,
            ProjectGameDto.Violet => ProjectGame.Violet,
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static ProjectPathRoleDto ToDto(ProjectPathRole role)
    {
        return role switch
        {
            ProjectPathRole.BaseRomFs => ProjectPathRoleDto.BaseRomFs,
            ProjectPathRole.BaseExeFs => ProjectPathRoleDto.BaseExeFs,
            ProjectPathRole.OutputRoot => ProjectPathRoleDto.OutputRoot,
            ProjectPathRole.SaveFile => ProjectPathRoleDto.SaveFile,
            ProjectPathRole.ScarletVioletSupportFolder => ProjectPathRoleDto.ScarletVioletSupportFolder,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private static ProjectPathStatusDto ToDto(ProjectPathStatus status)
    {
        return status switch
        {
            ProjectPathStatus.NotSet => ProjectPathStatusDto.NotSet,
            ProjectPathStatus.Missing => ProjectPathStatusDto.Missing,
            ProjectPathStatus.WrongKind => ProjectPathStatusDto.WrongKind,
            ProjectPathStatus.Valid => ProjectPathStatusDto.Valid,
            ProjectPathStatus.Unsafe => ProjectPathStatusDto.Unsafe,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}

