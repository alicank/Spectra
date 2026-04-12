using System.Text.Json;
using Spectra.Contracts.Mcp;
using Xunit;

namespace Spectra.Tests.Mcp;

public class JsonRpcTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ── Request serialization ──

    [Fact]
    public void Request_SerializesWithRequiredFields()
    {
        var request = new JsonRpcRequest
        {
            Id = 1,
            Method = "tools/list",
            Params = new { cursor = (string?)null }
        };

        var json = JsonSerializer.Serialize(request, Options);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("tools/list", doc.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void Request_OmitsParams_WhenNull()
    {
        var request = new JsonRpcRequest { Id = 1, Method = "ping" };

        var json = JsonSerializer.Serialize(request, Options);
        var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("params", out _));
    }

    [Fact]
    public void Request_OmitsId_WhenNull_ForNotifications()
    {
        var request = new JsonRpcRequest
        {
            Id = null,
            Method = "notifications/initialized"
        };

        var json = JsonSerializer.Serialize(request, Options);
        var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("id", out _));
    }

    // ── Response deserialization ──

    [Fact]
    public void Response_DeserializesSuccessResult()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "id": 1,
            "result": { "tools": [] }
        }
        """;

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, Options);

        Assert.NotNull(response);
        Assert.True(response!.IsSuccess);
        Assert.Equal(1, response.Id);
        Assert.NotNull(response.Result);
    }

    [Fact]
    public void Response_DeserializesError()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "id": 2,
            "error": {
                "code": -32601,
                "message": "Method not found"
            }
        }
        """;

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, Options);

        Assert.NotNull(response);
        Assert.False(response!.IsSuccess);
        Assert.Equal(-32601, response.Error!.Code);
        Assert.Equal("Method not found", response.Error.Message);
    }

    [Fact]
    public void Response_ErrorWithData()
    {
        var json = """
        {
            "jsonrpc": "2.0",
            "id": 3,
            "error": {
                "code": -32602,
                "message": "Invalid params",
                "data": "Expected 'name' field"
            }
        }
        """;

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, Options);

        Assert.NotNull(response!.Error!.Data);
    }

    [Fact]
    public void Response_IsSuccess_TrueWhenNoError()
    {
        var response = new JsonRpcResponse { Id = 1 };

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void Response_IsSuccess_FalseWhenErrorPresent()
    {
        var response = new JsonRpcResponse
        {
            Id = 1,
            Error = new JsonRpcError { Code = -1, Message = "fail" }
        };

        Assert.False(response.IsSuccess);
    }
}