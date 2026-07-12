// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Text;
using KM.Formats.SwSh;
using KM.Tools.Bridge;
using System.Diagnostics;
using System.Text;
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

    [Fact]
    public async Task RunAsyncWritesOneResponseForEveryRequestLine()
    {
        var requests = string.Join(
            Environment.NewLine,
            JsonSerializer.Serialize(new
            {
                command = "first-unsupported-command",
                payload = new { },
                requestId = "persistent-bridge-1",
            }),
            JsonSerializer.Serialize(new
            {
                command = "second-unsupported-command",
                payload = new { },
                requestId = "persistent-bridge-2",
            }));
        var input = new StringReader(requests);
        var output = new StringWriter();

        var exitCode = await new BridgeLineRunner().RunAsync(
            input,
            output,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var responses = output.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, responses.Length);
        using var first = JsonDocument.Parse(responses[0]);
        using var second = JsonDocument.Parse(responses[1]);
        Assert.Equal("persistent-bridge-1", first.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("persistent-bridge-2", second.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task HiddenBridgeProcessPreservesUnicodeAcrossRepeatedRequests()
    {
        const string command = "Farfetch’d_日本_café_Straße_Español_Русский_Українська_简体中文";
        var expectedMessage = $"Bridge command '{command}' is not supported.";
        var request = JsonSerializer.Serialize(new
        {
            command,
            payload = new { },
            requestId = "unicode-hidden-bridge",
        });

        await using var bridge = await HiddenBridgeProcess.StartAsync();
        Assert.Equal(expectedMessage, ReadErrorMessage(await bridge.SendAsync(request)));
        Assert.False(bridge.HasExited);
        Assert.Equal(expectedMessage, ReadErrorMessage(await bridge.SendAsync(request)));
        Assert.False(bridge.HasExited);
    }

    [Fact]
    public async Task HiddenBridgeValidationDoesNotCompoundUnicodeSessionText()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        const string summary = "Set Farfetch’d_日本 evolution slot 0.";
        var session = new EditSessionDto(
            "unicode-validation-session",
            HasPendingChanges: true,
            [
                new PendingEditDto(
                    "workflow.items",
                    summary,
                    [new FileProvenanceDto(FileLayerDto.Base, "data/items.bin")],
                    RecordId: "1",
                    Field: "price",
                    NewValue: "1"),
            ]);

        for (var pass = 0; pass < 3; pass++)
        {
            var request = new BridgeRequest<ValidateEditSessionRequest>(
                KmCommandNames.ValidateEditSession,
                new ValidateEditSessionRequest(temp.Paths, session),
                RequestId: $"unicode-validation-{pass}");
            var responseJson = await RunHiddenBridgeJson(
                JsonSerializer.Serialize(request, BridgeJson.SerializerOptions));
            var response = JsonSerializer.Deserialize<BridgeResponse<ValidateEditSessionResponse>>(
                responseJson,
                BridgeJson.SerializerOptions);

            Assert.NotNull(response?.Payload);
            session = response.Payload.Session;
            Assert.Equal(summary, Assert.Single(session.PendingEdits).Summary);
        }
    }

    [Fact]
    public async Task HiddenBridgeWritesExactUnicodeTextToGameOutput()
    {
        using var temp = TemporaryBridgeProject.Create("Unicode_日本_café_Українська_简体中文");
        SwShTextBridgeFixtures.WriteBaseText(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        const string value = "Farfetch’d · Pokémon · café · Straße · Español · Русский · Українська · 简体中文 · 日本語";

        var update = await RunHiddenBridgeRequest<UpdateTextEntryRequest, UpdateTextEntryResponse>(
            KmCommandNames.UpdateTextEntry,
            new UpdateTextEntryRequest(
                temp.Paths,
                Session: null,
                TextKey: "romfs/bin/message/English/common/story.dat#0",
                Value: value),
            "unicode-output-update");
        var session = update.Session;
        Assert.Equal(value, Assert.Single(session.PendingEdits).NewValue);

        for (var pass = 0; pass < 3; pass++)
        {
            var validation = await RunHiddenBridgeRequest<ValidateEditSessionRequest, ValidateEditSessionResponse>(
                KmCommandNames.ValidateEditSession,
                new ValidateEditSessionRequest(temp.Paths, session),
                $"unicode-output-validation-{pass}");
            session = validation.Session;
            Assert.Equal(value, Assert.Single(session.PendingEdits).NewValue);
        }

        var plan = await RunHiddenBridgeRequest<CreateChangePlanRequest, CreateChangePlanResponse>(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, session),
            "unicode-output-plan");
        var apply = await RunHiddenBridgeRequest<ApplyChangePlanRequest, ApplyChangePlanResponse>(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, session, plan.ChangePlan),
            "unicode-output-apply");

        Assert.Contains(
            "romfs/bin/message/English/common/story.dat",
            apply.ApplyResult.WrittenFiles);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            "story.dat");
        var output = SwShGameTextFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(value, output.Lines[0].Text);
        Assert.Equal("Second line.", output.Lines[1].Text);
    }

    private static async Task<string> RunHiddenBridgeRequest(string command)
    {
        var request = JsonSerializer.Serialize(new
        {
            command,
            payload = new { },
            requestId = "unicode-hidden-bridge",
        });
        var responseJson = await RunHiddenBridgeJson(request);
        using var response = JsonDocument.Parse(responseJson);
        return response.RootElement
            .GetProperty("error")
            .GetProperty("message")
            .GetString()
            ?? string.Empty;
    }

    private static string ReadErrorMessage(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        return response.RootElement
            .GetProperty("error")
            .GetProperty("message")
            .GetString()
            ?? string.Empty;
    }

    private static async Task<TResponse> RunHiddenBridgeRequest<TRequest, TResponse>(
        string command,
        TRequest payload,
        string requestId)
    {
        var request = new BridgeRequest<TRequest>(command, payload, requestId);
        var responseJson = await RunHiddenBridgeJson(
            JsonSerializer.Serialize(request, BridgeJson.SerializerOptions));
        var response = JsonSerializer.Deserialize<BridgeResponse<TResponse>>(
            responseJson,
            BridgeJson.SerializerOptions);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        return response.Payload;
    }

    private static async Task<string> RunHiddenBridgeJson(string requestJson)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(BridgeLineRunner).Assembly.Location);
        Assert.False(string.IsNullOrWhiteSpace(assemblyDirectory));
        var executablePath = Path.Combine(
            assemblyDirectory,
            OperatingSystem.IsWindows() ? "KM.Tools.exe" : "KM.Tools");
        Assert.True(File.Exists(executablePath), $"Bridge executable was not found at {executablePath}.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "bridge-once",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        await process.StandardInput.WriteLineAsync(requestJson);
        process.StandardInput.Close();
        var responseJson = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, standardError);
        return responseJson;
    }

    private sealed class HiddenBridgeProcess : IAsyncDisposable
    {
        private readonly Process process;

        private HiddenBridgeProcess(Process process)
        {
            this.process = process;
        }

        public bool HasExited => process.HasExited;

        public static Task<HiddenBridgeProcess> StartAsync()
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(BridgeLineRunner).Assembly.Location);
            Assert.False(string.IsNullOrWhiteSpace(assemblyDirectory));
            var executablePath = Path.Combine(
                assemblyDirectory,
                OperatingSystem.IsWindows() ? "KM.Tools.exe" : "KM.Tools");
            Assert.True(File.Exists(executablePath), $"Bridge executable was not found at {executablePath}.");
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "bridge",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            var process = Process.Start(startInfo);
            Assert.NotNull(process);
            return Task.FromResult(new HiddenBridgeProcess(process));
        }

        public async Task<string> SendAsync(string requestJson)
        {
            await process.StandardInput.WriteLineAsync(requestJson);
            await process.StandardInput.FlushAsync(TestContext.Current.CancellationToken);
            var response = await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken);
            Assert.False(string.IsNullOrWhiteSpace(response));
            return response;
        }

        public async ValueTask DisposeAsync()
        {
            process.StandardInput.Close();
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            process.Dispose();
        }
    }
}
