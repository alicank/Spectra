// Spectra MultiAgentSupervisor Sample
// A supervisor agent delegates research and writing to specialist workers,
// then synthesizes their outputs into a final RFP response.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
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
            // --- Provider ---
            spectra.AddOpenRouter(cfg =>
            {
                cfg.ApiKey = apiKey;
                cfg.Model = "openai/gpt-4o-mini";
            });

            // --- Agents ---

            // Supervisor: orchestrates the work, delegates to researcher and writer
            spectra.AddAgent("supervisor", "openrouter", "openai/gpt-4o-mini", agent => agent
                .WithSystemPrompt("""
                    You are an RFP response coordinator. Your job is to produce a compelling
                    proposal response by delegating to your specialist workers.

                    You have two workers:
                    - "researcher": Investigates the client's industry, competitors, and needs.
                    - "writer": Drafts polished proposal sections from research findings.

                    Workflow:
                    1. Delegate research to the researcher with a clear brief.
                    2. Once research is back, delegate writing to the writer with the findings.
                    3. Synthesize both outputs into a final executive summary.

                    Always delegate — do NOT attempt research or writing yourself.
                    After both workers return, produce your final consolidated response.
                    """)
                .AsSupervisor("researcher", "writer")
                .WithDelegationPolicy(DelegationPolicy.Allowed)
                .WithMaxTokens(1024));

            // Researcher: gathers industry context and competitive intelligence
            spectra.AddAgent("researcher", "openrouter", "openai/gpt-4o-mini", agent => agent
                .WithSystemPrompt("""
                    You are a business research analyst. When given a research brief,
                    provide concise, factual findings about the client's industry,
                    competitive landscape, and key challenges. Keep your response
                    under 300 words and focus on actionable intelligence.
                    """)
                .WithMaxTokens(512));

            // Writer: drafts proposal sections from research
            spectra.AddAgent("writer", "openrouter", "openai/gpt-4o-mini", agent => agent
                .WithSystemPrompt("""
                    You are a professional proposal writer. When given research findings
                    and a writing brief, draft a polished proposal section that is
                    persuasive, client-focused, and under 300 words.
                    Use clear headings and bullet points where appropriate.
                    """)
                .WithMaxTokens(512));

            spectra.AddInMemoryCheckpoints();
            spectra.AddConsoleEvents();
        });
    })
    .Build();

// --- Build the workflow in code ---
var workflow = WorkflowBuilder.Create("rfp-response")
    .WithName("RFP Response Generator")
    .AddAgent("supervisor", "openrouter", "openai/gpt-4o-mini", agent => agent
        .WithSystemPrompt("You are an RFP coordinator. Delegate to researcher and writer.")
        .AsSupervisor("researcher", "writer")
        .WithDelegationPolicy(DelegationPolicy.Allowed))
    .AddAgentNode("coordinate", "supervisor", node => node
        .WithUserPrompt("""
            We received an RFP from {{inputs.client}}.

            RFP Summary:
            {{inputs.rfpSummary}}

            Produce a complete proposal response by:
            1. Delegating industry research to the researcher
            2. Delegating proposal drafting to the writer (include the research findings)
            3. Synthesizing everything into a final executive summary
            """)
        .WithMaxIterations(10))
    .Build();

// --- Run ---
var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["client"] = "Meridian Healthcare Group";
state.Inputs["rfpSummary"] = """
    Meridian Healthcare Group is seeking a technology partner to modernize their
    patient records system. They need cloud migration, HIPAA-compliant data handling,
    real-time analytics dashboards, and integration with existing EHR systems.
    Budget: $2-5M. Timeline: 18 months. Decision by Q3.
    """;

Console.WriteLine("═══ RFP Response Generator — Multi-Agent Supervisor ═══");
Console.WriteLine($"Client: {state.Inputs["client"]}");
Console.WriteLine();

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();
Console.WriteLine("═══ FINAL RESULT ═══");

if (result.Context.TryGetValue("coordinate", out var coordOutput) && coordOutput is IDictionary<string, object?> outputs)
{
    if (outputs.TryGetValue("response", out var response))
    {
        Console.WriteLine(response);
    }

    if (outputs.TryGetValue("iterations", out var iterations))
        Console.WriteLine($"\nTotal LLM iterations: {iterations}");

    if (outputs.TryGetValue("totalInputTokens", out var inTok) &&
        outputs.TryGetValue("totalOutputTokens", out var outTok))
        Console.WriteLine($"Tokens: {inTok} input + {outTok} output");
}

Console.WriteLine($"\nErrors: {result.Errors.Count}");
foreach (var e in result.Errors)
    Console.WriteLine($"  - {e}");