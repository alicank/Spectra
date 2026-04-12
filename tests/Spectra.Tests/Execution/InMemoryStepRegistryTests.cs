using Spectra.Contracts.Steps;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class InMemoryStepRegistryTests
{
    [Fact]
    public void Register_and_GetStep_returns_step()
    {
        var registry = new InMemoryStepRegistry();
        var step = new FakeStep("prompt");

        registry.Register(step);

        Assert.Same(step, registry.GetStep("prompt"));
    }

    [Fact]
    public void GetStep_returns_null_for_unknown()
    {
        var registry = new InMemoryStepRegistry();
        Assert.Null(registry.GetStep("nope"));
    }

    [Fact]
    public void GetStep_is_case_insensitive()
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new FakeStep("Prompt"));

        Assert.NotNull(registry.GetStep("prompt"));
        Assert.NotNull(registry.GetStep("PROMPT"));
    }

    [Fact]
    public void Register_overwrites_existing_step()
    {
        var registry = new InMemoryStepRegistry();
        var first = new FakeStep("prompt");
        var second = new FakeStep("prompt");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.GetStep("prompt"));
    }

    [Fact]
    public void Register_throws_on_null()
    {
        var registry = new InMemoryStepRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void GetStep_throws_on_null()
    {
        var registry = new InMemoryStepRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.GetStep(null!));
    }

    private class FakeStep : IStep
    {
        public FakeStep(string stepType) => StepType = stepType;

        public string StepType { get; }

        public Task<StepResult> ExecuteAsync(StepContext context)
            => Task.FromResult(new StepResult { Status = StepStatus.Succeeded });
    }
}