using Spectra.Contracts.Providers;
using Spectra.Kernel.Caching;
using Xunit;

namespace Spectra.Tests.Caching;

public class CacheKeyGeneratorTests
{
    [Fact]
    public void Generate_SameRequest_ProducesSameKey()
    {
        var request = MakeRequest("Hello");

        var key1 = CacheKeyGenerator.Generate("test:", request);
        var key2 = CacheKeyGenerator.Generate("test:", request);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Generate_DifferentContent_ProducesDifferentKeys()
    {
        var key1 = CacheKeyGenerator.Generate("test:", MakeRequest("Hello"));
        var key2 = CacheKeyGenerator.Generate("test:", MakeRequest("World"));

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Generate_DifferentModels_ProducesDifferentKeys()
    {
        var r1 = new LlmRequest { Model = "gpt-4", Messages = [LlmMessage.FromText(LlmRole.User, "Hi")] };
        var r2 = new LlmRequest { Model = "claude-3", Messages = [LlmMessage.FromText(LlmRole.User, "Hi")] };

        var key1 = CacheKeyGenerator.Generate("test:", r1);
        var key2 = CacheKeyGenerator.Generate("test:", r2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Generate_IncludesPrefix()
    {
        var key = CacheKeyGenerator.Generate("myprefix:", MakeRequest("Hello"));

        Assert.StartsWith("myprefix:", key);
    }

    [Fact]
    public void Generate_IncludesModelInKey()
    {
        var key = CacheKeyGenerator.Generate("test:", MakeRequest("Hello", model: "gpt-4o"));

        Assert.Contains("gpt-4o", key);
    }

    [Fact]
    public void Generate_DifferentTemperatures_ProduceDifferentKeys()
    {
        var r1 = new LlmRequest
        {
            Model = "test",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            Temperature = 0.0
        };
        var r2 = new LlmRequest
        {
            Model = "test",
            Messages = [LlmMessage.FromText(LlmRole.User, "Hi")],
            Temperature = 1.0
        };

        Assert.NotEqual(
            CacheKeyGenerator.Generate("test:", r1),
            CacheKeyGenerator.Generate("test:", r2));
    }

    private static LlmRequest MakeRequest(string content, string model = "test-model")
        => new()
        {
            Model = model,
            Messages = [LlmMessage.FromText(LlmRole.User, content)]
        };
}