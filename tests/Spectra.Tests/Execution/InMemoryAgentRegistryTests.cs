using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class InMemoryAgentRegistryTests
{
    [Fact]
    public void Register_and_GetAgent_returns_agent()
    {
        var registry = new InMemoryAgentRegistry();
        var agent = CreateAgent("coder");

        registry.Register(agent);

        Assert.Same(agent, registry.GetAgent("coder"));
    }

    [Fact]
    public void GetAgent_returns_null_for_unknown()
    {
        var registry = new InMemoryAgentRegistry();
        Assert.Null(registry.GetAgent("nope"));
    }

    [Fact]
    public void GetAgent_is_case_insensitive()
    {
        var registry = new InMemoryAgentRegistry();
        registry.Register(CreateAgent("Coder"));

        Assert.NotNull(registry.GetAgent("coder"));
        Assert.NotNull(registry.GetAgent("CODER"));
    }

    [Fact]
    public void Register_overwrites_existing_agent()
    {
        var registry = new InMemoryAgentRegistry();
        var first = CreateAgent("coder");
        var second = CreateAgent("coder");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.GetAgent("coder"));
    }

    [Fact]
    public void GetAll_returns_all_registered_agents()
    {
        var registry = new InMemoryAgentRegistry();
        registry.Register(CreateAgent("coder"));
        registry.Register(CreateAgent("reviewer"));

        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetAll_returns_empty_when_none_registered()
    {
        var registry = new InMemoryAgentRegistry();
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void Register_throws_on_null()
    {
        var registry = new InMemoryAgentRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void GetAgent_throws_on_null()
    {
        var registry = new InMemoryAgentRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.GetAgent(null!));
    }

    private static AgentDefinition CreateAgent(string id) =>
        new() { Id = id, Provider = "openai", Model = "gpt-4" };
}