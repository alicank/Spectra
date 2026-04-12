// Spectra MultiAgent Sample
// Demonstrates multi-agent handoff and supervisor patterns

using Spectra.Workflow;
using Spectra.Contracts.Workflow;

Console.WriteLine("=== Spectra Multi-Agent Patterns ===\n");

// ── Pattern 1: Agent Handoff ──
// A researcher agent can hand off to a coder when implementation is needed.
// The coder can hand off to a reviewer for code review.

Console.WriteLine("--- Pattern 1: Handoff Chain ---");

var handoffWorkflow = WorkflowBuilder.Create("handoff-demo")
    .WithName("Research → Code → Review Pipeline")
    .WithMaxHandoffChainDepth(5)
    .WithGlobalTokenBudget(200_000)

    // Define agents
    .AddAgent("researcher", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a research specialist. When you have enough information for implementation, hand off to the coder.")
        .WithHandoffTargets("coder")
        .WithHandoffPolicy(HandoffPolicy.Allowed)
        .WithConversationScope(ConversationScope.Summary))

    .AddAgent("coder", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a coding specialist. Implement what the researcher found. When done, hand off to the reviewer.")
        .WithHandoffTargets("reviewer")
        .WithConversationScope(ConversationScope.Full)
        .WithEscalationTarget("human"))

    .AddAgent("reviewer", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a code reviewer. Review the implementation and provide feedback.")
        .WithConversationScope(ConversationScope.Full))

    // Define agent nodes
    .AddAgentNode("research-node", "researcher", node => node
        .WithTools("web_search", "read_document")
        .WithUserPrompt("{{inputs.task}}")
        .WithMaxIterations(10)
        .WithTokenBudget(50_000))

    .AddAgentNode("code-node", "coder", node => node
        .WithTools("write_file", "run_tests")
        .WithMaxIterations(15)
        .WithTokenBudget(80_000))

    .AddAgentNode("review-node", "reviewer", node => node
        .WithTools("read_file")
        .WithMaxIterations(5)
        .WithTokenBudget(30_000))

    .SetEntryNode("research-node")
    .Build();

Console.WriteLine($"  Workflow: {handoffWorkflow.Name}");
Console.WriteLine($"  Agents: {string.Join(" → ", handoffWorkflow.Agents.Select(a => a.Id))}");
Console.WriteLine($"  Global token budget: {handoffWorkflow.GlobalTokenBudget:N0}");
Console.WriteLine();

// ── Pattern 2: Supervisor with Workers ──
// A lead agent delegates tasks to specialist workers and aggregates results.

Console.WriteLine("--- Pattern 2: Supervisor ---");

var supervisorWorkflow = WorkflowBuilder.Create("supervisor-demo")
    .WithName("Supervised Team")
    .WithGlobalTokenBudget(500_000)
    .WithMaxTotalAgentIterations(200)

    .AddAgent("lead", "openai", "gpt-4", agent => agent
        .WithSystemPrompt(
            "You are a team lead. Break down the task and delegate to your workers. " +
            "Collect their results and synthesize a final answer.")
        .AsSupervisor("researcher", "coder", "analyst")
        .WithDelegationPolicy(DelegationPolicy.Allowed)
        .WithMaxDelegationDepth(2)
        .WithEscalationTarget("human"))

    .AddAgent("researcher", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a research specialist."))

    .AddAgent("coder", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a coding specialist."))

    .AddAgent("analyst", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a data analyst."))

    .AddAgentNode("lead-node", "lead", node => node
        .WithUserPrompt("{{inputs.task}}")
        .WithMaxIterations(30)
        .WithTokenBudget(100_000)
        .WithTimeout(TimeSpan.FromMinutes(10)))

    .SetEntryNode("lead-node")
    .Build();

Console.WriteLine($"  Workflow: {supervisorWorkflow.Name}");
Console.WriteLine($"  Supervisor: {supervisorWorkflow.Agents[0].Id}");
Console.WriteLine($"  Workers: {string.Join(", ", supervisorWorkflow.Agents[0].SupervisorWorkers)}");
Console.WriteLine($"  Max delegation depth: {supervisorWorkflow.Agents[0].MaxDelegationDepth}");
Console.WriteLine();

// ── Pattern 3: Handoff with Approval Gates ──
// Handoffs require human approval before proceeding.

Console.WriteLine("--- Pattern 3: Gated Handoff ---");

var gatedWorkflow = WorkflowBuilder.Create("gated-handoff-demo")
    .WithName("Gated Research → Code Pipeline")

    .AddAgent("researcher", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a research specialist.")
        .WithHandoffTargets("coder")
        .WithHandoffPolicy(HandoffPolicy.RequiresApproval)
        .WithConversationScope(ConversationScope.LastN, maxMessages: 5))

    .AddAgent("coder", "openai", "gpt-4", agent => agent
        .WithSystemPrompt("You are a coding specialist.")
        .WithCyclePolicy(CyclePolicy.AllowWithLimit(2)))

    .AddAgentNode("research-node", "researcher", node => node
        .WithUserPrompt("{{inputs.task}}")
        .WithMaxIterations(10))

    .AddAgentNode("code-node", "coder", node => node
        .WithMaxIterations(15))

    .SetEntryNode("research-node")
    .Build();

Console.WriteLine($"  Workflow: {gatedWorkflow.Name}");
Console.WriteLine($"  Handoff policy: {gatedWorkflow.Agents[0].HandoffPolicy}");
Console.WriteLine($"  Conversation scope: {gatedWorkflow.Agents[0].ConversationScope} (last {gatedWorkflow.Agents[0].MaxContextMessages})");
Console.WriteLine();

Console.WriteLine("=== All patterns configured successfully ===");