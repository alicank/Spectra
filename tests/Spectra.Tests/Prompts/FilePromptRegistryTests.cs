using Spectra.Contracts.Prompts;
using Spectra.Kernel.Prompts;
using Xunit;

namespace Spectra.Tests.Prompts;

public class FilePromptRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public FilePromptRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spectra-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Loads_prompt_from_file()
    {
        WritePrompt("analyze.md", """
            ---
            name: Code Analysis
            description: Analyzes code
            version: "1.0"
            variables:
              - language
              - code
            ---
            You are a {{language}} expert.

            {{code}}
            """);

        using var registry = new FilePromptRegistry(_tempDir);
        var prompt = registry.GetPrompt("analyze");

        Assert.NotNull(prompt);
        Assert.Equal("analyze", prompt.Id);
        Assert.Equal("Code Analysis", prompt.Name);
        Assert.Equal("Analyzes code", prompt.Description);
        Assert.Equal("1.0", prompt.Version);
        Assert.Equal(["language", "code"], prompt.Variables);
        Assert.Contains("{{language}}", prompt.Content);
        Assert.DoesNotContain("---", prompt.Content);
        Assert.NotNull(prompt.FilePath);
        Assert.NotNull(prompt.LoadedAt);
    }

    [Fact]
    public void Loads_file_without_front_matter()
    {
        WritePrompt("simple.md", "Just a plain prompt with no front matter.");

        using var registry = new FilePromptRegistry(_tempDir);
        var prompt = registry.GetPrompt("simple");

        Assert.NotNull(prompt);
        Assert.Equal("simple", prompt.Id);
        Assert.Equal("Just a plain prompt with no front matter.", prompt.Content);
        Assert.Null(prompt.Name);
        Assert.Empty(prompt.Variables);
    }

    [Fact]
    public void Custom_id_from_front_matter_overrides_filename()
    {
        WritePrompt("my-file.md", """
            ---
            id: custom-id
            name: Custom
            ---
            Content here.
            """);

        using var registry = new FilePromptRegistry(_tempDir);

        Assert.NotNull(registry.GetPrompt("custom-id"));
    }

    [Fact]
    public void Loads_from_subdirectories()
    {
        var subDir = Path.Combine(_tempDir, "tasks");
        Directory.CreateDirectory(subDir);

        WritePrompt(Path.Combine("tasks", "review.md"), """
            ---
            name: Review
            ---
            Review the code.
            """);

        using var registry = new FilePromptRegistry(_tempDir);
        var prompt = registry.GetPrompt("tasks/review");

        Assert.NotNull(prompt);
    }

    [Fact]
    public void GetAll_returns_all_loaded_prompts()
    {
        WritePrompt("a.md", "Prompt A");
        WritePrompt("b.md", "Prompt B");

        using var registry = new FilePromptRegistry(_tempDir);

        Assert.Equal(2, registry.GetAll().Count);
    }

    [Fact]
    public void GetPrompt_returns_null_for_unknown_id()
    {
        using var registry = new FilePromptRegistry(_tempDir);

        Assert.Null(registry.GetPrompt("nonexistent"));
    }

    [Fact]
    public void Reload_picks_up_changes()
    {
        WritePrompt("mutable.md", "Version 1");

        using var registry = new FilePromptRegistry(_tempDir);
        Assert.Equal("Version 1", registry.GetPrompt("mutable")!.Content);

        WritePrompt("mutable.md", "Version 2");
        registry.Reload();

        Assert.Equal("Version 2", registry.GetPrompt("mutable")!.Content);
    }

    [Fact]
    public void Register_adds_prompt_manually()
    {
        using var registry = new FilePromptRegistry(_tempDir);

        registry.Register(new PromptTemplate
        {
            Id = "manual",
            Content = "Manually registered"
        });

        Assert.NotNull(registry.GetPrompt("manual"));
    }

    [Fact]
    public void Metadata_captures_extra_front_matter_keys()
    {
        WritePrompt("rich.md", """
            ---
            name: Rich
            version: "2.0"
            author: alican
            tags: experimental
            ---
            Content.
            """);

        using var registry = new FilePromptRegistry(_tempDir);
        var prompt = registry.GetPrompt("rich")!;

        Assert.Equal("alican", prompt.Metadata["author"]?.ToString());
        Assert.Equal("experimental", prompt.Metadata["tags"]?.ToString());
        Assert.False(prompt.Metadata.ContainsKey("name"));  // reserved, not in metadata
    }

    [Fact]
    public async Task Watch_detects_new_file()
    {
        WritePrompt("initial.md", "Initial");

        using var registry = new FilePromptRegistry(_tempDir, watch: true);
        Assert.Single(registry.GetAll());

        // Write a new file — watcher should pick it up.
        WritePrompt("added.md", "Added at runtime");
        await Task.Delay(500); // Allow watcher to fire.

        Assert.NotNull(registry.GetPrompt("added"));
    }

    [Fact]
    public void Throws_for_nonexistent_directory()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new FilePromptRegistry(Path.Combine(_tempDir, "nope")));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private void WritePrompt(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Dedent: remove common leading whitespace (for inline test strings).
        var lines = content.Split('\n');
        var minIndent = lines
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var dedented = string.Join('\n', lines.Select(l =>
            l.Length >= minIndent ? l[minIndent..] : l));

        File.WriteAllText(fullPath, dedented.Trim());
    }
}