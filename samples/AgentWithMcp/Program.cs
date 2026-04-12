// Spectra AgentWithMcp Sample
// An agent that reads meeting notes from disk via the official MCP filesystem server,
// then answers questions about decisions, action items, and follow-ups.
// MCP tools are discovered at startup and used identically to native Spectra tools.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectra.Contracts.Execution;
using Spectra.Contracts.Mcp;
using Spectra.Contracts.State;
using Spectra.Registration;
using Spectra.Workflow;

var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
    ?? throw new InvalidOperationException(
        "Set OPENROUTER_API_KEY environment variable before running this sample.");

// Meeting notes live in workflows/ next to the binary.
// The MCP server is sandboxed to this directory — it cannot access anything outside it.
var notesDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "workflows"));

Directory.CreateDirectory(notesDir);
SeedMeetingNotes(notesDir);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSpectra(spectra =>
        {
            spectra.AddOpenRouter(config =>
            {
                config.ApiKey = apiKey;
                config.Model = "anthropic/claude-haiku-4-5";
            });

            // ── MCP: official filesystem server (requires Node.js / npx) ──
            // Spectra launches this as a subprocess at startup, handshakes over
            // stdio JSON-RPC, discovers its tools, and registers each one as a
            // native ITool. The workflow references them by name — no MCP-specific
            // code anywhere in the workflow definition below.

            // On Windows, npx is a .cmd wrapper — Process.Start can't resolve it
            // directly from PATH. Route through cmd.exe /c to let the shell handle it.
            var isWindows = OperatingSystem.IsWindows();

            spectra.AddMcpServer(new McpServerConfig
            {
                Name = "filesystem",
                Command = isWindows ? "cmd.exe" : "npx",
                Arguments = isWindows
                    ? new List<string> { "/c", "npx", "-y", "@modelcontextprotocol/server-filesystem", notesDir }
                    : new List<string> { "-y", "@modelcontextprotocol/server-filesystem", notesDir },
                Transport = McpTransportType.Stdio,

                // InheritEnvironment must be true so the child process gets PATH,
                // PATHEXT, SystemRoot etc. Without this, cmd.exe /c npx can't
                // resolve npx.cmd and the process exits immediately ("pipe closed").
                // In production, prefer false + explicit EnvironmentVariables for security.
                InheritEnvironment = true,

                // Read-only: the agent can browse and read but never modify files.
                AllowedTools = new List<string> { "read_file", "list_directory", "get_file_info" }
            });

            spectra.AddConsoleEvents();
        });
    })
    .Build();

// ── Workflow ─────────────────────────────────────────────────────────────────

var workflow = WorkflowBuilder.Create("meeting-assistant")
    .WithName("Meeting Notes Assistant")
    .AddAgent("assistant", "openrouter", "anthropic/claude-haiku-4-5", agent => agent
        .WithSystemPrompt("""
            You are a meeting assistant with read access to a folder of meeting notes.
            Each file is a record of one meeting and follows a consistent structure:
            date, attendees, agenda, discussion points, decisions, and action items.

            When answering a question:
            1. Call mcp__filesystem__list_directory to see which meeting files are available.
            2. Call mcp__filesystem__read_file on whichever files are relevant to the question.
               Read multiple files if the question spans several meetings.
            3. Give a clear, structured answer. For action item queries, group by owner.
               For decision queries, include the meeting date for traceability.

            Only report what is written in the files. If something is not mentioned, say so.
            """)
        .WithMaxTokens(1024))
    .AddNode("answer", "agent", node => node
        .WithParameter("agentId", "assistant")
        .WithParameter("userPrompt", "{{inputs.question}}")
        .WithParameter("tools", new List<string> { "mcp__filesystem__read_file", "mcp__filesystem__list_directory", "mcp__filesystem__get_file_info" })
        .WithParameter("maxIterations", 8))
    .Build();

// ── Run ──────────────────────────────────────────────────────────────────────

// Start the host — this connects to the MCP subprocess, handshakes, and registers tools.
// Without StartAsync, AddMcpServer config exists but no connection is ever established.
await host.StartAsync();

var runner = host.Services.GetRequiredService<IWorkflowRunner>();

var state = new WorkflowState();
state.Inputs["question"] = """
    What are all the open action items across every meeting, and who owns each one?
    """;

// Try changing the question to explore other capabilities:
//   "What was decided about the API rate limiting?"
//   "Summarise the product roadmap meeting in one paragraph."
//   "Which meetings did Alice attend?"
//   "List every decision made in November 2024."

var result = await runner.RunAsync(workflow, state);

Console.WriteLine();

if (result.Context.TryGetValue("answer", out var output)
    && output is IDictionary<string, object?> dict)
{
    if (dict.TryGetValue("response", out var response))
        Console.WriteLine($"Assistant:\n{response}");

    if (dict.TryGetValue("iterations", out var iterations))
        Console.WriteLine($"\nTool-call iterations: {iterations}");

    if (dict.TryGetValue("stopReason", out var stopReason))
        Console.WriteLine($"Stop reason: {stopReason}");
}

await host.StopAsync();
Console.WriteLine($"\nErrors: {result.Errors.Count}");

// ── Seed demo meeting notes ───────────────────────────────────────────────────

static void SeedMeetingNotes(string dir)
{
    WriteFile(dir, "2024-11-05-product-roadmap.txt", """
        MEETING NOTES
        Date       : 2024-11-05 (Tuesday, 14:00 – 15:30)
        Location   : Conference Room B / Zoom hybrid
        Attendees  : Alice (PM), Bob (Backend), Carol (Design), David (QA)
        Facilitator: Alice

        AGENDA
        1. Q1 2025 roadmap priorities
        2. API rate limiting rollout
        3. Design system migration

        DISCUSSION

        Q1 2025 priorities:
        The team agreed to focus on three themes: performance, onboarding, and API stability.
        Alice presented the draft roadmap. Bob flagged that the search rewrite would need
        at least 6 weeks and should not be squeezed into Q1.

        API rate limiting:
        Current plan is a tiered model: 100 req/min (free), 1 000 req/min (pro), unlimited (enterprise).
        Bob raised concerns about Redis dependency in production — needs a fallback.
        Decision: proceed with Redis-backed rate limiting but implement a local in-memory
        fallback for single-node deployments. Target release: end of November.

        Design system migration:
        Carol demoed the new component library. Estimated 3-week migration for the dashboard.
        David asked for a QA checklist before the migration starts.

        DECISIONS
        - API rate limiting: Redis + in-memory fallback, release end-November 2024
        - Search rewrite pushed to Q2 2025
        - Design system migration begins 2024-11-18, Carol leads

        ACTION ITEMS
        - Bob   : Implement Redis rate limiter with in-memory fallback — due 2024-11-22
        - Carol : Share component library migration guide with the team — due 2024-11-08
        - David : Draft QA checklist for design system migration — due 2024-11-15
        - Alice : Update roadmap doc and share with stakeholders — due 2024-11-07
        """);

    WriteFile(dir, "2024-11-12-backend-sync.txt", """
        MEETING NOTES
        Date       : 2024-11-12 (Tuesday, 10:00 – 11:00)
        Location   : Zoom
        Attendees  : Bob (Backend), Eve (DevOps), Frank (Security)
        Facilitator: Bob

        AGENDA
        1. Redis rate limiter implementation update
        2. Production deployment pipeline
        3. Security review of new API endpoints

        DISCUSSION

        Rate limiter update:
        Bob has the Redis integration working locally. The in-memory fallback is coded
        but not yet tested under load. Eve confirmed that Redis is available in staging.

        Deployment pipeline:
        Eve walked through the new blue-green deployment setup. Zero-downtime deploys
        are now possible for stateless services. Stateful services still need a maintenance
        window. The team agreed to move the rate limiter service to the new pipeline.

        Security review:
        Frank reviewed the three new API endpoints added last sprint. Two are fine.
        One endpoint (POST /admin/config) lacks authentication middleware — this is a
        blocker and must be fixed before any production release.

        DECISIONS
        - POST /admin/config must have auth middleware before release — Frank to verify
        - Rate limiter will use the new blue-green pipeline for its production deploy
        - Load testing of in-memory fallback scheduled for week of 2024-11-18

        ACTION ITEMS
        - Bob   : Add auth middleware to POST /admin/config — due 2024-11-13 (urgent)
        - Bob   : Run load tests on in-memory fallback — due 2024-11-20
        - Eve   : Provision Redis in production environment — due 2024-11-15
        - Frank : Re-verify POST /admin/config after Bob's fix — due 2024-11-14
        """);

    WriteFile(dir, "2024-11-19-sprint-retrospective.txt", """
        MEETING NOTES
        Date       : 2024-11-19 (Tuesday, 16:00 – 17:00)
        Location   : Conference Room A
        Attendees  : Alice (PM), Bob (Backend), Carol (Design), David (QA), Eve (DevOps)
        Facilitator: Alice

        AGENDA
        1. Sprint 22 retrospective — what went well / what didn't
        2. Process improvements
        3. Sprint 23 goals

        DISCUSSION

        What went well:
        - Blue-green deployment rollout was smooth (Eve)
        - Rate limiter shipped on time (Bob)
        - Carol's component library docs were well received by the team

        What didn't go well:
        - POST /admin/config security issue was caught late — should have been in PR review
        - QA checklist for design migration was delivered one day late (David)
        - Too many Slack interruptions during focus time

        Process improvements agreed:
        - Add a security checklist to the PR template (Frank to draft, even though not present today)
        - "Focus hours" 10:00–12:00 daily — no non-urgent Slack messages
        - QA to be looped into design handoffs earlier

        Sprint 23 goals:
        - Complete design system migration for the dashboard
        - Begin search rewrite scoping
        - Harden monitoring and alerting for the rate limiter

        DECISIONS
        - Security checklist added to PR template — mandatory from Sprint 23
        - Focus hours policy starts 2024-11-25
        - Search rewrite scoping (not implementation) moved to Sprint 23

        ACTION ITEMS
        - Alice : Announce focus hours policy to the whole company — due 2024-11-21
        - Bob   : Set up rate limiter alerting in Grafana — due 2024-11-29
        - Carol : Complete dashboard design migration — due 2024-11-29
        - David : Write search rewrite scoping document — due 2024-11-29
        - Frank : Draft security checklist for PR template — due 2024-11-22 (async)
        """);
}

static void WriteFile(string dir, string name, string content)
{
    var path = Path.Combine(dir, name);
    if (!File.Exists(path))
        File.WriteAllText(path, content.TrimStart());
}