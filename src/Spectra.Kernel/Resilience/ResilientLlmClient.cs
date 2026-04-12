using System.Text.RegularExpressions;
using Spectra.Contracts.Providers;

namespace Spectra.Kernel.Resilience;

/// <summary>
/// Decorator that adds timeout and retry behaviour to any <see cref="ILlmClient"/>.
/// Transient failures (timeouts, HTTP 429/5xx, network errors) are retried with
/// configurable exponential back-off and jitter.
/// </summary>
public sealed partial class ResilientLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly LlmResilienceOptions _options;

    public string ProviderName => _inner.ProviderName;
    public string ModelId => _inner.ModelId;
    public ModelCapabilities Capabilities => _inner.Capabilities;

    public ResilientLlmClient(ILlmClient inner, LlmResilienceOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new LlmResilienceOptions();
    }

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var maxAttempts = 1 + _options.MaxRetries;
        LlmResponse? lastResponse = null;
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
                await DelayBeforeRetry(attempt, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CreateTimeoutCts(cancellationToken);
                var token = timeoutCts?.Token ?? cancellationToken;

                lastResponse = await _inner.CompleteAsync(request, token);

                if (lastResponse.Success)
                    return lastResponse;

                // Non-retryable error — fail fast
                if (!IsRetryableResponse(lastResponse))
                    return lastResponse;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout on this attempt — treat as transient
                lastException = new TimeoutException(
                    $"LLM call timed out after {_options.Timeout.TotalSeconds}s (attempt {attempt + 1}/{maxAttempts})");
            }
            catch (HttpRequestException ex)
            {
                // Network-level failure — transient
                lastException = ex;
            }
        }

        // All attempts exhausted
        if (lastResponse is not null)
            return lastResponse;

        return LlmResponse.Error(
            $"All {maxAttempts} attempts failed. Last error: {lastException?.Message ?? "unknown"}");
    }

    private CancellationTokenSource? CreateTimeoutCts(CancellationToken cancellationToken)
    {
        if (_options.Timeout == Timeout.InfiniteTimeSpan)
            return null;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.Timeout);
        return cts;
    }

    private async Task DelayBeforeRetry(int attempt, CancellationToken cancellationToken)
    {
        var delay = _options.UseExponentialBackoff
            ? ComputeExponentialDelay(attempt)
            : _options.BaseDelay;

        if (delay > _options.MaxDelay)
            delay = _options.MaxDelay;

        await Task.Delay(delay, cancellationToken);
    }

    private TimeSpan ComputeExponentialDelay(int attempt)
    {
        // 2^(attempt-1) * baseDelay + random jitter up to 25%
        var factor = Math.Pow(2, attempt - 1);
        var baseMs = _options.BaseDelay.TotalMilliseconds * factor;
        var jitter = baseMs * 0.25 * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    private bool IsRetryableResponse(LlmResponse response)
    {
        if (response.ErrorMessage is null)
            return false;

        var match = HttpStatusCodePattern().Match(response.ErrorMessage);
        if (!match.Success)
            return false;

        var statusCode = int.Parse(match.Groups[1].Value);
        return _options.RetryableStatusCodes.Contains(statusCode);
    }

    [GeneratedRegex(@"HTTP (\d{3})")]
    private static partial Regex HttpStatusCodePattern();
}