// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
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

        // Diagnostics cross the bridge as readable strings instead of numeric enum values.
        options.Converters.Add(new JsonStringEnumConverter<ApiDiagnosticSeverity>(JsonNamingPolicy.CamelCase));

        return options;
    }
}
