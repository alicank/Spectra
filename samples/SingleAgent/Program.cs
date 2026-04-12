// Spectra SingleAgent Sample
// An autonomous agent with two custom tools (calculator + clock).
// The agent reasons about a question, calls tools in a loop, and synthesizes an answer.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Providers;
using Spectra.Contracts.State;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Registration;
using Spectra.Workflow;

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException(
        "Set OPENROUTER_API_KEY environment variable before running this sample.");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(config =>
            {
                config.ApiKey = apiKey;
                config.Model = "openai/gpt-4o-mini";
            });

            // Register custom tools — the agent can discover and call these
            spectra.AddTool(new CalculatorTool());
            spectra.AddTool(new ClockTool());

            spectra.AddConsoleEvents();
        });
    })
    .Build();

var workflow = WorkflowBuilder.Create("smart-assistant")
    .WithName("Smart Assistant with Tools")
    .AddAgent("assistant", "openrouter", "openai/gpt-4o-mini", agent => agent
        .WithSystemPrompt("""
            You are a helpful assistant with access to tools.
            Use the calculator for any math operations — never do arithmetic in your head.
            Use the clock to get the current time when asked.
            Think step by step: break complex questions into tool calls, observe results, then synthesize a final answer.
            """)
        .WithMaxTokens(1024))
    .AddNode("solve", "agent", node => node
        .WithParameter("agentId", "assistant")
        .WithParameter("userPrompt", "{{inputs.question}}")
        .WithParameter("tools", new[] { "calculator", "clock" })
        .WithParameter("maxIterations", 10))
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["question"] = """
    I'm planning a dinner party for 7 people. Each person eats about 350 grams of food.
    I need to buy ingredients for 1.5x that amount to be safe.
    How many kilograms of food should I buy in total?
    Also, what time is it right now — do I still have time to go shopping today?
    """;

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();

if (result.Context.TryGetValue("solve", out var output)
    && output is IDictionary<string, object?> dict)
{
    if (dict.TryGetValue("response", out var response))
        Console.WriteLine($"Agent answer:\n{response}");

    if (dict.TryGetValue("iterations", out var iterations))
        Console.WriteLine($"\nTool-call iterations: {iterations}");

    if (dict.TryGetValue("stopReason", out var stopReason))
        Console.WriteLine($"Stop reason: {stopReason}");
}

Console.WriteLine($"Errors: {result.Errors.Count}");

// ─── Custom Tools ────────────────────────────────────────────────────

/// <summary>
/// A basic calculator that evaluates arithmetic expressions.
/// Supports: +, -, *, / on two operands.
/// </summary>
public class CalculatorTool : ITool
{
    public string Name => "calculator";

    public ToolDefinition Definition => new()
    {
        Name = "calculator",
        Description = "Evaluate a simple arithmetic operation on two numbers. Use this for any math — never calculate in your head.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "a",
                Type = "number",
                Description = "First operand",
                Required = true
            },
            new ToolParameter
            {
                Name = "b",
                Type = "number",
                Description = "Second operand",
                Required = true
            },
            new ToolParameter
            {
                Name = "operation",
                Type = "string",
                Description = "Operation to perform: add, subtract, multiply, divide",
                Required = true
            }
        ]
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        if (!TryGetNumber(arguments, "a", out var a))
            return Task.FromResult(ToolResult.Fail("Parameter 'a' is required and must be a number."));

        if (!TryGetNumber(arguments, "b", out var b))
            return Task.FromResult(ToolResult.Fail("Parameter 'b' is required and must be a number."));

        var op = arguments.GetValueOrDefault("operation")?.ToString()?.ToLowerInvariant() ?? "";

        var result = op switch
        {
            "add" => $"{a} + {b} = {a + b}",
            "subtract" => $"{a} - {b} = {a - b}",
            "multiply" => $"{a} * {b} = {a * b}",
            "divide" when b == 0 => null,
            "divide" => $"{a} / {b} = {a / b}",
            _ => null
        };

        return Task.FromResult(result is not null
            ? ToolResult.Ok(result)
            : ToolResult.Fail(op == "divide" ? "Cannot divide by zero." : $"Unknown operation '{op}'. Use: add, subtract, multiply, divide."));
    }

    private static bool TryGetNumber(Dictionary<string, object?> args, string key, out double value)
    {
        value = 0;
        if (!args.TryGetValue(key, out var raw) || raw is null) return false;
        return double.TryParse(raw.ToString(), out value);
    }
}

/// <summary>
/// Returns the current local date and time.
/// </summary>
public class ClockTool : ITool
{
    public string Name => "clock";

    public ToolDefinition Definition => new()
    {
        Name = "clock",
        Description = "Get the current local date and time. Use this whenever the user asks about time or scheduling.",
        Parameters = [] // No parameters needed
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var result = $"Current date/time: {now:dddd, MMMM d, yyyy — HH:mm:ss} (local time zone)";
        return Task.FromResult(ToolResult.Ok(result));
    }
}