using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;

namespace Spectra.Extensions.Providers.Ollama;

public sealed class OllamaClient : ILlmStreamClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly OllamaConfig _config;
    private readonly string _modelOverride;

    public string ProviderName => _config.ProviderName;
    public string ModelId => _modelOverride;

    public ModelCapabilities Capabilities { get; }

    internal OllamaClient(HttpClient http, OllamaConfig config, string? modelOverride = null)
    {
        _http = http;
        _config = config;
        _modelOverride = modelOverride ?? config.Model;

        Capabilities = new ModelCapabilities
        {
            SupportsJsonMode = config.Capabilities.SupportsJsonMode,
            SupportsToolCalling = config.Capabilities.SupportsToolCalling,
            SupportsVision = config.Capabilities.SupportsVision,
            SupportsAudio = config.Capabilities.SupportsAudio,
            SupportsVideo = config.Capabilities.SupportsVideo,
            SupportsStreaming = config.Capabilities.SupportsStreaming,
            MaxContextTokens = config.Capabilities.MaxContextTokens,
            MaxOutputTokens = config.Capabilities.MaxOutputTokens
        };
    }

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = OllamaRequestMapper.Map(EnsureModel(request), _config);
        body["stream"] = false;

        var sw = Stopwatch.StartNew();
        using var httpReq = BuildRequest(body);
        using var httpRes = await _http.SendAsync(httpReq, cancellationToken);

        var json = await httpRes.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        if (!httpRes.IsSuccessStatusCode)
            return LlmResponse.Error($"HTTP {(int)httpRes.StatusCode}: {json}");

        return OllamaResponseMapper.MapCompletion(json, sw.Elapsed);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = OllamaRequestMapper.Map(EnsureModel(request), _config);
        body["stream"] = true;

        using var httpReq = BuildRequest(body);
        using var httpRes = await _http.SendAsync(
            httpReq,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        httpRes.EnsureSuccessStatusCode();

        var stream = await httpRes.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Ollama streams newline-delimited JSON (not SSE)
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                yield break;

            if (line.Length == 0)
                continue;

            if (OllamaResponseMapper.IsStreamDone(line))
                yield break;

            var delta = OllamaResponseMapper.ExtractStreamDelta(line);
            if (delta is not null)
                yield return delta;
        }
    }

    private HttpRequestMessage BuildRequest(JsonObject body)
    {
        var url = $"{_config.Host.TrimEnd('/')}/api/chat";

        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };
    }

    private LlmRequest EnsureModel(LlmRequest request)
    {
        if (request.Model == _modelOverride)
            return request;

        return new LlmRequest
        {
            Model = _modelOverride,
            Messages = request.Messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            StopSequence = request.StopSequence,
            OutputMode = request.OutputMode,
            JsonSchema = request.JsonSchema,
            SystemPrompt = request.SystemPrompt,
            Tools = request.Tools
        };
    }

    public void Dispose() => _http.Dispose();
}