using Spectra.Contracts.Memory;
using Xunit;

namespace Spectra.Tests.Memory;

public class MemoryNamespaceTests
{
    [Fact]
    public void Global_ReturnsGlobal()
    {
        Assert.Equal("global", MemoryNamespace.Global);
    }

    [Fact]
    public void ForUser_FormatsCorrectly()
    {
        Assert.Equal("user.alice", MemoryNamespace.ForUser("Alice"));
    }

    [Fact]
    public void ForWorkflow_FormatsCorrectly()
    {
        Assert.Equal("workflow.my-flow", MemoryNamespace.ForWorkflow("my-flow"));
    }

    [Fact]
    public void ForRun_FormatsCorrectly()
    {
        var runId = "abc-123";
        Assert.Equal("run.abc-123", MemoryNamespace.ForRun(runId));
    }

    [Fact]
    public void Compose_JoinsDotSeparated()
    {
        var result = MemoryNamespace.Compose("tenant", "acme", "user", "bob");
        Assert.Equal("tenant.acme.user.bob", result);
    }

    [Fact]
    public void Compose_EmptySegment_Throws()
    {
        Assert.Throws<ArgumentException>(() => MemoryNamespace.Compose("a", "", "b"));
    }

    [Fact]
    public void Compose_NoSegments_Throws()
    {
        Assert.Throws<ArgumentException>(() => MemoryNamespace.Compose());
    }

    [Theory]
    [InlineData("global", true)]
    [InlineData("user.alice", true)]
    [InlineData("tenant.acme.user.bob", true)]
    [InlineData("with-dashes_and_underscores", true)]
    [InlineData("", false)]
    [InlineData(".leading-dot", false)]
    [InlineData("trailing-dot.", false)]
    [InlineData("double..dot", false)]
    [InlineData("has space", false)]
    public void IsValid_ReturnsExpected(string ns, bool expected)
    {
        Assert.Equal(expected, MemoryNamespace.IsValid(ns));
    }
}