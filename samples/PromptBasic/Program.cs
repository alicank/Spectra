using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
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
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var workflow = WorkflowBuilder.Create("summarize")
    .WithName("Summarize Text")
    .AddAgent("assistant", "openrouter", "google/gemini-2.5-flash", agent => agent
        .WithSystemPrompt("You are a concise summarizer. Respond in 2-3 sentences max.")
        .WithMaxTokens(200))
    .AddNode("summarize", "prompt", node => node
        .WithParameter("agentId", "assistant")
        .WithParameter("userPrompt", "Summarize this text:\n\n{{inputs.text}}"))
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["text"] = """
    Spectra is an open-source .NET framework for orchestrating AI workflows as explicit graphs.
    You define workflows in C# or JSON, mix code steps and AI steps, choose providers per step,
    and keep the whole flow visible. It supports sequential, parallel, and cyclic execution,
    checkpointing with resume, human-in-the-loop interrupts, multi-agent handoffs,
    long-term memory, MCP tool integration, and native streaming.
    """;

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();

if (result.Context.TryGetValue("summarize", out var output)
    && output is IDictionary<string, object?> dict
    && dict.TryGetValue("response", out var response))
{
    Console.WriteLine($"Summary: {response}");
}

Console.WriteLine($"Errors: {result.Errors.Count}");