// ═══════════════════════════════════════════════════════════════════════════════
// Spectra ResearchPipeline — The Final Boss
// ═══════════════════════════════════════════════════════════════════════════════
//
// This is the capstone sample. It combines nearly every Spectra feature into a
// single realistic workflow: a Compliance Incident Analysis Pipeline for a
// financial services firm.
//
// Features demonstrated:
//   ✓ JSON-defined workflow with agents, nodes, edges, and subgraphs
//   ✓ PromptStep — incident classification (single LLM call)
//   ✓ AgentStep — investigation with custom tools (search + query)
//   ✓ MemoryStoreStep — persist investigation findings
//   ✓ Parallel fan-out — risk + regulatory analysis run concurrently
//   ✓ WaitForAll merge — aggregate both analysis branches
//   ✓ Declarative InterruptBefore — human compliance officer review
//   ✓ SubgraphStep — isolated child workflow for CSV remediation
//   ✓ Subgraph agent with file tools — reads/writes CSV (Anthropic-style coding)
//   ✓ FileCheckpointStore — checkpoint at every node
//   ✓ Console event streaming — full observability
//
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Tools;
using Spectra.Contracts.Workflow;
using Spectra.Registration;
using System.Text.Json;

// ── Configuration ──────────────────────────────────────────────────────────────

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException(
        "Set OPENROUTER_API_KEY environment variable. Get one at https://openrouter.ai/keys");

var incidentId = "INC-2026-04-0847";
var incidentReport = """
    INCIDENT REPORT — INC-2026-04-0847
    Date: 2026-04-07
    Desk: FX-7 (Foreign Exchange Trading)
    Reporter: Automated Surveillance System
    
    Summary:
    Unusual trading pattern detected on desk FX-7. Between 09:15 and 10:35 UTC,
    six transactions totaling $21,250,000 USD were executed with counterparty
    Meridian Capital in rapid succession (average interval: 16 minutes).
    The volume exceeds the desk's normal daily counterparty concentration limit
    of $10M by 112%. Three transactions occurred within 2 minutes of each other.
    
    The pattern matches the "layering" signature in our surveillance model
    (confidence: 78%). No pre-trade approval was logged for transactions
    exceeding the $3M single-trade threshold (TXN-40294, TXN-40297, TXN-40299).
    
    Desk head (J. Morrison) was on PTO at time of execution.
    Executing trader: K. Patel (employee ID: EMP-4419).
    """;

Console.WriteLine("═══ Compliance Incident Analysis Pipeline ═══");
Console.WriteLine($"Incident: {incidentId}");
Console.WriteLine();

// ── Host & DI Setup ────────────────────────────────────────────────────────────

var checkpointDir = Path.Combine(Path.GetTempPath(), "spectra-compliance-checkpoints");
Directory.CreateDirectory(checkpointDir);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            // Primary provider
            spectra.AddOpenRouter(config =>
            {
                config.ApiKey = apiKey;
                config.Model = "openai/gpt-4o-mini";
            });

            // Custom steps for this pipeline
            spectra.AddStep(new MergeResultsStep());
            spectra.AddStep(new ReviewGateStep());

            // Custom tools for the investigator agent
            spectra.AddTool(new SearchIncidentsTool());
            spectra.AddTool(new QueryTransactionsTool());

            // File tools for the subgraph code agent
            spectra.AddTool(new ReadFileTool());
            spectra.AddTool(new WriteFileTool());

            // Memory — persist findings across nodes
            spectra.AddInMemoryMemory();

            // Checkpointing — save state at every node, checkpoint on interrupt
            spectra.AddFileCheckpoints(checkpointDir, opts =>
            {
                opts.Frequency = Spectra.Contracts.Checkpointing.CheckpointFrequency.EveryNode;
                opts.CheckpointOnInterrupt = true;
            });

            // Console events — full observability
            spectra.AddConsoleEvents();
        });
    })
    .Build();

// ── Load Workflow ──────────────────────────────────────────────────────────────

var workflowStore = new JsonFileWorkflowStore("./workflows");
var workflow = workflowStore.Get("compliance-incident-pipeline")
    ?? throw new InvalidOperationException("Could not load 'compliance-incident-pipeline' workflow.");

Console.WriteLine($"Loaded: {workflow.Name}");
Console.WriteLine($"  Nodes: {workflow.Nodes.Count}");
Console.WriteLine($"  Edges: {workflow.Edges.Count}");
Console.WriteLine($"  Agents: {workflow.Agents.Count}");
Console.WriteLine($"  Subgraphs: {workflow.Subgraphs.Count}");
Console.WriteLine();

// ── Run Phase 1: Up to the human review interrupt ──────────────────────────────

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["incidentId"] = incidentId;
state.Inputs["incidentReport"] = incidentReport;

Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine("  PHASE 1: Classification → Investigation → Analysis");
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine();

var result = await runner.RunAsync(workflow, state);

// ── Print Phase 1 Results ──────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("── Phase 1 Results ──");

if (result.Context.TryGetValue("classify", out var classifyObj)
    && classifyObj is IDictionary<string, object?> classifyDict
    && classifyDict.TryGetValue("response", out var classification))
{
    Console.WriteLine($"  Classification: {classification}");
}

if (result.Context.TryGetValue("investigate", out var investigateObj)
    && investigateObj is IDictionary<string, object?> investigateDict
    && investigateDict.TryGetValue("response", out var findings))
{
    var findingsStr = findings?.ToString() ?? "";
    Console.WriteLine($"  Findings: {(findingsStr.Length > 200 ? findingsStr[..200] + "..." : findingsStr)}");
}

if (result.Context.TryGetValue("risk-assessment", out var riskObj)
    && riskObj is IDictionary<string, object?> riskDict
    && riskDict.TryGetValue("response", out var riskResponse))
{
    var riskStr = riskResponse?.ToString() ?? "";
    Console.WriteLine($"  Risk Assessment: {(riskStr.Length > 200 ? riskStr[..200] + "..." : riskStr)}");
}

if (result.Context.TryGetValue("regulatory-impact", out var regObj)
    && regObj is IDictionary<string, object?> regDict
    && regDict.TryGetValue("response", out var regResponse))
{
    var regStr = regResponse?.ToString() ?? "";
    Console.WriteLine($"  Regulatory Impact: {(regStr.Length > 200 ? regStr[..200] + "..." : regStr)}");
}

// ── Check for interrupt ────────────────────────────────────────────────────────
// WorkflowState has no Status property. If the pipeline was interrupted, the
// final report node won't have run — we detect this by absence of its output.

if (result.Errors.Count == 0 && !result.Context.ContainsKey("generate-report"))
{
    Console.WriteLine();
    Console.WriteLine("══════════════════════════════════════════════════════════════");
    Console.WriteLine("  ⏸  PAUSED — Human Review Required");
    Console.WriteLine("══════════════════════════════════════════════════════════════");
    Console.WriteLine();

    // Simulate compliance officer review and approval
    Console.WriteLine("  [Simulating compliance officer approval...]");
    Console.WriteLine("  Reviewer: Sarah Chen (Chief Compliance Officer)");
    Console.WriteLine("  Decision: APPROVED — proceed with remediation");
    Console.WriteLine();

    // ── Phase 2: Resume after approval ─────────────────────────────────────────

    Console.WriteLine("══════════════════════════════════════════════════════════════");
    Console.WriteLine("  PHASE 2: Remediation → Report Generation");
    Console.WriteLine("══════════════════════════════════════════════════════════════");
    Console.WriteLine();

    var approval = InterruptResponse.ApprovedResponse(
        respondedBy: "sarah.chen@compliance.internal",
        comment: "Approved. Flag all Meridian Capital transactions and escalate to desk head upon return.");

    result = await runner.ResumeWithResponseAsync(workflow, result.RunId, approval);
}

// ── Final Report ───────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine("  FINAL COMPLIANCE REPORT");
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine();

if (result.Context.TryGetValue("generate-report", out var reportObj)
    && reportObj is IDictionary<string, object?> reportDict
    && reportDict.TryGetValue("response", out var report))
{
    Console.WriteLine(report);
}

// ── Summary ────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("── Pipeline Summary ──");
Console.WriteLine($"  Status: {(result.Errors.Count == 0 ? "Success" : "Completed with errors")}");
Console.WriteLine($"  Errors: {result.Errors.Count}");
foreach (var error in result.Errors)
    Console.WriteLine($"    - {error}");

// Check if CSV was modified
var csvPath = "./data/flagged-transactions.csv";
if (File.Exists(csvPath))
{
    var csvContent = await File.ReadAllTextAsync(csvPath);
    var flaggedCount = csvContent.Split('\n').Count(line => line.Contains("FLAGGED"));
    Console.WriteLine($"  Transactions flagged: {flaggedCount}");
}

// Cleanup checkpoints
try { Directory.Delete(checkpointDir, true); } catch { /* ignore */ }

Console.WriteLine();
Console.WriteLine("═══ Pipeline Complete ═══");

// ═══════════════════════════════════════════════════════════════════════════════
// Custom Steps
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Merges results from parallel analysis branches into a single context entry.
/// </summary>
public class MergeResultsStep : IStep
{
    public string StepType => "merge_results";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        var merged = new Dictionary<string, object?>();

        if (context.State.Context.TryGetValue("risk-assessment", out var riskData))
            merged["riskAssessment"] = riskData;

        if (context.State.Context.TryGetValue("regulatory-impact", out var regData))
            merged["regulatoryImpact"] = regData;

        merged["mergedAt"] = DateTimeOffset.UtcNow.ToString("o");

        Console.WriteLine("  [merge] Combined risk assessment + regulatory impact analysis");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = merged
        });
    }
}

/// <summary>
/// Passthrough step for the human review gate.
/// The actual interrupt is declarative (interruptBefore on the node).
/// </summary>
public class ReviewGateStep : IStep
{
    public string StepType => "review_gate";

    public Task<StepResult> ExecuteAsync(StepContext context)
    {
        Console.WriteLine("  [review] Human review completed — proceeding with remediation");

        return Task.FromResult(new StepResult
        {
            Status = StepStatus.Succeeded,
            Outputs = new Dictionary<string, object?>
            {
                ["reviewed"] = true,
                ["reviewedAt"] = DateTimeOffset.UtcNow.ToString("o")
            }
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Custom Tools — Investigation
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Simulates searching an internal incident database for related past incidents.
/// </summary>
public class SearchIncidentsTool : ITool
{
    public string Name => "search_incidents";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Search the internal incident database for related past incidents. Pass a query string describing what to search for.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "query",
                Type = "string",
                Description = "Search query for related incidents",
                Required = true
            }
        ]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> arguments, WorkflowState state, CancellationToken ct = default)
    {
        var query = arguments.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
        Console.WriteLine($"  [tool:search_incidents] Searching: \"{query}\"");

        var results = JsonSerializer.Serialize(new[]
        {
            new
            {
                IncidentId = "INC-2025-11-0312",
                Date = "2025-11-15",
                Desk = "FX-3",
                Type = "TRADE_SURVEILLANCE",
                Summary = "Similar concentration pattern with Meridian Capital on FX-3. Volume exceeded limits by 85%. Investigation found unauthorized algorithmic trading script. Trader K. Patel (same trader) received formal warning.",
                Resolution = "Trading script disabled, additional pre-trade controls implemented on FX-3."
            },
            new
            {
                IncidentId = "INC-2025-08-0198",
                Date = "2025-08-22",
                Desk = "FX-7",
                Type = "TRADE_SURVEILLANCE",
                Summary = "Rapid-fire execution pattern on FX-7 during low-liquidity window. Counterparty: Apex Trading. No pre-trade approvals for 4 of 7 transactions.",
                Resolution = "Pre-trade approval workflow updated. Desk limits reviewed."
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(ToolResult.Ok(results));
    }
}

/// <summary>
/// Simulates querying the transaction database for suspicious patterns.
/// </summary>
public class QueryTransactionsTool : ITool
{
    public string Name => "query_transactions";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Query the transaction database for suspicious patterns. Filter by desk, counterparty, or date.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "desk",
                Type = "string",
                Description = "Trading desk identifier (e.g., FX-7)",
                Required = true
            },
            new ToolParameter
            {
                Name = "counterparty",
                Type = "string",
                Description = "Counterparty name to filter by",
                Required = false
            },
            new ToolParameter
            {
                Name = "date",
                Type = "string",
                Description = "Date to query (YYYY-MM-DD format)",
                Required = false
            }
        ]
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, object?> arguments, WorkflowState state, CancellationToken ct = default)
    {
        var desk = arguments.TryGetValue("desk", out var d) ? d?.ToString() ?? "" : "";
        var counterparty = arguments.TryGetValue("counterparty", out var c) ? c?.ToString() : null;
        Console.WriteLine($"  [tool:query_transactions] Desk={desk}, Counterparty={counterparty ?? "all"}");

        var results = JsonSerializer.Serialize(new
        {
            Query = new { Desk = desk, Counterparty = counterparty, Date = "2026-04-07" },
            TotalTransactions = 10,
            MeridianCapitalTransactions = 6,
            MeridianCapitalVolume = "$21,250,000",
            DeskDailyLimit = "$10,000,000",
            LimitExceeded = true,
            LimitExceededBy = "112%",
            TransactionsWithoutPreTradeApproval = new[]
            {
                new { Id = "TXN-40294", Amount = "$4,200,000", Threshold = "$3,000,000" },
                new { Id = "TXN-40297", Amount = "$5,100,000", Threshold = "$3,000,000" },
                new { Id = "TXN-40299", Amount = "$3,600,000", Threshold = "$3,000,000" }
            },
            RapidSuccessionWindow = new
            {
                Window = "09:15-10:35 UTC",
                AvgInterval = "16 minutes",
                ShortestInterval = "24 seconds (TXN-40296 → TXN-40297)"
            },
            TraderInfo = new
            {
                Name = "K. Patel",
                EmployeeId = "EMP-4419",
                DeskHead = "J. Morrison (on PTO)",
                PriorIncidents = 1,
                PriorIncidentId = "INC-2025-11-0312"
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(ToolResult.Ok(results));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Custom Tools — File Operations (for subgraph code agent)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Reads a file from disk. Used by the code agent in the subgraph.
/// </summary>
public class ReadFileTool : ITool
{
    public string Name => "read_file";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Read the contents of a file from disk. Returns the file content as text.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "path",
                Type = "string",
                Description = "File path to read",
                Required = true
            }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> arguments, WorkflowState state, CancellationToken ct = default)
    {
        var path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "" : "";
        Console.WriteLine($"  [tool:read_file] Reading: {path}");

        try
        {
            var content = await File.ReadAllTextAsync(path, ct);
            return ToolResult.Ok(content);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Error reading file: {ex.Message}");
        }
    }
}

/// <summary>
/// Writes content to a file on disk. Used by the code agent in the subgraph.
/// </summary>
public class WriteFileTool : ITool
{
    public string Name => "write_file";

    public ToolDefinition Definition => new()
    {
        Name = Name,
        Description = "Write content to a file on disk. Creates or overwrites the file.",
        Parameters =
        [
            new ToolParameter
            {
                Name = "path",
                Type = "string",
                Description = "File path to write to",
                Required = true
            },
            new ToolParameter
            {
                Name = "content",
                Type = "string",
                Description = "Content to write to the file",
                Required = true
            }
        ]
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object?> arguments, WorkflowState state, CancellationToken ct = default)
    {
        var path = arguments.TryGetValue("path", out var p) ? p?.ToString() ?? "" : "";
        var content = arguments.TryGetValue("content", out var c) ? c?.ToString() ?? "" : "";
        Console.WriteLine($"  [tool:write_file] Writing: {path} ({content.Length} chars)");

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, content, ct);
            return ToolResult.Ok($"Successfully wrote {content.Length} characters to {path}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Error writing file: {ex.Message}");
        }
    }
}