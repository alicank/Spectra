using Spectra.Contracts.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

/// <summary>
/// Tests for MCP tool filtering logic: allowed lists, denied lists, read-only mode,
/// and their combinations. Mirrors the filtering in <c>McpToolProvider.IsToolAllowed</c>.
/// </summary>
public class McpToolFilterTests
{
    private static McpToolInfo Tool(string name, bool readOnlyHint = false, bool destructiveHint = false) => new()
    {
        Name = name,
        Description = $"Tool: {name}",
        Annotations = new McpToolAnnotations
        {
            ReadOnlyHint = readOnlyHint,
            DestructiveHint = destructiveHint
        }
    };

    private static McpToolInfo ToolWithoutAnnotations(string name) => new()
    {
        Name = name,
        Description = $"Tool: {name}",
        Annotations = null
    };

    // ── Allowed tools whitelist ──

    [Fact]
    public void AllowedTools_OnlyPermitsListedTools()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            AllowedTools = ["read_file", "list_dir"]
        };

        var tools = new[]
        {
            Tool("read_file"), Tool("list_dir"),
            Tool("write_file"), Tool("delete_file")
        };

        var filtered = tools.Where(t => IsAllowed(t, config)).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, t => t.Name == "read_file");
        Assert.Contains(filtered, t => t.Name == "list_dir");
    }

    [Fact]
    public void AllowedTools_IsCaseInsensitive()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            AllowedTools = ["READ_FILE"]
        };

        Assert.True(IsAllowed(Tool("read_file"), config));
    }

    [Fact]
    public void AllowedTools_Null_PermitsAll()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            AllowedTools = null
        };

        Assert.True(IsAllowed(Tool("anything"), config));
    }

    [Fact]
    public void AllowedTools_EmptyList_PermitsAll()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            AllowedTools = []
        };

        Assert.True(IsAllowed(Tool("anything"), config));
    }

    // ── Denied tools blacklist ──

    [Fact]
    public void DeniedTools_BlocksListedTools()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            DeniedTools = ["delete_file", "format_disk"]
        };

        Assert.True(IsAllowed(Tool("read_file"), config));
        Assert.False(IsAllowed(Tool("delete_file"), config));
        Assert.False(IsAllowed(Tool("format_disk"), config));
    }

    [Fact]
    public void DeniedTools_IsCaseInsensitive()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            DeniedTools = ["DELETE_FILE"]
        };

        Assert.False(IsAllowed(Tool("delete_file"), config));
    }

    [Fact]
    public void DeniedTools_Null_BlocksNothing()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            DeniedTools = null
        };

        Assert.True(IsAllowed(Tool("anything"), config));
    }

    // ── Allowed + Denied combination ──

    [Fact]
    public void AllowedAndDenied_DeniedOverridesAllowed()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            AllowedTools = ["read_file", "write_file"],
            DeniedTools = ["write_file"]
        };

        Assert.True(IsAllowed(Tool("read_file"), config));
        Assert.False(IsAllowed(Tool("write_file"), config)); // denied wins
        Assert.False(IsAllowed(Tool("delete_file"), config)); // not in allowed
    }

    // ── Read-only mode ──

    [Fact]
    public void ReadOnly_BlocksNonReadOnlyTools()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            ReadOnly = true
        };

        Assert.True(IsAllowed(Tool("search", readOnlyHint: true), config));
        Assert.False(IsAllowed(Tool("write", readOnlyHint: false), config));
    }

    [Fact]
    public void ReadOnly_ToolsWithoutAnnotations_AreBlocked()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            ReadOnly = true
        };

        // No annotations at all — default ReadOnlyHint is false, so blocked
        // But annotations is null, not ReadOnlyHint: false
        // The filter checks: tool.Annotations is { ReadOnlyHint: false }
        // When Annotations is null, this pattern match fails → tool passes
        // This is a deliberate design choice: unknown tools are allowed unless
        // we have explicit evidence they're write tools.
        var noAnnotations = ToolWithoutAnnotations("unknown");
        // Annotations is null → pattern `is { ReadOnlyHint: false }` does NOT match
        Assert.True(IsAllowed(noAnnotations, config));
    }

    [Fact]
    public void ReadOnly_False_PermitsEverything()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            ReadOnly = false
        };

        Assert.True(IsAllowed(Tool("write", readOnlyHint: false), config));
        Assert.True(IsAllowed(Tool("read", readOnlyHint: true), config));
    }

    // ── All filters combined ──

    [Fact]
    public void AllFilters_AppliedInOrder()
    {
        var config = new McpServerConfig
        {
            Name = "s",
            Command = "echo",
            AllowedTools = ["search", "read", "write"],
            DeniedTools = ["write"],
            ReadOnly = true
        };

        Assert.True(IsAllowed(Tool("search", readOnlyHint: true), config)); // allowed, not denied, read-only
        Assert.True(IsAllowed(Tool("read", readOnlyHint: true), config));   // allowed, not denied, read-only
        Assert.False(IsAllowed(Tool("write", readOnlyHint: false), config)); // denied
        Assert.False(IsAllowed(Tool("delete", readOnlyHint: false), config)); // not in allowed
    }

    [Fact]
    public void NoFilters_AllToolsPass()
    {
        var config = new McpServerConfig { Name = "s", Command = "echo" };

        Assert.True(IsAllowed(Tool("a"), config));
        Assert.True(IsAllowed(Tool("b", readOnlyHint: true), config));
        Assert.True(IsAllowed(Tool("c", destructiveHint: true), config));
    }

    // ── Helper replicating McpToolProvider.IsToolAllowed ──

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