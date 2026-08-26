// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Security.Cryptography;

namespace KM.SV.TmMachine;

public sealed class SvTmMachineControlsWorkflowService
{
    public const string RecipeDataPath = SvDataPaths.ShopWazaMachineDataArray;
    public const string MaterialTrackingScriptPath = "script/lua/bin/release/main/main.blua";
    public const string WorkflowId = SvWorkflowIds.TmMachineControls;

    private readonly SvWorkflowFileSource fileSource;

    public SvTmMachineControlsWorkflowService()
        : this(new SvWorkflowFileSource())
    {
    }

    internal SvTmMachineControlsWorkflowService(SvWorkflowFileSource fileSource)
    {
        this.fileSource = fileSource;
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return SvWorkflowSupport.CreateSummary(
            project,
            WorkflowId,
            "TM Machine Controls",
            "Control TM recipe availability and tracking-window material visibility independently.");
    }

    public SvTmMachineControlsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var provenance = new List<SvTmMachineControlProvenance>();
        var recipeState = BlockedState(
            "progressionGated",
            "TM recipe availability cannot load until the project is ready.",
            SvTmRecipeAvailabilityPatcher.ExpectedRecipeCount);
        var materialState = BlockedState(
            "discoveryGated",
            "TM material visibility cannot load until the project is ready.");

        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "TM Machine Controls requires a Pokemon Scarlet or Pokemon Violet project.",
                expected: "Scarlet or Violet project",
                code: SvTmMachineControlsDiagnosticCodes.ProjectUnsupported));
            return CreateWorkflow(summary, recipeState, materialState, provenance, diagnostics);
        }

        if (summary.Availability == SvWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, recipeState, materialState, provenance, diagnostics);
        }

        try
        {
            var baseRecipe = fileSource.ReadBase(project, RecipeDataPath);
            var currentRecipe = fileSource.Read(project, RecipeDataPath);
            var analysis = SvTmRecipeAvailabilityPatcher.Analyze(baseRecipe.Bytes, currentRecipe.Bytes);
            recipeState = ToRecipeState(analysis, summary.Availability);
            provenance.Add(CreateProvenance("recipeAvailability", currentRecipe));
            if (analysis.Kind == SvTmRecipeAvailabilityKind.Unsupported)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    analysis.Message,
                    $"romfs/{RecipeDataPath}",
                    expected: "Supported Scarlet/Violet 4.0.0 TM recipe table",
                    code: SvTmMachineControlsDiagnosticCodes.RecipeSourceUnsupported));
            }
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException)
        {
            recipeState = BlockedState(
                "progressionGated",
                $"TM recipe availability could not be inspected: {exception.Message}",
                SvTmRecipeAvailabilityPatcher.ExpectedRecipeCount);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                recipeState.Message,
                $"romfs/{RecipeDataPath}",
                expected: "Readable Scarlet/Violet 4.0.0 TM recipe table",
                code: SvTmMachineControlsDiagnosticCodes.RecipeSourceUnsupported));
        }

        try
        {
            var baseScript = fileSource.ReadBase(project, MaterialTrackingScriptPath);
            var currentScript = fileSource.Read(project, MaterialTrackingScriptPath);
            var baseAnalysis = SvTmMaterialVisibilityPatcher.Analyze(baseScript.Bytes);
            var currentAnalysis = SvTmMaterialVisibilityPatcher.Analyze(currentScript.Bytes);
            if (baseAnalysis.Kind != SvTmMaterialVisibilityKind.DiscoveryGated)
            {
                materialState = BlockedState(
                    "discoveryGated",
                    "The base tracking script is not the supported Scarlet/Violet 4.0.0 input.");
            }
            else
            {
                materialState = ToMaterialState(currentAnalysis, summary.Availability);
            }

            provenance.Add(CreateProvenance("materialVisibility", currentScript));
            if (!materialState.CanStage && summary.Availability == SvWorkflowAvailability.Available)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    materialState.Message,
                    $"romfs/{MaterialTrackingScriptPath}",
                    expected: "Supported Scarlet/Violet 4.0.0 tracking script",
                    code: SvTmMachineControlsDiagnosticCodes.MaterialSourceUnsupported));
            }
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException)
        {
            materialState = BlockedState(
                "discoveryGated",
                $"TM material visibility could not be inspected: {exception.Message}");
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                materialState.Message,
                $"romfs/{MaterialTrackingScriptPath}",
                expected: "Readable Scarlet/Violet 4.0.0 tracking script",
                code: SvTmMachineControlsDiagnosticCodes.MaterialSourceUnsupported));
        }

        return CreateWorkflow(summary, recipeState, materialState, provenance, diagnostics);
    }

    private static SvTmMachineControlsWorkflow CreateWorkflow(
        SvWorkflowSummary summary,
        SvTmMachineControlState recipeState,
        SvTmMachineControlState materialState,
        IReadOnlyList<SvTmMachineControlProvenance> provenance,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SvTmMachineControlsWorkflow(
            summary,
            "Scarlet/Violet 4.0.0",
            recipeState,
            materialState,
            provenance,
            new SvTmMachineControlsStats(
                SvTmRecipeAvailabilityPatcher.ExpectedRecipeCount,
                provenance.Count,
                SupportedBuildCount: 2),
            diagnostics);
    }

    private static SvTmMachineControlState ToRecipeState(
        SvTmRecipeAvailabilityAnalysis analysis,
        SvWorkflowAvailability availability)
    {
        var (policy, status) = analysis.Kind switch
        {
            SvTmRecipeAvailabilityKind.ProgressionGated => ("progressionGated", "standard"),
            SvTmRecipeAvailabilityKind.AllAvailable => ("allAvailable", "installed"),
            SvTmRecipeAvailabilityKind.Customized => ("customized", "customized"),
            _ => ("unknown", "blocked"),
        };
        return new SvTmMachineControlState(
            policy,
            status,
            analysis.Message,
            analysis.Kind != SvTmRecipeAvailabilityKind.Unsupported
                && availability == SvWorkflowAvailability.Available,
            StagedPolicy: null,
            analysis.MatchingRecordCount,
            analysis.TotalRecordCount);
    }

    private static SvTmMachineControlState ToMaterialState(
        SvTmMaterialVisibilityAnalysis analysis,
        SvWorkflowAvailability availability)
    {
        var (policy, status) = analysis.Kind switch
        {
            SvTmMaterialVisibilityKind.DiscoveryGated => ("discoveryGated", "standard"),
            SvTmMaterialVisibilityKind.AlwaysVisible => ("alwaysVisible", "installed"),
            _ => ("unknown", "blocked"),
        };
        return new SvTmMachineControlState(
            policy,
            status,
            analysis.Message,
            analysis.Kind != SvTmMaterialVisibilityKind.Unsupported
                && availability == SvWorkflowAvailability.Available,
            StagedPolicy: null,
            MatchingRecordCount: analysis.Kind == SvTmMaterialVisibilityKind.Unsupported ? 0 : 1,
            TotalRecordCount: 1);
    }

    private static SvTmMachineControlState BlockedState(
        string policy,
        string message,
        int totalRecordCount = 1)
    {
        return new SvTmMachineControlState(
            policy,
            "blocked",
            message,
            CanStage: false,
            StagedPolicy: null,
            MatchingRecordCount: 0,
            totalRecordCount);
    }

    private static SvTmMachineControlProvenance CreateProvenance(
        string control,
        SvWorkflowFile source)
    {
        return new SvTmMachineControlProvenance(
            control,
            source.RelativePath,
            source.SourceLayer,
            source.FileState,
            Convert.ToHexString(SHA256.HashData(source.Bytes)));
    }

    internal static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null,
        string? code = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            file,
            Domain: "sv.tmMachineControls",
            Field: field,
            Expected: expected)
        {
            Code = code,
        };
    }
}
