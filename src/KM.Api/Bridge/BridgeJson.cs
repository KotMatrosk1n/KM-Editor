// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
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
        options.Converters.Add(new JsonStringEnumConverter<ProjectHealthStateDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectPathRoleDto>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ProjectPathStatusDto>(JsonNamingPolicy.CamelCase));

        return options;
    }
}
