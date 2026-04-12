// Spectra StreamingOutput Sample
// Streams LLM tokens to the console in real-time using runner.StreamAsync().

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Streaming;
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
                config.Model = "mistralai/mistral-medium-3.1";
            });
            spectra.AddConsoleEvents();
        });
    })
    .Build();

var workflow = WorkflowBuilder.Create("stream-demo")
    .WithName("Streaming Demo")
    .AddAgent("writer", "openrouter", "mistralai/mistral-medium-3.1", agent => agent
        .WithSystemPrompt("You are a technical writer. Explain concepts clearly and concisely.")
        .WithMaxTokens(300))
    .AddNode("explain", "prompt", node => node
        .WithParameter("agentId", "writer")
        .WithParameter("userPrompt", "Explain what graph-based AI orchestration is and why it matters:\n\n{{inputs.topic}}"))
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["topic"] = """
    Modern AI applications combine multiple LLM calls, tool invocations, and branching logic.
    Graph-based orchestration treats each step as a node and each dependency as an edge,
    making the entire flow explicit, observable, and reproducible.
    """;

Console.WriteLine("--- Streaming tokens ---\n");

var tokenCount = 0;
var stepCount = 0;

await foreach (var evt in runner.StreamAsync(workflow, StreamMode.Tokens, state))
{
    switch (evt)
    {
        case TokenStreamEvent token:
            Console.Write(token.Token);
            tokenCount++;
            break;

        case StepCompletedEvent step:
            stepCount++;
            Console.WriteLine($"\n\n[{step.NodeId}] completed in {step.Duration}");
            break;

        case WorkflowCompletedEvent done:
            Console.WriteLine($"\nWorkflow finished — success: {done.Success}");
            break;
    }
}

Console.WriteLine($"\nTokens streamed: {tokenCount} | Steps completed: {stepCount}");