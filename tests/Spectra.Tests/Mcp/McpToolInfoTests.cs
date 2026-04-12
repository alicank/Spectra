using System.Text.Json;
using Spectra.Contracts.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpToolInfoTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Deserializes_MinimalToolInfo()
    {
        var json = """{ "name": "read_file" }""";

        var tool = JsonSerializer.Deserialize<McpToolInfo>(json, Options);

        Assert.NotNull(tool);
        Assert.Equal("read_file", tool!.Name);
        Assert.Null(tool.Description);
        Assert.Null(tool.InputSchema);
        Assert.Null(tool.Annotations);
    }

    [Fact]
    public void Deserializes_FullToolInfo()
    {
        var json = """
        {
            "name": "write_file",
            "description": "Write content to a file.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "content": { "type": "string" }
                },
                "required": ["path", "content"]
            },
            "annotations": {
                "title": "Write File",
                "readOnlyHint": false,
                "destructiveHint": true,
                "idempotentHint": true,
                "openWorldHint": true
            }
        }
        """;

        var tool = JsonSerializer.Deserialize<McpToolInfo>(json, Options);

        Assert.NotNull(tool);
        Assert.Equal("write_file", tool!.Name);
        Assert.Equal("Write content to a file.", tool.Description);
        Assert.NotNull(tool.InputSchema);
        Assert.NotNull(tool.Annotations);
        Assert.Equal("Write File", tool.Annotations!.Title);
        Assert.False(tool.Annotations.ReadOnlyHint);
        Assert.True(tool.Annotations.DestructiveHint);
        Assert.True(tool.Annotations.IdempotentHint);
        Assert.True(tool.Annotations.OpenWorldHint);
    }

    [Fact]
    public void Annotations_DefaultValues_AreCorrect()
    {
        var annotations = new McpToolAnnotations();

        Assert.Null(annotations.Title);
        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint); // default true per MCP spec
    }
}