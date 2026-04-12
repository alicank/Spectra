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
                config.Model = "anthropic/claude-haiku-4.5";
            });

            spectra.AddConsoleEvents();
        });
    })
    .Build();

// JSON Schema — used as guidance in the prompt, validated client-side
var contactSchema = """
    {
      "type": "object",
      "properties": {
        "name":    { "type": "string" },
        "company": { "type": "string" },
        "email":   { "type": "string" },
        "phone":   { "type": "string" },
        "role":    { "type": "string" }
      },
      "required": ["name", "company", "email"]
    }
    """;

var workflow = WorkflowBuilder.Create("extract-contact")
    .WithName("Extract Contact Info")
    .AddAgent("extractor", "openrouter", "anthropic/claude-haiku-4.5", agent => agent
        .WithSystemPrompt("You extract structured contact information from text.")
        .WithMaxTokens(300))
    .AddNode("extract", "structured_output", node => node
        .WithParameter("agentId", "extractor")
        .WithParameter("userPrompt", "Extract the contact information from this text:\n\n{{inputs.text}}")
        .WithParameter("jsonSchema", contactSchema))
    .Build();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["text"] = """
    Hi, I'm Marie Dupont from Acme Insurance. You can reach me at 
    marie.dupont@acme-insurance.fr or call +33 4 78 00 12 34. 
    I'm the Claims Director and I'd love to discuss the new platform.
    """;

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();

// Outputs are a flat dictionary — direct access to extracted fields
if (result.Context.TryGetValue("extract", out var output)
    && output is IDictionary<string, object?> contact)
{
    Console.WriteLine("Extracted contact:");
    foreach (var (key, value) in contact)
        Console.WriteLine($"  {key,-10}: {value}");
}

Console.WriteLine($"\nErrors: {result.Errors.Count}");