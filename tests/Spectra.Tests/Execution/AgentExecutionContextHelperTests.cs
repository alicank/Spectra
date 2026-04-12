using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class AgentExecutionContextHelperTests
{
    private static StepContext CreateContext(
        Dictionary<string, object?>? inputs = null,
        WorkflowState? state = null,
        WorkflowDefinition? workflowDef = null) =>
        new()
        {
            RunId = "run-1",
            WorkflowId = "wf-1",
            NodeId = "node-1",
            State = state ?? new WorkflowState { WorkflowId = "wf-1" },
            CancellationToken = CancellationToken.None,
            Inputs = inputs ?? [],
            WorkflowDefinition = workflowDef
        };

    private static AgentDefinition CreateAgent(
        string id = "agent-1",
        int maxHandoffChainDepth = 5,
        List<string>? handoffTargets = null,
        HandoffPolicy handoffPolicy = HandoffPolicy.Allowed,
        CyclePolicy? cyclePolicy = null,
        TimeSpan? timeout = null) =>
        new()
        {
            Id = id,
            Provider = "openai",
            Model = "gpt-4",
            MaxHandoffChainDepth = maxHandoffChainDepth,
            HandoffTargets = handoffTargets ?? [],
            HandoffPolicy = handoffPolicy,
            CyclePolicy = cyclePolicy ?? CyclePolicy.Deny,
            Timeout = timeout
        };

    // ── GetFromInputs ──

    [Fact]
    public void GetFromInputs_returns_context_when_present()
    {
        var execCtx = new AgentExecutionContext { ChainDepth = 3 };
        var ctx = CreateContext(new Dictionary<string, object?>
        {
            ["__agentExecutionContext"] = execCtx
        });

        var result = AgentExecutionContextHelper.GetFromInputs(ctx);

        Assert.NotNull(result);
        Assert.Equal(3, result!.ChainDepth);
    }

    [Fact]
    public void GetFromInputs_returns_null_when_missing()
    {
        var ctx = CreateContext();

        var result = AgentExecutionContextHelper.GetFromInputs(ctx);

        Assert.Null(result);
    }

    // ── GetFromState ──

    [Fact]
    public void GetFromState_returns_context_when_present()
    {
        var state = new WorkflowState { WorkflowId = "wf-1" };
        var execCtx = new AgentExecutionContext { ChainDepth = 5 };
        state.Context["__agentExecutionContext"] = execCtx;

        var result = AgentExecutionContextHelper.GetFromState(state);

        Assert.NotNull(result);
        Assert.Equal(5, result!.ChainDepth);
    }

    [Fact]
    public void GetFromState_returns_null_when_missing()
    {
        var state = new WorkflowState { WorkflowId = "wf-1" };

        var result = AgentExecutionContextHelper.GetFromState(state);

        Assert.Null(result);
    }

    // ── StoreInState ──

    [Fact]
    public void StoreInState_writes_context_to_state()
    {
        var state = new WorkflowState { WorkflowId = "wf-1" };
        var execCtx = new AgentExecutionContext { OriginatorRunId = "run-42" };

        AgentExecutionContextHelper.StoreInState(state, execCtx);

        var retrieved = AgentExecutionContextHelper.GetFromState(state);
        Assert.NotNull(retrieved);
        Assert.Equal("run-42", retrieved!.OriginatorRunId);
    }

    // ── GetOrCreate ──

    [Fact]
    public void GetOrCreate_returns_existing_from_inputs()
    {
        var existing = new AgentExecutionContext { ChainDepth = 7 };
        var ctx = CreateContext(new Dictionary<string, object?>
        {
            ["__agentExecutionContext"] = existing
        });

        var result = AgentExecutionContextHelper.GetOrCreate(ctx, null, null);

        Assert.Same(existing, result);
    }

    [Fact]
    public void GetOrCreate_creates_new_with_agent_config()
    {
        var agent = CreateAgent(timeout: TimeSpan.FromMinutes(2));
        var workflow = new WorkflowDefinition
        {
            Id = "wf-1",
            GlobalTokenBudget = 100_000
        };
        var ctx = CreateContext(workflowDef: workflow);

        var result = AgentExecutionContextHelper.GetOrCreate(ctx, agent, workflow);

        Assert.Equal("run-1", result.OriginatorRunId);
        Assert.Equal(CyclePolicyMode.Deny, result.CyclePolicy.Mode);
        Assert.Equal(100_000, result.GlobalBudgetRemaining);
        Assert.NotNull(result.WallClockDeadline);
    }

    [Fact]
    public void GetOrCreate_uses_workflow_timeout_when_agent_has_none()
    {
        var agent = CreateAgent();
        var workflow = new WorkflowDefinition
        {
            Id = "wf-1",
            DefaultTimeout = TimeSpan.FromMinutes(5)
        };
        var ctx = CreateContext(workflowDef: workflow);

        var result = AgentExecutionContextHelper.GetOrCreate(ctx, agent, workflow);

        Assert.NotNull(result.WallClockDeadline);
    }

    // ── ValidateHandoff ──

    [Fact]
    public void ValidateHandoff_passes_when_all_checks_ok()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"]);
        var execCtx = new AgentExecutionContext { ChainDepth = 0 };

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateHandoff_blocks_unknown_target()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"]);
        var execCtx = new AgentExecutionContext();

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-c", null);

        Assert.NotNull(error);
        Assert.Contains("not allowed", error);
    }

    [Fact]
    public void ValidateHandoff_blocks_disabled_policy()
    {
        var agent = CreateAgent(
            handoffTargets: ["agent-b"],
            handoffPolicy: HandoffPolicy.Disabled);
        var execCtx = new AgentExecutionContext();

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.NotNull(error);
        Assert.Contains("disabled", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHandoff_blocks_chain_depth_exceeded()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"], maxHandoffChainDepth: 2);
        var execCtx = new AgentExecutionContext { ChainDepth = 2 };

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.NotNull(error);
        Assert.Contains("depth", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHandoff_blocks_workflow_level_chain_depth()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"], maxHandoffChainDepth: 10);
        var workflow = new WorkflowDefinition
        {
            Id = "wf-1",
            MaxHandoffChainDepth = 3
        };
        var execCtx = new AgentExecutionContext { ChainDepth = 3 };

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", workflow);

        Assert.NotNull(error);
        Assert.Contains("workflow-level", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHandoff_blocks_cycle_with_deny_policy()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"]);
        var execCtx = new AgentExecutionContext();
        execCtx.VisitedAgents.Add("agent-b");

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.NotNull(error);
        Assert.Contains("already been visited", error);
    }

    [Fact]
    public void ValidateHandoff_allows_cycle_with_allow_policy()
    {
        var agent = CreateAgent(
            handoffTargets: ["agent-b"],
            cyclePolicy: CyclePolicy.Allow);
        var execCtx = new AgentExecutionContext { CyclePolicy = CyclePolicy.Allow };
        execCtx.VisitedAgents.Add("agent-b");

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateHandoff_allows_cycle_within_limit()
    {
        var agent = CreateAgent(
            handoffTargets: ["agent-b"],
            cyclePolicy: CyclePolicy.AllowWithLimit(2));
        var execCtx = new AgentExecutionContext
        {
            CyclePolicy = CyclePolicy.AllowWithLimit(2)
        };
        execCtx.VisitedAgents.Add("agent-b");
        execCtx.HandoffHistory.Add(new AgentHandoffRecord
        {
            FromAgent = "agent-a",
            ToAgent = "agent-b",
            Intent = "first",
            Timestamp = DateTimeOffset.UtcNow,
            ChainDepth = 1,
            TokensConsumedAtHandoff = 100
        });

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.Null(error); // 1 visit < 2 limit
    }

    [Fact]
    public void ValidateHandoff_blocks_cycle_exceeding_limit()
    {
        var agent = CreateAgent(
            handoffTargets: ["agent-b"],
            cyclePolicy: CyclePolicy.AllowWithLimit(1));
        var execCtx = new AgentExecutionContext
        {
            CyclePolicy = CyclePolicy.AllowWithLimit(1)
        };
        execCtx.VisitedAgents.Add("agent-b");
        execCtx.HandoffHistory.Add(new AgentHandoffRecord
        {
            FromAgent = "agent-a",
            ToAgent = "agent-b",
            Intent = "first",
            Timestamp = DateTimeOffset.UtcNow,
            ChainDepth = 1,
            TokensConsumedAtHandoff = 100
        });

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.NotNull(error);
        Assert.Contains("revisit limit", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHandoff_blocks_exhausted_budget()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"]);
        var execCtx = new AgentExecutionContext
        {
            GlobalBudgetRemaining = 1000,
            TotalTokensConsumed = 1000
        };

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.NotNull(error);
        Assert.Contains("budget", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateHandoff_blocks_past_deadline()
    {
        var agent = CreateAgent(handoffTargets: ["agent-b"]);
        var execCtx = new AgentExecutionContext
        {
            WallClockDeadline = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        var error = AgentExecutionContextHelper.ValidateHandoff(execCtx, agent, "agent-b", null);

        Assert.NotNull(error);
        Assert.Contains("deadline", error, StringComparison.OrdinalIgnoreCase);
    }
}