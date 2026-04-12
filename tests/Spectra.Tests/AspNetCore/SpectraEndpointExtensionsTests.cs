using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.AspNetCore;
using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Streaming;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Registration;
using Xunit;

namespace Spectra.Tests.AspNetCore;

public class SpectraEndpointExtensionsTests : IAsyncDisposable
{
    private readonly string _workflowDir;
    private IHost? _host;

    public SpectraEndpointExtensionsTests()
    {
        _workflowDir = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workflowDir);

        // Write a test workflow JSON
        var workflowJson = """
        {
          "id": "test-workflow",
          "name": "Test Workflow",
          "version": 1,
          "entryNodeId": "greet",
          "nodes": [
            {
              "id": "greet",
              "stepType": "echo",
              "parameters": { "message": "Hello test!" }
            }
          ],
          "edges": [],
          "agents": [],
          "stateFields": [],
          "maxConcurrency": 4,
          "defaultTimeout": "00:05:00",
          "maxNodeIterations": 100
        }
        """;
        File.WriteAllText(Path.Combine(_workflowDir, "test-workflow.workflow.json"), workflowJson);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.StopAsync();
        _host?.Dispose();

        if (Directory.Exists(_workflowDir))
            Directory.Delete(_workflowDir, recursive: true);
    }

    private HttpClient CreateTestClient(Action<SpectraBuilder>? configureSpectra = null)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSpectra(spectra =>
                    {
                        spectra.AddStep(new TestEchoStep());
                        spectra.AddInMemoryCheckpoints();
                        spectra.AddWorkflowsFromDirectory(_workflowDir);
                        configureSpectra?.Invoke(spectra);
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSpectra();
                    });
                });
            })
            .Build();

        _host.Start();
        return _host.GetTestClient();
    }

    // ── POST /spectra/run ────────────────────────────────────────

    [Fact]
    public async Task Run_WithValidWorkflowId_ReturnsSuccess()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/run", new
        {
            workflowId = "test-workflow"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("runId").GetString()));
        Assert.Equal("test-workflow", body.GetProperty("workflowId").GetString());
    }

    [Fact]
    public async Task Run_WithInputs_PassesToWorkflowState()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/run", new
        {
            workflowId = "test-workflow",
            inputs = new Dictionary<string, object> { ["task"] = "hello" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Run_WithUnknownWorkflowId_ReturnsBadRequest()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/run", new
        {
            workflowId = "non-existent"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_WithNullBody_ReturnsBadRequest()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsync("/spectra/run",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Run_WithInlineWorkflow_ExecutesSuccessfully()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/run", new
        {
            workflow = new
            {
                id = "inline-test",
                name = "Inline Test",
                version = 1,
                entryNodeId = "step1",
                nodes = new[]
                {
                    new { id = "step1", stepType = "echo", parameters = new { message = "inline!" } }
                },
                edges = Array.Empty<object>(),
                agents = Array.Empty<object>(),
                stateFields = Array.Empty<object>()
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    // ── GET /spectra/checkpoints/{runId} ─────────────────────────

    [Fact]
    public async Task Checkpoints_AfterRun_ReturnsCheckpoint()
    {
        using var client = CreateTestClient();

        // Run a workflow first to create a checkpoint
        var runResponse = await client.PostAsJsonAsync("/spectra/run", new
        {
            workflowId = "test-workflow"
        });
        var runBody = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = runBody.GetProperty("runId").GetString()!;

        // Inspect checkpoint
        var response = await client.GetAsync($"/spectra/checkpoints/{runId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(runId, body.GetProperty("runId").GetString());
        Assert.Equal("test-workflow", body.GetProperty("workflowId").GetString());
        Assert.Equal("Completed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Checkpoints_UnknownRunId_ReturnsNotFound()
    {
        using var client = CreateTestClient();

        var response = await client.GetAsync("/spectra/checkpoints/non-existent-run");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /spectra/stream ──────────────────────────────────────

    [Fact]
    public async Task Stream_WithValidWorkflowId_ReturnsSseStream()
    {
        using var client = CreateTestClient();

        var response = await client.GetAsync(
            "/spectra/stream?workflowId=test-workflow&mode=Updates",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        // Read the stream content — workflow is short so it completes quickly
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("event:", content);
        Assert.Contains("data:", content);
    }

    [Fact]
    public async Task Stream_WithUnknownWorkflowId_ReturnsBadRequest()
    {
        using var client = CreateTestClient();

        var response = await client.GetAsync("/spectra/stream?workflowId=non-existent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST /spectra/fork/{runId} ───────────────────────────────

    [Fact]
    public async Task Fork_WithUnknownWorkflow_ReturnsBadRequest()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/fork/some-run", new
        {
            workflowId = "non-existent",
            checkpointIndex = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST /spectra/interrupt/{runId} ──────────────────────────

    [Fact]
    public async Task Interrupt_WithUnknownWorkflow_ReturnsBadRequest()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/interrupt/some-run", new
        {
            workflowId = "non-existent",
            approved = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Custom Prefix ────────────────────────────────────────────

    [Fact]
    public async Task MapSpectra_WithCustomPrefix_UsesPrefix()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSpectra(spectra =>
                    {
                        spectra.AddStep(new TestEchoStep());
                        spectra.AddInMemoryCheckpoints();
                        spectra.AddWorkflowsFromDirectory(_workflowDir);
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSpectra("/api/workflows");
                    });
                });
            })
            .Build();

        await host.StartAsync();
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/workflows/run", new
        {
            workflowId = "test-workflow"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await host.StopAsync();
        host.Dispose();
    }

    // ── Response Shape ───────────────────────────────────────────

    [Fact]
    public async Task Run_ResponseContainsExpectedFields()
    {
        using var client = CreateTestClient();

        var response = await client.PostAsJsonAsync("/spectra/run", new
        {
            workflowId = "test-workflow"
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Verify all expected fields are present
        Assert.True(body.TryGetProperty("runId", out _));
        Assert.True(body.TryGetProperty("workflowId", out _));
        Assert.True(body.TryGetProperty("success", out _));
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.True(body.TryGetProperty("artifacts", out _));
        Assert.True(body.TryGetProperty("context", out _));
    }

    // ── No Checkpoint Store ──────────────────────────────────────

    [Fact]
    public async Task Checkpoints_WithoutStore_ReturnsBadRequest()
    {
        // Build a host WITHOUT checkpoint store
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSpectra(spectra =>
                    {
                        spectra.AddStep(new TestEchoStep());
                        // No AddInMemoryCheckpoints()
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSpectra();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/spectra/checkpoints/some-run");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await host.StopAsync();
        host.Dispose();
    }

    // ── Test Doubles ─────────────────────────────────────────────

    private sealed class TestEchoStep : IStep
    {
        public string StepType => "echo";

        public Task<StepResult> ExecuteAsync(StepContext context)
        {
            var message = context.Inputs.TryGetValue("message", out var msg)
                ? msg?.ToString() ?? "(null)"
                : "(no message)";

            return Task.FromResult(new StepResult
            {
                Status = StepStatus.Succeeded,
                Outputs = new Dictionary<string, object?> { ["message"] = message }
            });
        }
    }
}