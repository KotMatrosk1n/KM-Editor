// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KM.Api.Bridge;

/// <summary>
/// Shared JSON settings for the local UI/backend bridge wire contract.
/// </summary>
public static class BridgeJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Bridge enums cross as readable strings instead of numeric enum values.
        options.Converters.Add(new JsonStringEnumConverter<ApiDiagnosticSeverity>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ChangePlanOutputModeDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<FileLayerDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectGameDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectHealthStateDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectFileGraphEntryStateDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectFileLayerDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectPathRoleDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectPathStatusDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<WorkflowAvailabilityDto>(JsonNamingPolicy.CamelCase));

        return options;
    }
}
