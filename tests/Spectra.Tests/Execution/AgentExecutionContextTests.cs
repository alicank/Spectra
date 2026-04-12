using Spectra.Contracts.Workflow;
using Xunit;

namespace Spectra.Tests.Execution;

public class AgentExecutionContextTests
{
    [Fact]
    public void Fork_creates_independent_copy()
    {
        var original = new AgentExecutionContext
        {
            ChainDepth = 2,
            DelegationDepth = 1,
            TotalTokensConsumed = 5000,
            GlobalBudgetRemaining = 95000,
            OriginatorRunId = "run-1",
            ParentAgentId = "agent-a",
            CyclePolicy = CyclePolicy.AllowWithLimit(3)
        };
        original.VisitedAgents.Add("agent-a");
        original.VisitedAgents.Add("agent-b");
        original.HandoffHistory.Add(new AgentHandoffRecord
        {
            FromAgent = "agent-a",
            ToAgent = "agent-b",
            Intent = "implement",
            Timestamp = DateTimeOffset.UtcNow,
            ChainDepth = 1,
            TokensConsumedAtHandoff = 3000
        });

        var fork = original.Fork();

        // Values are copied
        Assert.Equal(2, fork.ChainDepth);
        Assert.Equal(1, fork.DelegationDepth);
        Assert.Equal(5000, fork.TotalTokensConsumed);
        Assert.Equal(95000, fork.GlobalBudgetRemaining);
        Assert.Equal("run-1", fork.OriginatorRunId);
        Assert.Equal("agent-a", fork.ParentAgentId);
        Assert.Contains("agent-a", fork.VisitedAgents);
        Assert.Contains("agent-b", fork.VisitedAgents);
        Assert.Single(fork.HandoffHistory);

        // Mutations are independent
        fork.ChainDepth = 10;
        fork.VisitedAgents.Add("agent-c");
        fork.HandoffHistory.Add(new AgentHandoffRecord
        {
            FromAgent = "agent-b",
            ToAgent = "agent-c",
            Intent = "review",
            Timestamp = DateTimeOffset.UtcNow,
            ChainDepth = 3,
            TokensConsumedAtHandoff = 6000
        });

        Assert.Equal(2, original.ChainDepth);
        Assert.DoesNotContain("agent-c", original.VisitedAgents);
        Assert.Single(original.HandoffHistory);
    }

    [Fact]
    public void Fork_preserves_wall_clock_deadline()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        var original = new AgentExecutionContext { WallClockDeadline = deadline };

        var fork = original.Fork();

        Assert.Equal(deadline, fork.WallClockDeadline);
    }

    [Fact]
    public void Fork_preserves_cycle_policy()
    {
        var original = new AgentExecutionContext
        {
            CyclePolicy = CyclePolicy.AllowWithLimit(5)
        };

        var fork = original.Fork();

        Assert.Equal(CyclePolicyMode.AllowWithLimit, fork.CyclePolicy.Mode);
        Assert.Equal(5, fork.CyclePolicy.MaxRevisits);
    }

    [Fact]
    public void Default_values_are_sensible()
    {
        var ctx = new AgentExecutionContext();

        Assert.Equal(0, ctx.ChainDepth);
        Assert.Equal(0, ctx.DelegationDepth);
        Assert.Equal(0, ctx.TotalTokensConsumed);
        Assert.Equal(0, ctx.GlobalBudgetRemaining);
        Assert.Empty(ctx.HandoffHistory);
        Assert.Empty(ctx.VisitedAgents);
        Assert.Null(ctx.WallClockDeadline);
        Assert.Equal(CyclePolicyMode.Deny, ctx.CyclePolicy.Mode);
        Assert.Equal(string.Empty, ctx.OriginatorRunId);
        Assert.Null(ctx.ParentAgentId);
    }

    [Fact]
    public void VisitedAgents_is_case_insensitive()
    {
        var ctx = new AgentExecutionContext();
        ctx.VisitedAgents.Add("Agent-A");

        Assert.Contains("agent-a", ctx.VisitedAgents);
        Assert.Contains("AGENT-A", ctx.VisitedAgents);
    }
}

public class CyclePolicyTests
{
    [Fact]
    public void Deny_has_correct_mode()
    {
        var policy = CyclePolicy.Deny;

        Assert.Equal(CyclePolicyMode.Deny, policy.Mode);
        Assert.Equal(0, policy.MaxRevisits);
    }

    [Fact]
    public void Allow_has_correct_mode()
    {
        var policy = CyclePolicy.Allow;

        Assert.Equal(CyclePolicyMode.Allow, policy.Mode);
    }

    [Fact]
    public void AllowWithLimit_has_correct_mode_and_limit()
    {
        var policy = CyclePolicy.AllowWithLimit(3);

        Assert.Equal(CyclePolicyMode.AllowWithLimit, policy.Mode);
        Assert.Equal(3, policy.MaxRevisits);
    }
}

public class AgentHandoffRecordTests
{
    [Fact]
    public void Record_stores_all_fields()
    {
        var ts = DateTimeOffset.UtcNow;
        var record = new AgentHandoffRecord
        {
            FromAgent = "a",
            ToAgent = "b",
            Intent = "review",
            Timestamp = ts,
            ChainDepth = 2,
            TokensConsumedAtHandoff = 1000
        };

        Assert.Equal("a", record.FromAgent);
        Assert.Equal("b", record.ToAgent);
        Assert.Equal("review", record.Intent);
        Assert.Equal(ts, record.Timestamp);
        Assert.Equal(2, record.ChainDepth);
        Assert.Equal(1000, record.TokensConsumedAtHandoff);
    }
}