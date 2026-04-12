using System.Text.Json;
using System.Text.RegularExpressions;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Providers.Fallback;
using Spectra.Contracts.Steps;
using Spectra.Kernel.Prompts;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Built-in step that performs an LLM completion and parses the response as JSON.
/// Uses JSON mode (not provider-level structured output) for maximum provider compatibility,
/// then validates and normalizes the response client-side — similar to Pydantic's approach.
/// </summary>
public partial class StructuredOutputStep : IStep
{
    private readonly PromptStep _inner;

    public string StepType => "structured_output";

    public StructuredOutputStep(
        IProviderRegistry providerRegistry,
        IAgentRegistry agentRegistry,
        PromptRenderer promptRenderer,
        IPromptRegistry? promptRegistry = null,
        IFallbackPolicyRegistry? fallbackPolicyRegistry = null,
        IEventSink? eventSink = null)
    {
        _inner = new PromptStep(providerRegistry, agentRegistry, promptRenderer,
            promptRegistry, fallbackPolicyRegistry, eventSink);
    }

    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        // 1. Always use JSON mode — provider-agnostic, no schema pushed to the API
        var jsonSchema = context.Inputs.TryGetValue("jsonSchema", out var js) ? js as string : null;

        var augmentedInputs = new Dictionary<string, object?>(context.Inputs)
        {
            ["outputMode"] = "json"
        };

        // 2. If a schema is provided, inject it into the system prompt so the LLM
        //    knows the expected shape — but validation happens client-side
        if (!string.IsNullOrEmpty(jsonSchema))
        {
            var existingSystem = context.Inputs.TryGetValue("systemPrompt", out var sp)
                ? sp?.ToString() : null;

            augmentedInputs["systemPrompt"] = string.IsNullOrEmpty(existingSystem)
                ? $"Respond with valid JSON only matching this schema:\n{jsonSchema}\nNo additional text."
                : $"{existingSystem}\n\nRespond with valid JSON matching this schema:\n{jsonSchema}";
        }

        // 3. Build a new context with the augmented inputs
        var innerContext = new StepContext
        {
            RunId = context.RunId,
            WorkflowId = context.WorkflowId,
            NodeId = context.NodeId,
            State = context.State,
            CancellationToken = context.CancellationToken,
            Inputs = augmentedInputs,
            Services = context.Services,
            WorkflowDefinition = context.WorkflowDefinition,
            OnToken = context.OnToken,
            Interrupt = context.Interrupt
        };

        // 4. Delegate to PromptStep
        var result = await _inner.ExecuteAsync(innerContext);

        if (result.Status != StepStatus.Succeeded)
            return result;

        // 5. Extract the raw response
        var rawResponse = result.Outputs.TryGetValue("response", out var r) ? r as string : null;

        if (string.IsNullOrEmpty(rawResponse))
            return StepResult.Fail("Structured output step received an empty response from the LLM.");

        // 6. Extract JSON (handles ```json ... ``` wrapping)
        var json = ExtractJson(rawResponse);

        // 7. Parse and normalize to CLR types (no JsonElement leaks)
        try
        {
            using var document = JsonDocument.Parse(json);
            var normalized = NormalizeJsonElement(document.RootElement);

            // Return the parsed domain data directly as outputs
            if (normalized is Dictionary<string, object?> parsedDict)
                return StepResult.Success(parsedDict);

            // If the root is an array or primitive, wrap it
            return StepResult.Success(new Dictionary<string, object?>
            {
                ["result"] = normalized,
                ["rawResponse"] = rawResponse
            });
        }
        catch (JsonException ex)
        {
            return StepResult.Fail(
                $"LLM response is not valid JSON: {ex.Message}\nRaw content:\n{rawResponse}",
                ex,
                new Dictionary<string, object?> { ["rawResponse"] = rawResponse });
        }
    }

    /// <summary>
    /// Extracts JSON from LLM responses that may wrap it in markdown code blocks.
    /// </summary>
    private static string ExtractJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        content = content.Trim();

        // Case 1: raw JSON
        if (content.StartsWith('{') || content.StartsWith('['))
            return content;

        // Case 2: ```json ... ``` or ``` ... ```
        if (content.StartsWith("```"))
        {
            var firstBrace = content.IndexOf('{');
            var firstBracket = content.IndexOf('[');

            var start = (firstBrace, firstBracket) switch
            {
                ( >= 0, >= 0) => Math.Min(firstBrace, firstBracket),
                ( >= 0, _) => firstBrace,
                (_, >= 0) => firstBracket,
                _ => -1
            };

            if (start < 0) return content;

            var isArray = content[start] == '[';
            var lastClose = isArray ? content.LastIndexOf(']') : content.LastIndexOf('}');

            if (lastClose > start)
                return content.Substring(start, lastClose - start + 1);
        }

        return content;
    }

    /// <summary>
    /// Converts a JsonElement tree into plain CLR types so downstream steps
    /// never deal with JsonElement (which requires a living JsonDocument).
    /// </summary>
    private static object? NormalizeJsonElement(JsonElement je) =>
        je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => je.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => NormalizeJsonElement(p.Value),
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => je.EnumerateArray()
                .Select(NormalizeJsonElement)
                .ToList(),
            _ => je.ToString()
        };
}