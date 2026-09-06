// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Models;
using KM.Core.Projects;
using KM.SV.Models;
namespace KM.Tools.Bridge;

internal static class ModelPreviewBridge
{
    internal static object Catalog(ModelCatalogRequest request) =>
        new SvModelPreviewService().Catalog(Open(request.Paths));

    internal static object Prepare(ModelPrepareRequest request)
    {
        if (request.TransferId is not { Length: 32 } || !request.TransferId.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid model transfer identifier.");
        var scene = new SvModelPreviewService().Prepare(Open(request.Paths), request.Id);
        var folder = Path.Combine(Path.GetTempPath(), "km-editor-model-preview");
        var path = Path.Combine(folder, request.TransferId + ".kmv");
        // The native host owns a delete-on-close handle. Even a cancelled worker or host
        // crash cannot leave a completed transfer accumulating on disk.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length != 0) throw new InvalidDataException("Model transfer is already populated.");
        scene.Write(stream);
        if (stream.Length > 96L * 1024 * 1024) throw new InvalidDataException("Model preview exceeds the transfer budget.");
        return new { Ready = true };
    }

    private static OpenedProject Open(KM.Api.Projects.ProjectPathsDto paths)
    {
        var core = ProjectBridgeMapper.ToCore(paths);
        if (core.SelectedGame is not (ProjectGame.Scarlet or ProjectGame.Violet))
            throw new InvalidDataException("The Beta model viewer currently supports Scarlet and Violet.");
        return new ProjectWorkspaceService().ValidateAndOpen(core);
    }
}
