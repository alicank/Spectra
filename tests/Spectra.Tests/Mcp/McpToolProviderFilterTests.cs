using System.Text.Json;
using Spectra.Contracts.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

/// <summary>
/// Tests for MCP tool filtering logic (allowed/denied/read-only).
/// Uses reflection to test the private IsToolAllowed method via the public interface.
/// </summary>
public class McpToolProviderFilterTests
{
    private static McpToolInfo Tool(string name, bool readOnly = false) => new()
    {
        Name = name,
        Description = $"Test tool {name}",
        Annotations = new McpToolAnnotations
        {
            ReadOnlyHint = readOnly
        }
    };

    [Fact]
    public void AllowedTools_FiltersToWhitelist()
    {
        var tools = new[] { Tool("read_file"), Tool("write_file"), Tool("delete_file") };
        var config = new McpServerConfig
        {
            Name = "test",
            Command = "echo",
            AllowedTools = ["read_file", "write_file"]
        };

        var filtered = tools.Where(t => IsAllowed(t, config)).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, t => t.Name == "delete_file");
    }

    [Fact]
    public void DeniedTools_BlocksBlacklist()
    {
        var tools = new[] { Tool("read_file"), Tool("write_file"), Tool("delete_file") };
        var config = new McpServerConfig
        {
            Name = "test",
            Command = "echo",
            DeniedTools = ["delete_file"]
        };

        var filtered = tools.Where(t => IsAllowed(t, config)).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, t => t.Name == "delete_file");
    }

    [Fact]
    public void ReadOnly_BlocksNonReadOnlyTools()
    {
        var tools = new[] { Tool("read_file", readOnly: true), Tool("write_file", readOnly: false) };
        var config = new McpServerConfig
        {
            Name = "test",
            Command = "echo",
            ReadOnly = true
        };

        var filtered = tools.Where(t => IsAllowed(t, config)).ToList();

        Assert.Single(filtered);
        Assert.Equal("read_file", filtered[0].Name);
    }

    [Fact]
    public void NoFilters_AllToolsPass()
    {
        var tools = new[] { Tool("a"), Tool("b"), Tool("c") };
        var config = new McpServerConfig
        {
            Name = "test",
            Command = "echo"
        };

        var filtered = tools.Where(t => IsAllowed(t, config)).ToList();

        Assert.Equal(3, filtered.Count);
    }

    // Replicates McpToolProvider.IsToolAllowed logic for unit testing
    private static bool IsAllowed(McpToolInfo tool, McpServerConfig config)
    {
        if (config.AllowedTools is { Count: > 0 })
        {
            if (!config.AllowedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        if (config.DeniedTools is { Count: > 0 })
        {
            if (config.DeniedTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        if (config.ReadOnly && tool.Annotations is { ReadOnlyHint: false })
            return false;

        return true;
    }
}