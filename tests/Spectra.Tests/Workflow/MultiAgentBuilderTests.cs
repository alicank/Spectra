using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Workflow;
using Xunit;

namespace Spectra.Tests.Workflow;

public class MultiAgentBuilderTests
{
    // ── AgentBuilder multi-agent properties ──

    [Fact]
    public void AgentBuilder_sets_handoff_targets()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithHandoffTargets("agent-b", "agent-c"))
            .Build();

        var agent = def.Agents[0];
        Assert.Equal(2, agent.HandoffTargets.Count);
        Assert.Contains("agent-b", agent.HandoffTargets);
        Assert.Contains("agent-c", agent.HandoffTargets);
    }

    [Fact]
    public void AgentBuilder_sets_handoff_policy()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithHandoffPolicy(HandoffPolicy.RequiresApproval))
            .Build();

        Assert.Equal(HandoffPolicy.RequiresApproval, def.Agents[0].HandoffPolicy);
    }

    [Fact]
    public void AgentBuilder_sets_supervisor_workers()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("lead", "openai", "gpt-4", a => a
                .AsSupervisor("worker-1", "worker-2"))
            .Build();

        var agent = def.Agents[0];
        Assert.Equal(2, agent.SupervisorWorkers.Count);
        Assert.Contains("worker-1", agent.SupervisorWorkers);
    }

    [Fact]
    public void AgentBuilder_sets_delegation_policy()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithDelegationPolicy(DelegationPolicy.RequiresApproval))
            .Build();

        Assert.Equal(DelegationPolicy.RequiresApproval, def.Agents[0].DelegationPolicy);
    }

    [Fact]
    public void AgentBuilder_sets_max_delegation_depth()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithMaxDelegationDepth(5))
            .Build();

        Assert.Equal(5, def.Agents[0].MaxDelegationDepth);
    }

    [Fact]
    public void AgentBuilder_sets_max_handoff_chain_depth()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithMaxHandoffChainDepth(8))
            .Build();

        Assert.Equal(8, def.Agents[0].MaxHandoffChainDepth);
    }

    [Fact]
    public void AgentBuilder_sets_conversation_scope_and_max_messages()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithConversationScope(ConversationScope.LastN, maxMessages: 5))
            .Build();

        Assert.Equal(ConversationScope.LastN, def.Agents[0].ConversationScope);
        Assert.Equal(5, def.Agents[0].MaxContextMessages);
    }

    [Fact]
    public void AgentBuilder_sets_cycle_policy()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithCyclePolicy(CyclePolicy.AllowWithLimit(3)))
            .Build();

        Assert.Equal(CyclePolicyMode.AllowWithLimit, def.Agents[0].CyclePolicy.Mode);
        Assert.Equal(3, def.Agents[0].CyclePolicy.MaxRevisits);
    }

    [Fact]
    public void AgentBuilder_sets_escalation_target()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithEscalationTarget("human"))
            .Build();

        Assert.Equal("human", def.Agents[0].EscalationTarget);
    }

    [Fact]
    public void AgentBuilder_sets_timeout()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithTimeout(TimeSpan.FromMinutes(5)))
            .Build();

        Assert.Equal(TimeSpan.FromMinutes(5), def.Agents[0].Timeout);
    }

    [Fact]
    public void AgentBuilder_sets_state_read_write_paths()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4", a => a
                .WithStateReadPaths("Context.*", "Inputs.task")
                .WithStateWritePaths("Context.Result"))
            .Build();

        var agent = def.Agents[0];
        Assert.Equal(2, agent.StateReadPaths.Count);
        Assert.Single(agent.StateWritePaths);
        Assert.Contains("Context.*", agent.StateReadPaths);
    }

    [Fact]
    public void AgentBuilder_defaults_are_sensible()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .AddAgent("a1", "openai", "gpt-4")
            .Build();

        var agent = def.Agents[0];
        Assert.Empty(agent.HandoffTargets);
        Assert.Equal(HandoffPolicy.Allowed, agent.HandoffPolicy);
        Assert.Empty(agent.SupervisorWorkers);
        Assert.Equal(DelegationPolicy.Allowed, agent.DelegationPolicy);
        Assert.Equal(3, agent.MaxDelegationDepth);
        Assert.Equal(5, agent.MaxHandoffChainDepth);
        Assert.Equal(ConversationScope.Handoff, agent.ConversationScope);
        Assert.Equal(10, agent.MaxContextMessages);
        Assert.Equal(CyclePolicyMode.Deny, agent.CyclePolicy.Mode);
        Assert.Null(agent.EscalationTarget);
        Assert.Null(agent.Timeout);
        Assert.Empty(agent.StateReadPaths);
        Assert.Empty(agent.StateWritePaths);
    }

    // ── AgentNodeBuilder multi-agent properties ──

    [Fact]
    public void AgentNodeBuilder_stores_handoff_targets_as_parameter()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("n", "agent-1", n => n
                .WithHandoffTargets("agent-b", "agent-c"))
            .Build();

        var targets = def.Nodes[0].Parameters["__handoffTargets"] as string[];
        Assert.NotNull(targets);
        Assert.Equal(2, targets!.Length);
        Assert.Contains("agent-b", targets);
    }

    [Fact]
    public void AgentNodeBuilder_stores_supervisor_workers_as_parameter()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("n", "agent-1", n => n
                .AsSupervisor("w1", "w2"))
            .Build();

        var workers = def.Nodes[0].Parameters["__supervisorWorkers"] as string[];
        Assert.NotNull(workers);
        Assert.Equal(2, workers!.Length);
    }

    [Fact]
    public void AgentNodeBuilder_stores_escalation_target()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("n", "agent-1", n => n
                .WithEscalationTarget("human"))
            .Build();

        Assert.Equal("human", def.Nodes[0].Parameters["__escalationTarget"]);
    }

    [Fact]
    public void AgentNodeBuilder_stores_timeout_as_seconds()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("n", "agent-1", n => n
                .WithTimeout(TimeSpan.FromMinutes(3)))
            .Build();

        Assert.Equal(180.0, def.Nodes[0].Parameters["__timeout"]);
    }

    [Fact]
    public void AgentNodeBuilder_without_multi_agent_config_has_no_extra_params()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddAgentNode("n", "agent-1")
            .Build();

        Assert.False(def.Nodes[0].Parameters.ContainsKey("__handoffTargets"));
        Assert.False(def.Nodes[0].Parameters.ContainsKey("__supervisorWorkers"));
        Assert.False(def.Nodes[0].Parameters.ContainsKey("__escalationTarget"));
        Assert.False(def.Nodes[0].Parameters.ContainsKey("__timeout"));
    }

    // ── WorkflowBuilder global guards ──

    [Fact]
    public void WorkflowBuilder_sets_global_token_budget()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .WithGlobalTokenBudget(200_000)
            .Build();

        Assert.Equal(200_000, def.GlobalTokenBudget);
    }

    [Fact]
    public void WorkflowBuilder_sets_max_handoff_chain_depth()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .WithMaxHandoffChainDepth(15)
            .Build();

        Assert.Equal(15, def.MaxHandoffChainDepth);
    }

    [Fact]
    public void WorkflowBuilder_sets_max_total_agent_iterations()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .WithMaxTotalAgentIterations(1000)
            .Build();

        Assert.Equal(1000, def.MaxTotalAgentIterations);
    }

    [Fact]
    public void WorkflowBuilder_global_guard_defaults()
    {
        var def = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo")
            .Build();

        Assert.Equal(0, def.GlobalTokenBudget);
        Assert.Equal(10, def.MaxHandoffChainDepth);
        Assert.Equal(500, def.MaxTotalAgentIterations);
    }

    [Fact]
    public void WorkflowBuilder_global_guard_fluent_chaining()
    {
        var builder = WorkflowBuilder.Create("wf-1")
            .AddNode("n", "echo");

        var returned = builder
            .WithGlobalTokenBudget(100)
            .WithMaxHandoffChainDepth(5)
            .WithMaxTotalAgentIterations(200);

        Assert.Same(builder, returned);
    }

    // ── StepResult.HandoffTo ──

    [Fact]
    public void StepResult_HandoffTo_creates_correct_result()
    {
        var handoff = new AgentHandoff
        {
            FromAgent = "a",
            ToAgent = "b",
            Intent = "implement"
        };

        var result = StepResult.HandoffTo(handoff, new() { ["key"] = "value" });

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.Same(handoff, result.Handoff);
        Assert.Equal("value", result.Outputs["key"]);
    }

    [Fact]
    public void StepResult_HandoffTo_with_null_outputs()
    {
        var handoff = new AgentHandoff
        {
            FromAgent = "a",
            ToAgent = "b",
            Intent = "test"
        };

        var result = StepResult.HandoffTo(handoff);

        Assert.Equal(StepStatus.Handoff, result.Status);
        Assert.NotNull(result.Outputs);
        Assert.Empty(result.Outputs);
    }

    // ── HandoffEvents ──

    [Fact]
    public void AgentHandoffEvent_stores_all_fields()
    {
        var evt = new Contracts.Events.AgentHandoffEvent
        {
            RunId = "r1",
            WorkflowId = "wf1",
            EventType = "AgentHandoffEvent",
            FromAgent = "a",
            ToAgent = "b",
            Intent = "implement",
            ChainDepth = 2,
            ConversationScope = ConversationScope.Full,
            TokensBudgetPassed = 50_000
        };

        Assert.Equal("a", evt.FromAgent);
        Assert.Equal("b", evt.ToAgent);
        Assert.Equal(2, evt.ChainDepth);
        Assert.Equal(ConversationScope.Full, evt.ConversationScope);
    }

    [Fact]
    public void AgentHandoffBlockedEvent_stores_reason()
    {
        var evt = new Contracts.Events.AgentHandoffBlockedEvent
        {
            RunId = "r1",
            WorkflowId = "wf1",
            EventType = "AgentHandoffBlockedEvent",
            FromAgent = "a",
            ToAgent = "b",
            Reason = "cycle denied"
        };

        Assert.Equal("cycle denied", evt.Reason);
    }

    [Fact]
    public void AgentEscalationEvent_stores_details()
    {
        var evt = new Contracts.Events.AgentEscalationEvent
        {
            RunId = "r1",
            WorkflowId = "wf1",
            EventType = "AgentEscalationEvent",
            FailedAgent = "a",
            EscalationTarget = "human",
            Reason = "max_iterations",
            FailureDetails = "Reached 10 iterations"
        };

        Assert.Equal("human", evt.EscalationTarget);
        Assert.Equal("max_iterations", evt.Reason);
    }

    [Fact]
    public void AgentDelegationStartedEvent_stores_fields()
    {
        var evt = new Contracts.Events.AgentDelegationStartedEvent
        {
            RunId = "r1",
            WorkflowId = "wf1",
            EventType = "AgentDelegationStartedEvent",
            SupervisorAgent = "lead",
            WorkerAgent = "coder",
            Task = "Implement feature X",
            DelegationDepth = 1,
            BudgetAllocated = 100_000
        };

        Assert.Equal("lead", evt.SupervisorAgent);
        Assert.Equal("coder", evt.WorkerAgent);
        Assert.Equal(1, evt.DelegationDepth);
    }

    [Fact]
    public void AgentDelegationCompletedEvent_stores_fields()
    {
        var evt = new Contracts.Events.AgentDelegationCompletedEvent
        {
            RunId = "r1",
            WorkflowId = "wf1",
            EventType = "AgentDelegationCompletedEvent",
            SupervisorAgent = "lead",
            WorkerAgent = "coder",
            Status = "Succeeded",
            TokensUsed = 5000,
            Duration = TimeSpan.FromSeconds(30),
            ResultSummary = "Feature implemented"
        };

        Assert.Equal("Succeeded", evt.Status);
        Assert.Equal(5000, evt.TokensUsed);
        Assert.Equal("Feature implemented", evt.ResultSummary);
    }

    // ── Full multi-agent workflow builder ergonomics ──

    [Fact]
    public void Full_multi_agent_workflow_builds_correctly()
    {
        var def = WorkflowBuilder.Create("multi-agent-pipeline")
            .WithName("Research → Code → Review")
            .WithGlobalTokenBudget(200_000)
            .WithMaxHandoffChainDepth(5)

            .AddAgent("researcher", "openai", "gpt-4", a => a
                .WithSystemPrompt("You research things.")
                .WithHandoffTargets("coder")
                .WithHandoffPolicy(HandoffPolicy.Allowed)
                .WithConversationScope(ConversationScope.Summary))

            .AddAgent("coder", "openai", "gpt-4", a => a
                .WithSystemPrompt("You code things.")
                .WithHandoffTargets("reviewer")
                .WithEscalationTarget("human"))

            .AddAgent("reviewer", "openai", "gpt-4", a => a
                .WithSystemPrompt("You review code.")
                .WithCyclePolicy(CyclePolicy.AllowWithLimit(2)))

            .AddAgentNode("research-node", "researcher", n => n
                .WithTools("web_search")
                .WithUserPrompt("{{inputs.task}}")
                .WithMaxIterations(10))

            .AddAgentNode("code-node", "coder", n => n
                .WithTools("write_file", "run_tests")
                .WithMaxIterations(15))

            .AddAgentNode("review-node", "reviewer", n => n
                .WithMaxIterations(5))

            .SetEntryNode("research-node")
            .Build();

        Assert.Equal("multi-agent-pipeline", def.Id);
        Assert.Equal(3, def.Agents.Count);
        Assert.Equal(3, def.Nodes.Count);
        Assert.Equal(200_000, def.GlobalTokenBudget);
        Assert.Equal(5, def.MaxHandoffChainDepth);

        // Researcher
        Assert.Contains("coder", def.Agents[0].HandoffTargets);
        Assert.Equal(ConversationScope.Summary, def.Agents[0].ConversationScope);

        // Coder
        Assert.Equal("human", def.Agents[1].EscalationTarget);
        Assert.Contains("reviewer", def.Agents[1].HandoffTargets);

        // Reviewer
        Assert.Equal(CyclePolicyMode.AllowWithLimit, def.Agents[2].CyclePolicy.Mode);
    }
}