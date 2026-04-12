using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Prompts;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in step that performs a single LLM completion.
/// Supports prompt registry resolution, inline prompts, multi-turn messages,
/// multimodal content (images), and streaming.
/// </summary>
public class PromptStep : IStep
{
    private readonly IProviderRegistry _providerRegistry;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IPromptRegistry? _promptRegistry;
    private readonly PromptRenderer _promptRenderer;
    private readonly IFallbackPolicyRegistry? _fallbackPolicyRegistry;
    private readonly IEventSink? _eventSink;

    public string StepType => "prompt";

    public PromptStep(
        IProviderRegistry providerRegistry,
        IAgentRegistry agentRegistry,
        PromptRenderer promptRenderer,
        IPromptRegistry? promptRegistry = null,
        IFallbackPolicyRegistry? fallbackPolicyRegistry = null,
        IEventSink? eventSink = null)
    {
        _providerRegistry = providerRegistry;
        _agentRegistry = agentRegistry;
        _promptRenderer = promptRenderer;
        _promptRegistry = promptRegistry;
        _fallbackPolicyRegistry = fallbackPolicyRegistry;
        _eventSink = eventSink;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        // 1. Resolve the LLM client
        var (client, clientError) = ResolveClient(context);
        if (client is null)
            return StepResult.Fail(clientError ?? "Failed to resolve LLM client.");

        // 2. Resolve the agent definition (may be null for ad-hoc provider+model)
        var agent = TryGetAgent(context);

        // 3. Build the system prompt
        var systemPrompt = ResolveSystemPrompt(context, agent);

        // 4. Build the message list
        var messages = BuildMessages(context);

        // 5. Build the LLM request
        var request = BuildRequest(context, agent, messages, systemPrompt);

        // 6. Execute (streaming or standard)
        try
        {
            LlmResponse response;

            if (context.IsStreaming && client is ILlmStreamClient streamClient)
            {
                response = await ExecuteStreamingAsync(
                    streamClient, request, context.OnToken!, context.CancellationToken);
            }
            else
            {
                response = await client.CompleteAsync(request, context.CancellationToken);
            }

            // 7. Map result
            if (!response.Success)
                return StepResult.Fail(response.ErrorMessage ?? "LLM request failed.");

            var outputs = new Dictionary<string, object?>
            {
                ["response"] = response.Content,
                ["model"] = response.Model,
                ["inputTokens"] = response.InputTokens,
                ["outputTokens"] = response.OutputTokens,
                ["latency"] = response.Latency,
                ["stopReason"] = response.StopReason
            };

            if (response.HasToolCalls)
                outputs["toolCalls"] = response.ToolCalls;

            return StepResult.Success(outputs);
        }
        catch (OperationCanceledException)
        {
            return StepResult.Fail("LLM request was cancelled.");
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"LLM request failed: {ex.Message}", ex);
        }
    }

    private (ILlmClient? Client, string? Error) ResolveClient(StepContext context)
    {
        return LlmClientResolver.ResolveClientWithFallback(
            context, _agentRegistry, _providerRegistry, _fallbackPolicyRegistry, _eventSink);
    }

    private AgentDefinition? TryGetAgent(StepContext context)
    {
        return LlmClientResolver.TryGetAgent(context, _agentRegistry);
    }

    private string? ResolveSystemPrompt(StepContext context, AgentDefinition? agent)
    {
        return LlmClientResolver.ResolveSystemPrompt(context, agent, _promptRegistry, _promptRenderer);
    }

    private List<LlmMessage> BuildMessages(StepContext context)
    {
        // If pre-built messages are provided, use them directly
        if (context.Inputs.TryGetValue("messages", out var msgObj) && msgObj is IEnumerable<LlmMessage> messages)
            return messages.ToList();

        var result = new List<LlmMessage>();

        // User prompt resolution: inline → userPromptRef → node.UserPromptRef
        var userPrompt = GetStringInput(context, "userPrompt");

        if (string.IsNullOrEmpty(userPrompt))
        {
            // Try userPromptRef from inputs
            var userPromptRef = GetStringInput(context, "userPromptRef");
            if (!string.IsNullOrEmpty(userPromptRef) && _promptRegistry is not null)
            {
                var template = _promptRegistry.GetPrompt(userPromptRef);
                if (template is not null)
                    userPrompt = RenderTemplate(template.Content, context);
            }
        }

        if (string.IsNullOrEmpty(userPrompt))
            return result;

        var renderedUser = RenderTemplate(userPrompt, context);

        // Check for multimodal content (images)
        var images = ResolveImages(context);

        if (images.Count > 0)
        {
            var contentParts = new List<LlmMessage.MediaContent>
            {
                LlmMessage.MediaContent.FromText(renderedUser)
            };
            contentParts.AddRange(images);

            result.Add(new LlmMessage
            {
                Role = LlmRole.User,
                ContentParts = contentParts
            });
        }
        else
        {
            result.Add(LlmMessage.FromText(LlmRole.User, renderedUser));
        }

        return result;
    }

    private static List<LlmMessage.MediaContent> ResolveImages(StepContext context)
    {
        if (!context.Inputs.TryGetValue("images", out var imagesObj) || imagesObj is null)
            return [];

        var result = new List<LlmMessage.MediaContent>();

        if (imagesObj is IEnumerable<Dictionary<string, object?>> imageList)
        {
            foreach (var img in imageList)
            {
                var data = img.TryGetValue("data", out var d) ? d as string : null;
                var mimeType = img.TryGetValue("mimeType", out var mt) ? mt as string : "image/png";
                if (!string.IsNullOrEmpty(data))
                    result.Add(LlmMessage.MediaContent.FromImage(data, mimeType!));
            }
        }

        return result;
    }

    private static LlmRequest BuildRequest(
        StepContext context,
        AgentDefinition? agent,
        List<LlmMessage> messages,
        string? systemPrompt)
    {
        var model = agent?.Model
            ?? (context.Inputs.TryGetValue("model", out var m) ? m as string : null)
            ?? "unknown";

        var outputModeStr = context.Inputs.TryGetValue("outputMode", out var om) ? om as string : null;
        var outputMode = outputModeStr switch
        {
            "json" => LlmOutputMode.Json,
            "structured_json" => LlmOutputMode.StructuredJson,
            _ => LlmOutputMode.Text
        };

        return new LlmRequest
        {
            Model = model,
            Messages = messages,
            SystemPrompt = systemPrompt,
            Temperature = GetDoubleInput(context, "temperature", agent?.Temperature ?? 0.7),
            MaxTokens = GetIntInput(context, "maxTokens", agent?.MaxTokens ?? 2048),
            OutputMode = outputMode,
            JsonSchema = context.Inputs.TryGetValue("jsonSchema", out var js) ? js as string : null,
            SkipCache = context.Inputs.TryGetValue("skipCache", out var sc) && sc is true
        };
    }

    private static async Task<LlmResponse> ExecuteStreamingAsync(
        ILlmStreamClient streamClient,
        LlmRequest request,
        Func<string, CancellationToken, Task> onToken,
        CancellationToken cancellationToken)
    {
        var accumulated = new System.Text.StringBuilder();

        await foreach (var chunk in streamClient.StreamAsync(request, cancellationToken))
        {
            accumulated.Append(chunk);
            await onToken(chunk, cancellationToken);
        }

        return new LlmResponse
        {
            Content = accumulated.ToString(),
            Success = true,
            Model = request.Model
        };
    }

    private string RenderTemplate(string template, StepContext context)
    {
        return LlmClientResolver.RenderTemplate(template, context, _promptRenderer);
    }

    private static string? GetStringInput(StepContext context, string key)
        => LlmClientResolver.GetStringInput(context, key);

    private static double GetDoubleInput(StepContext context, string key, double fallback)
        => LlmClientResolver.GetDoubleInput(context, key, fallback);

    private static int GetIntInput(StepContext context, string key, int fallback)
        => LlmClientResolver.GetIntInput(context, key, fallback);
}