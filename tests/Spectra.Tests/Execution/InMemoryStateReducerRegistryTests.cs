using Spectra.Contracts.State;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class InMemoryStateReducerRegistryTests
{
    [Fact]
    public void Register_and_Get_returns_reducer()
    {
        var registry = new InMemoryStateReducerRegistry();
        var reducer = new FakeReducer("messages");

        registry.Register(reducer);

        Assert.Same(reducer, registry.Get("messages"));
    }

    [Fact]
    public void Get_returns_null_for_unknown()
    {
        var registry = new InMemoryStateReducerRegistry();
        Assert.Null(registry.Get("nope"));
    }

    [Fact]
    public void Get_is_case_insensitive()
    {
        var registry = new InMemoryStateReducerRegistry();
        registry.Register(new FakeReducer("Messages"));

        Assert.NotNull(registry.Get("messages"));
        Assert.NotNull(registry.Get("MESSAGES"));
    }

    [Fact]
    public void Register_overwrites_existing_reducer()
    {
        var registry = new InMemoryStateReducerRegistry();
        var first = new FakeReducer("messages");
        var second = new FakeReducer("messages");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.Get("messages"));
    }

    [Fact]
    public void Register_throws_on_null()
    {
        var registry = new InMemoryStateReducerRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void Get_throws_on_null()
    {
        var registry = new InMemoryStateReducerRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Get(null!));
    }

    private class FakeReducer : IStateReducer
    {
        public FakeReducer(string key) => Key = key;

        public string Key { get; }

        public object? Reduce(object? currentValue, object? incomingValue) => incomingValue;
    }
}