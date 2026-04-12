using Spectra.Contracts.Events;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpEventsTests
{
    [Fact]
    public void McpServerConnectedEvent_IsWorkflowEvent()
    {
        var evt = new McpServerConnectedEvent
        {
            RunId = "r1",
            WorkflowId = "w1",
            EventType = "mcp.server_connected",
            ServerName = "filesystem",
            Transport = "stdio",
            ToolCount = 3,
            ToolNames = ["read_file", "write_file", "list_dir"]
        };

        Assert.IsAssignableFrom<WorkflowEvent>(evt);
        Assert.Equal("filesystem", evt.ServerName);
        Assert.Equal(3, evt.ToolCount);
        Assert.NotEqual(Guid.Empty, evt.EventId);
    }

    [Fact]
    public void McpServerDisconnectedEvent_CapturesReason()
    {
        var evt = new McpServerDisconnectedEvent
        {
            RunId = "r1",
            WorkflowId = "w1",
            EventType = "mcp.server_disconnected",
            ServerName = "github",
            Reason = "process_crashed"
        };

        Assert.Equal("process_crashed", evt.Reason);
    }

    [Fact]
    public void McpToolCallEvent_CapturesMcpSpecificFields()
    {
        var evt = new McpToolCallEvent
        {
            RunId = "r1",
            WorkflowId = "w1",
            EventType = "mcp.tool_call",
            ServerName = "filesystem",
            ToolName = "read_file",
            Transport = "stdio",
            JsonRpcRequestId = 42,
            Duration = TimeSpan.FromMilliseconds(150),
            Success = true,
            RetryCount = 0
        };

        Assert.Equal("filesystem", evt.ServerName);
        Assert.Equal("read_file", evt.ToolName);
        Assert.Equal(42, evt.JsonRpcRequestId);
        Assert.True(evt.Success);
        Assert.Equal(0, evt.RetryCount);
    }

    [Fact]
    public void McpToolCallEvent_CapturesErrorInfo()
    {
        var evt = new McpToolCallEvent
        {
            RunId = "r1",
            WorkflowId = "w1",
            EventType = "mcp.tool_call",
            ServerName = "api",
            ToolName = "query",
            Transport = "sse",
            Duration = TimeSpan.FromSeconds(30),
            Success = false,
            ErrorCode = -32000,
            ErrorMessage = "Internal error",
            RetryCount = 2
        };

        Assert.False(evt.Success);
        Assert.Equal(-32000, evt.ErrorCode);
        Assert.Equal("Internal error", evt.ErrorMessage);
        Assert.Equal(2, evt.RetryCount);
    }

    [Fact]
    public void McpToolCallBlockedEvent_CapturesBlockReason()
    {
        var evt = new McpToolCallBlockedEvent
        {
            RunId = "r1",
            WorkflowId = "w1",
            EventType = "mcp.tool_call_blocked",
            ServerName = "production-db",
            ToolName = "drop_table",
            Reason = "read_only_policy"
        };

        Assert.Equal("drop_table", evt.ToolName);
        Assert.Equal("read_only_policy", evt.Reason);
    }

    [Fact]
    public void AllMcpEvents_HaveTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var events = new WorkflowEvent[]
        {
            new McpServerConnectedEvent
            {
                RunId = "r", WorkflowId = "w", EventType = "t",
                ServerName = "s", Transport = "stdio", ToolCount = 0, ToolNames = []
            },
            new McpServerDisconnectedEvent
            {
                RunId = "r", WorkflowId = "w", EventType = "t",
                ServerName = "s", Reason = "normal"
            },
            new McpToolCallEvent
            {
                RunId = "r", WorkflowId = "w", EventType = "t",
                ServerName = "s", ToolName = "t", Transport = "stdio"
            },
            new McpToolCallBlockedEvent
            {
                RunId = "r", WorkflowId = "w", EventType = "t",
                ServerName = "s", ToolName = "t", Reason = "test"
            }
        };

        var after = DateTimeOffset.UtcNow;

        foreach (var evt in events)
        {
            Assert.True(evt.Timestamp >= before && evt.Timestamp <= after,
                $"{evt.GetType().Name}.Timestamp out of range");
        }
    }

    [Fact]
    public void AllMcpEvents_HaveUniqueEventIds()
    {
        var evt1 = new McpToolCallEvent
        {
            RunId = "r",
            WorkflowId = "w",
            EventType = "t",
            ServerName = "s",
            ToolName = "t",
            Transport = "stdio"
        };
        var evt2 = new McpToolCallEvent
        {
            RunId = "r",
            WorkflowId = "w",
            EventType = "t",
            ServerName = "s",
            ToolName = "t",
            Transport = "stdio"
        };

        Assert.NotEqual(evt1.EventId, evt2.EventId);
    }
}