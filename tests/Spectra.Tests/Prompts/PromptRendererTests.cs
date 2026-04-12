using Spectra.Contracts.Prompts;
using Spectra.Kernel.Prompts;
using Xunit;

namespace Spectra.Tests.Prompts;

public class PromptRendererTests
{
    private readonly PromptRenderer _renderer = new();

    [Fact]
    public void Substitutes_single_variable()
    {
        var result = _renderer.Render(
            "Hello {{name}}!",
            new Dictionary<string, object?> { ["name"] = "World" });

        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Substitutes_multiple_variables()
    {
        var result = _renderer.Render(
            "{{greeting}} {{name}}, welcome to {{place}}.",
            new Dictionary<string, object?>
            {
                ["greeting"] = "Hi",
                ["name"] = "Alican",
                ["place"] = "Spectra"
            });

        Assert.Equal("Hi Alican, welcome to Spectra.", result);
    }

    [Fact]
    public void Leaves_placeholder_when_missing_and_default_behavior()
    {
        var result = _renderer.Render(
            "Hello {{name}}, your role is {{role}}.",
            new Dictionary<string, object?> { ["name"] = "Alican" });

        Assert.Equal("Hello Alican, your role is {{role}}.", result);
    }

    [Fact]
    public void Throws_when_missing_and_throw_behavior()
    {
        var options = new PromptRenderOptions
        {
            MissingVariableBehavior = MissingVariableBehavior.ThrowException
        };

        var ex = Assert.Throws<KeyNotFoundException>(() =>
            _renderer.Render("Hello {{name}}", new Dictionary<string, object?>(), options));

        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Replaces_with_empty_when_missing_and_empty_behavior()
    {
        var options = new PromptRenderOptions
        {
            MissingVariableBehavior = MissingVariableBehavior.ReplaceWithEmpty
        };

        var result = _renderer.Render(
            "Hello {{name}}!",
            new Dictionary<string, object?>(),
            options);

        Assert.Equal("Hello !", result);
    }

    [Fact]
    public void No_variables_returns_template_as_is()
    {
        var result = _renderer.Render(
            "Just plain text.",
            new Dictionary<string, object?>());

        Assert.Equal("Just plain text.", result);
    }

    [Fact]
    public void Null_value_renders_as_empty()
    {
        var result = _renderer.Render(
            "Value is '{{val}}'.",
            new Dictionary<string, object?> { ["val"] = null });

        Assert.Equal("Value is ''.", result);
    }

    [Fact]
    public void Triple_braces_leaves_outer_braces()
    {
        // {{{x}}} → the regex matches the inner {{x}}, leaving the outer { and }
        var result = _renderer.Render(
            "{{{name}}}",
            new Dictionary<string, object?> { ["name"] = "test" });

        Assert.Equal("{test}", result);
    }

    [Fact]
    public void Same_variable_substituted_multiple_times()
    {
        var result = _renderer.Render(
            "{{x}} and {{x}} again",
            new Dictionary<string, object?> { ["x"] = "A" });

        Assert.Equal("A and A again", result);
    }
}