// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Encodings.Web;
using System.Text.Json;

namespace KM.Core.Workspace;

/// <summary>
/// Defines the JSON serialization profile for private authored workspace documents.
/// </summary>
public static class PrivateWorkspaceJson
{
    public static JsonSerializerOptions CreateSerializerOptions(
        JsonSerializerOptions? baseOptions = null)
    {
        var options = baseOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(baseOptions);
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        return options;
    }
}
