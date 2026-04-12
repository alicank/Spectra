using Spectra.Contracts.Diagnostics;
using Xunit;

namespace Spectra.Tests.Mcp;

/// <summary>
/// Ensures MCP OTel tag constants follow the Spectra naming convention.
/// </summary>
public class McpTagsTests
{
    [Theory]
    [InlineData(McpTags.ServerName)]
    [InlineData(McpTags.ToolName)]
    [InlineData(McpTags.Transport)]
    [InlineData(McpTags.RequestId)]
    [InlineData(McpTags.CallDuration)]
    [InlineData(McpTags.CallSuccess)]
    [InlineData(McpTags.ErrorCode)]
    [InlineData(McpTags.RetryCount)]
    [InlineData(McpTags.ResponseSize)]
    [InlineData(McpTags.CallsRemaining)]
    public void McpTags_FollowSpectraNamespace(string tag)
    {
        Assert.StartsWith("spectra.mcp.", tag);
    }

    [Theory]
    [InlineData(McpTags.ServerName)]
    [InlineData(McpTags.ToolName)]
    [InlineData(McpTags.Transport)]
    [InlineData(McpTags.RequestId)]
    [InlineData(McpTags.CallDuration)]
    [InlineData(McpTags.CallSuccess)]
    [InlineData(McpTags.ErrorCode)]
    [InlineData(McpTags.RetryCount)]
    [InlineData(McpTags.ResponseSize)]
    [InlineData(McpTags.CallsRemaining)]
    public void McpTags_UseDotSeparatedLowerCase(string tag)
    {
        Assert.DoesNotContain(" ", tag);
        Assert.Equal(tag.ToLowerInvariant(), tag);
    }

    [Fact]
    public void McpTags_AreDistinctFromCoreTags()
    {
        // Ensure no collisions with core SpectraTags
        var mcpTags = new[]
        {
            McpTags.ServerName, McpTags.ToolName, McpTags.Transport,
            McpTags.RequestId, McpTags.CallDuration, McpTags.CallSuccess,
            McpTags.ErrorCode, McpTags.RetryCount, McpTags.ResponseSize,
            McpTags.CallsRemaining
        };

        var coreTags = new[]
        {
            SpectraTags.WorkflowId, SpectraTags.RunId, SpectraTags.NodeId,
            SpectraTags.StepType, SpectraTags.StepStatus, SpectraTags.ErrorMessage,
            SpectraTags.BatchSize
        };

        foreach (var mcp in mcpTags)
            Assert.DoesNotContain(mcp, coreTags);
    }
}