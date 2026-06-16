// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSaveSeedServiceTests
{
    [Fact]
    public void SetSeedReturnsDiagnosticWhenSaveFilePathIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        var service = new SwShDynamaxAdventureSaveSeedService();

        var result = service.SetSeed(temp.Paths, seed: 0x1234);

        Assert.False(result.WasChanged);
        Assert.False(result.ChecksumsValid);
        Assert.Null(result.SaveFilePath);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("save file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetSeedReturnsDiagnosticWhenSaveFileDoesNotExist()
    {
        using var temp = TemporarySwShProject.Create();
        var missingSavePath = Path.Combine(temp.RootPath, "missing-main");
        var service = new SwShDynamaxAdventureSaveSeedService();

        var result = service.SetSeed(
            temp.Paths with { SaveFilePath = missingSavePath },
            seed: 0x1234);

        Assert.False(result.WasChanged);
        Assert.False(result.ChecksumsValid);
        Assert.Equal(missingSavePath, result.SaveFilePath);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("could not be found", StringComparison.OrdinalIgnoreCase));
    }
}
