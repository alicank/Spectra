using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Extensions.Providers.Shared;

namespace Spectra.Extensions.Providers.Gemini;

public sealed class GeminiClient : ILlmStreamClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly GeminiConfig _config;
    private readonly string _modelOverride;

    public string ProviderName => _config.ProviderName;
    public string ModelId => _modelOverride;

    public ModelCapabilities Capabilities { get; }

    internal GeminiClient(HttpClient http, GeminiConfig config, string? modelOverride = null)
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
        var body = GeminiRequestMapper.Map(EnsureModel(request));

        var sw = Stopwatch.StartNew();
        using var httpReq = BuildRequest(body, streaming: false);
        using var httpRes = await _http.SendAsync(httpReq, cancellationToken);

        var json = await httpRes.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        if (!httpRes.IsSuccessStatusCode)
            return LlmResponse.Error($"HTTP {(int)httpRes.StatusCode}: {json}");

        return GeminiResponseMapper.MapCompletion(json, sw.Elapsed);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = GeminiRequestMapper.Map(EnsureModel(request));

        using var httpReq = BuildRequest(body, streaming: true);
        using var httpRes = await _http.SendAsync(
            httpReq,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        httpRes.EnsureSuccessStatusCode();

        var stream = await httpRes.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var chunk in SseReader.ReadAsync(stream, cancellationToken))
        {
            var delta = GeminiResponseMapper.ExtractStreamDelta(chunk);
            if (delta is not null)
                yield return delta;
        }
    }

    private HttpRequestMessage BuildRequest(JsonObject body, bool streaming)
    {
        var method = streaming ? "streamGenerateContent?alt=sse" : "generateContent";
        var baseUrl = _config.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/models/{_modelOverride}:{method}";

        if (_config.ApiKey is not null)
        {
            var separator = url.Contains('?') ? "&" : "?";
            url += $"{separator}key={_config.ApiKey}";
        }

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