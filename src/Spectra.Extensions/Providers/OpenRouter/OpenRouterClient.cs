using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Spectra.Contracts.Providers;
using Spectra.Extensions.Providers.OpenAiCompatible;
using Spectra.Extensions.Providers.Shared;

namespace Spectra.Extensions.Providers.OpenRouter;

public sealed class OpenRouterClient : ILlmStreamClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly OpenRouterConfig _config;
    private readonly string _modelOverride;

    public string ProviderName => _config.ProviderName;
    public string ModelId => _modelOverride;

    public ModelCapabilities Capabilities { get; }

    internal OpenRouterClient(HttpClient http, OpenRouterConfig config, string? modelOverride = null)
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
        var body = OpenAiRequestMapper.Map(EnsureModel(request));

        var sw = Stopwatch.StartNew();
        using var httpReq = BuildRequest(body);
        using var httpRes = await _http.SendAsync(httpReq, cancellationToken);

        var json = await httpRes.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        if (!httpRes.IsSuccessStatusCode)
            return LlmResponse.Error($"HTTP {(int)httpRes.StatusCode}: {json}");

        return OpenAiResponseMapper.MapCompletion(json, sw.Elapsed);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = OpenAiRequestMapper.Map(EnsureModel(request));
        body["stream"] = true;

        using var httpReq = BuildRequest(body);
        using var httpRes = await _http.SendAsync(
            httpReq,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        httpRes.EnsureSuccessStatusCode();

        var stream = await httpRes.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var chunk in SseReader.ReadAsync(stream, cancellationToken))
        {
            var delta = OpenAiResponseMapper.ExtractStreamDelta(chunk);
            if (delta is not null)
                yield return delta;
        }
    }

    private HttpRequestMessage BuildRequest(JsonObject body)
    {
        var url = $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";

        var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };

        if (_config.ApiKey is not null)
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

        if (_config.SiteUrl is not null)
            msg.Headers.Add("HTTP-Referer", _config.SiteUrl);

        if (_config.SiteName is not null)
            msg.Headers.Add("X-Title", _config.SiteName);

        return msg;
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