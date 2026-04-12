using Spectra.Contracts.Execution;
using Spectra.Contracts.Providers;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class InMemoryProviderRegistryTests
{
    [Fact]
    public void Register_and_GetProvider_returns_provider()
    {
        var registry = new InMemoryProviderRegistry();
        var provider = new FakeProvider("openai");

        registry.Register(provider);

        Assert.Same(provider, registry.GetProvider("openai"));
    }

    [Fact]
    public void GetProvider_returns_null_for_unknown()
    {
        var registry = new InMemoryProviderRegistry();
        Assert.Null(registry.GetProvider("nope"));
    }

    [Fact]
    public void GetProvider_is_case_insensitive()
    {
        var registry = new InMemoryProviderRegistry();
        registry.Register(new FakeProvider("OpenAI"));

        Assert.NotNull(registry.GetProvider("openai"));
        Assert.NotNull(registry.GetProvider("OPENAI"));
    }

    [Fact]
    public void Register_overwrites_existing_provider()
    {
        var registry = new InMemoryProviderRegistry();
        var first = new FakeProvider("openai");
        var second = new FakeProvider("openai");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.GetProvider("openai"));
    }

    [Fact]
    public void CreateClient_returns_client_from_matching_provider()
    {
        var registry = new InMemoryProviderRegistry();
        var provider = new FakeProvider("openai");
        registry.Register(provider);

        var agent = new AgentDefinition { Id = "a1", Provider = "openai", Model = "gpt-4" };
        var client = registry.CreateClient(agent);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_falls_back_to_model_match()
    {
        var registry = new InMemoryProviderRegistry();
        var provider = new FakeProvider("openai", supportedModel: "gpt-4");
        registry.Register(provider);

        var agent = new AgentDefinition { Id = "a1", Provider = "unknown", Model = "gpt-4" };
        var client = registry.CreateClient(agent);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_returns_null_when_no_provider_matches()
    {
        var registry = new InMemoryProviderRegistry();

        var agent = new AgentDefinition { Id = "a1", Provider = "missing", Model = "gpt-4" };

        Assert.Null(registry.CreateClient(agent));
    }

    [Fact]
    public void Register_throws_on_null()
    {
        var registry = new InMemoryProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void GetProvider_throws_on_null()
    {
        var registry = new InMemoryProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.GetProvider(null!));
    }

    [Fact]
    public void CreateClient_throws_on_null()
    {
        var registry = new InMemoryProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.CreateClient(null!));
    }

    private class FakeProvider : ILlmProvider
    {
        private readonly string? _supportedModel;

        public FakeProvider(string name, string? supportedModel = null)
        {
            Name = name;
            _supportedModel = supportedModel;
        }

        public string Name { get; }

        public ILlmClient CreateClient(AgentDefinition agent) => new FakeClient();

        public bool SupportsModel(string modelId) =>
            _supportedModel != null &&
            string.Equals(_supportedModel, modelId, StringComparison.OrdinalIgnoreCase);
    }

    private class FakeClient : ILlmClient
    {
        public string ProviderName => "fake";
        public string ModelId => "fake-model";
        public ModelCapabilities Capabilities => new();

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "fake" });
    }
}