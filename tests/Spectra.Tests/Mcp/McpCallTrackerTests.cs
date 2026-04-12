using Spectra.Contracts.Mcp;
using Spectra.Kernel.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpCallTrackerTests
{
    private static McpServerConfig CreateConfig(
        string name = "test",
        int maxCalls = 0,
        decimal costPerCall = 0)
        => new()
        {
            Name = name,
            Command = "echo",
            MaxCallsPerSession = maxCalls,
            CostPerCall = costPerCall
        };

    [Fact]
    public void CheckAllowed_NoLimits_ReturnsNull()
    {
        var tracker = new McpCallTracker();
        var config = CreateConfig();

        var result = tracker.CheckAllowed(config, "tool1", 0);

        Assert.Null(result);
    }

    [Fact]
    public void CheckAllowed_ExceedsRateLimit_ReturnsError()
    {
        var tracker = new McpCallTracker();
        var config = CreateConfig(maxCalls: 2);

        tracker.RecordCall(config, "tool1");
        tracker.RecordCall(config, "tool2");

        var result = tracker.CheckAllowed(config, "tool3", 0);

        Assert.NotNull(result);
        Assert.Contains("maximum of 2 calls", result);
    }

    [Fact]
    public void CheckAllowed_ExceedsBudget_ReturnsError()
    {
        var tracker = new McpCallTracker();
        var config = CreateConfig(costPerCall: 5.0m);

        tracker.RecordCall(config, "tool1"); // cost = 5

        var result = tracker.CheckAllowed(config, "tool2", globalBudgetRemaining: 8.0m);

        Assert.NotNull(result);
        Assert.Contains("exceed", result);
    }

    [Fact]
    public void CheckAllowed_WithinBudget_ReturnsNull()
    {
        var tracker = new McpCallTracker();
        var config = CreateConfig(costPerCall: 3.0m);

        tracker.RecordCall(config, "tool1"); // cost = 3

        var result = tracker.CheckAllowed(config, "tool2", globalBudgetRemaining: 10.0m);

        Assert.Null(result);
    }

    [Fact]
    public void GetCallCount_TracksPerServer()
    {
        var tracker = new McpCallTracker();
        var config = CreateConfig("server-a");

        tracker.RecordCall(config, "tool1");
        tracker.RecordCall(config, "tool2");

        Assert.Equal(2, tracker.GetCallCount("server-a"));
        Assert.Equal(0, tracker.GetCallCount("server-b"));
    }

    [Fact]
    public void EstimatedCost_AccumulatesAcrossServers()
    {
        var tracker = new McpCallTracker();
        var configA = CreateConfig("a", costPerCall: 1.5m);
        var configB = CreateConfig("b", costPerCall: 2.0m);

        tracker.RecordCall(configA, "tool1");
        tracker.RecordCall(configB, "tool2");

        Assert.Equal(3.5m, tracker.EstimatedCost);
    }
}