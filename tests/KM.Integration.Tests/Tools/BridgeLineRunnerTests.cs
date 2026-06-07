// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Projects;
using KM.Tools.Bridge;
using System.Text.Json;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class BridgeLineRunnerTests
{
    [Fact]
    public async Task RunOnceAsyncWritesOneBridgeResponseLine()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var request = new BridgeRequest<ValidateProjectRequest>(
            KmCommandNames.ValidateProject,
            new ValidateProjectRequest(temp.Paths with { OutputRootPath = null }),
            RequestId: "request-line");
        var input = new StringReader(JsonSerializer.Serialize(request, BridgeJson.SerializerOptions));
        var output = new StringWriter();

        var exitCode = await new BridgeLineRunner().RunOnceAsync(input, output, TestContext.Current.CancellationToken);

        var response = JsonSerializer.Deserialize<BridgeResponse<ValidateProjectResponse>>(
            output.ToString(),
            BridgeJson.SerializerOptions);
        Assert.Equal(0, exitCode);
        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.Equal("request-line", response.RequestId);
        Assert.Equal(ProjectHealthStateDto.ReadOnlyReady, response.Payload?.Health.State);
    }
}
