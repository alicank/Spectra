using System.Text;
using Spectra.Extensions.Providers.Shared;
using Xunit;

namespace Spectra.Tests.Providers;

public class SseReaderTests
{
    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<List<string>> CollectAsync(Stream stream)
    {
        var items = new List<string>();
        await foreach (var item in SseReader.ReadAsync(stream))
            items.Add(item);
        return items;
    }

    [Fact]
    public async Task Yields_DataLines()
    {
        var stream = ToStream("data: hello\ndata: world\n\n");

        var result = await CollectAsync(stream);

        Assert.Equal(2, result.Count);
        Assert.Equal("hello", result[0]);
        Assert.Equal("world", result[1]);
    }

    [Fact]
    public async Task Stops_OnDoneSentinel()
    {
        var stream = ToStream("data: first\ndata: second\ndata: [DONE]\ndata: should-not-appear\n");

        var result = await CollectAsync(stream);

        Assert.Equal(2, result.Count);
        Assert.Equal("first", result[0]);
        Assert.Equal("second", result[1]);
    }

    [Fact]
    public async Task Skips_EmptyLines()
    {
        var stream = ToStream("\n\ndata: value\n\n");

        var result = await CollectAsync(stream);

        Assert.Single(result);
        Assert.Equal("value", result[0]);
    }

    [Fact]
    public async Task Skips_CommentLines()
    {
        var stream = ToStream(": this is a comment\ndata: actual\n");

        var result = await CollectAsync(stream);

        Assert.Single(result);
        Assert.Equal("actual", result[0]);
    }

    [Fact]
    public async Task Skips_NonDataFields()
    {
        var stream = ToStream("event: message\ndata: payload\nid: 123\n\n");

        var result = await CollectAsync(stream);

        Assert.Single(result);
        Assert.Equal("payload", result[0]);
    }

    [Fact]
    public async Task Returns_Empty_OnEmptyStream()
    {
        var stream = ToStream("");

        var result = await CollectAsync(stream);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handles_JsonPayloads()
    {
        var stream = ToStream("""
            data: {"choices":[{"delta":{"content":"Hi"}}]}
            data: {"choices":[{"delta":{"content":" there"}}]}
            data: [DONE]
            """);

        var result = await CollectAsync(stream);

        Assert.Equal(2, result.Count);
        Assert.Contains("Hi", result[0]);
        Assert.Contains("there", result[1]);
    }
}