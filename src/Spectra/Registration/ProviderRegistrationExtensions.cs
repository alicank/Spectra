using Spectra.Extensions.Providers.OpenAiCompatible;
using Spectra.Extensions.Providers.Anthropic;
using Spectra.Extensions.Providers.Gemini;
using Spectra.Extensions.Providers.Ollama;
using Spectra.Extensions.Providers.OpenRouter;

namespace Spectra.Registration;

public static class ProviderRegistrationExtensions
{
    /// <summary>
    /// Registers an OpenAI provider with the given configuration.
    /// </summary>
    public static SpectraBuilder AddOpenAi(this SpectraBuilder builder, Action<OpenAiConfig> configure)
    {
        var config = new OpenAiConfig { ProviderName = "openai" };
        configure(config);
        builder.AddProvider(new OpenAiCompatibleProvider(config));
        return builder;
    }

    /// <summary>
    /// Registers an Ollama provider. Defaults to http://localhost:11434/v1.
    /// </summary>
    /// <summary>
    /// Registers an Ollama provider using the native /api/chat endpoint.
    /// Defaults to http://localhost:11434 with model "llama3".
    /// </summary>
    public static SpectraBuilder AddOllama(this SpectraBuilder builder, Action<OllamaConfig> configure)
    {
        var config = new OllamaConfig();
        configure(config);
        builder.AddProvider(new OllamaProvider(config));
        return builder;
    }

    /// <summary>
    /// Registers any OpenAI-compatible provider by name.
    /// Use this for DeepSeek, Mistral, Together, Fireworks, Groq, or any custom endpoint.
    /// </summary>
    public static SpectraBuilder AddProvider(
        this SpectraBuilder builder,
        string providerName,
        Action<OpenAiConfig> configure)
    {
        var config = new OpenAiConfig { ProviderName = providerName };
        configure(config);
        builder.AddProvider(new OpenAiCompatibleProvider(config));
        return builder;
    }

    /// <summary>
    /// Registers an Anthropic Claude provider with the given configuration.
    /// </summary>
    public static SpectraBuilder AddAnthropic(this SpectraBuilder builder, Action<AnthropicConfig> configure)
    {
        var config = new AnthropicConfig();
        configure(config);
        builder.AddProvider(new AnthropicProvider(config));
        return builder;
    }

    /// <summary>
    /// Registers a Google Gemini provider with the given configuration.
    /// </summary>
    public static SpectraBuilder AddGemini(this SpectraBuilder builder, Action<GeminiConfig> configure)
    {
        var config = new GeminiConfig();
        configure(config);
        builder.AddProvider(new GeminiProvider(config));
        return builder;
    }

    /// <summary>
    /// Registers an OpenRouter provider with the given configuration.
    /// Routes to 300+ models from OpenAI, Anthropic, Google, Meta, and more through a single API.
    /// </summary>
    public static SpectraBuilder AddOpenRouter(this SpectraBuilder builder, Action<OpenRouterConfig> configure)
    {
        var config = new OpenRouterConfig();
        configure(config);
        builder.AddProvider(new OpenRouterProvider(config));
        return builder;
    }
}