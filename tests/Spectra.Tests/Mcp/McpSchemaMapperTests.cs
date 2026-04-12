using System.Text.Json;
using Spectra.Contracts.Mcp;
using Spectra.Kernel.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class McpSchemaMapperTests
{
    [Fact]
    public void ToToolDefinition_MapsNameWithServerPrefix()
    {
        var tool = new McpToolInfo
        {
            Name = "read_file",
            Description = "Read a file from disk."
        };

        var definition = McpSchemaMapper.ToToolDefinition(tool, "filesystem");

        Assert.Equal("mcp__filesystem__read_file", definition.Name);
        Assert.Equal("Read a file from disk.", definition.Description);
    }

    [Fact]
    public void ToToolDefinition_FlattensJsonSchemaProperties()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path to read" },
                "encoding": { "type": "string", "description": "Text encoding" }
            },
            "required": ["path"]
        }
        """).RootElement;

        var tool = new McpToolInfo
        {
            Name = "read_file",
            Description = "Read a file.",
            InputSchema = schema
        };

        var definition = McpSchemaMapper.ToToolDefinition(tool, "fs");

        Assert.Equal(2, definition.Parameters.Count);

        var pathParam = definition.Parameters.First(p => p.Name == "path");
        Assert.Equal("string", pathParam.Type);
        Assert.Equal("File path to read", pathParam.Description);
        Assert.True(pathParam.Required);

        var encodingParam = definition.Parameters.First(p => p.Name == "encoding");
        Assert.False(encodingParam.Required);
    }

    [Fact]
    public void ToToolDefinition_HandlesNoSchema()
    {
        var tool = new McpToolInfo
        {
            Name = "ping",
            Description = "Ping the server."
        };

        var definition = McpSchemaMapper.ToToolDefinition(tool, "test");

        Assert.Empty(definition.Parameters);
    }

    [Fact]
    public void ParseToolResult_ExtractsTextContent()
    {
        var result = JsonDocument.Parse("""
        {
            "content": [
                { "type": "text", "text": "Hello world" }
            ]
        }
        """).RootElement;

        var (content, isError) = McpSchemaMapper.ParseToolResult(result);

        Assert.Equal("Hello world", content);
        Assert.False(isError);
    }

    [Fact]
    public void ParseToolResult_DetectsErrorFlag()
    {
        var result = JsonDocument.Parse("""
        {
            "isError": true,
            "content": [
                { "type": "text", "text": "File not found" }
            ]
        }
        """).RootElement;

        var (content, isError) = McpSchemaMapper.ParseToolResult(result);

        Assert.Equal("File not found", content);
        Assert.True(isError);
    }

    [Fact]
    public void ParseToolResult_ConcatenatesMultipleTextBlocks()
    {
        var result = JsonDocument.Parse("""
        {
            "content": [
                { "type": "text", "text": "Line 1" },
                { "type": "text", "text": "Line 2" }
            ]
        }
        """).RootElement;

        var (content, _) = McpSchemaMapper.ParseToolResult(result);

        Assert.Equal("Line 1\nLine 2", content);
    }
}