using System.Text.Json;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class FileAgentLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public FileAgentLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spectra_agent_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadFromDirectory_ReturnsAllAgents()
    {
        WriteAgentFile("researcher.agent.json", new { id = "researcher", provider = "openai", model = "gpt-4o" });
        WriteAgentFile("coder.agent.json", new { id = "coder", provider = "anthropic", model = "claude-sonnet" });

        var agents = FileAgentLoader.LoadFromDirectory(_tempDir);

        Assert.Equal(2, agents.Count);
        Assert.Contains(agents, a => a.Id == "researcher");
        Assert.Contains(agents, a => a.Id == "coder");
    }

    [Fact]
    public void LoadFromDirectory_DeserializesAllProperties()
    {
        WriteAgentFile("full.agent.json", new
        {
            id = "full-agent",
            provider = "openai",
            model = "gpt-4o",
            temperature = 0.3,
            maxTokens = 4096,
            systemPrompt = "You are helpful.",
            systemPromptRef = "agents/full",
            handoffTargets = new[] { "coder", "reviewer" },
            escalationTarget = "human"
        });

        var agents = FileAgentLoader.LoadFromDirectory(_tempDir);
        var agent = Assert.Single(agents);

        Assert.Equal("full-agent", agent.Id);
        Assert.Equal("openai", agent.Provider);
        Assert.Equal("gpt-4o", agent.Model);
        Assert.Equal(0.3, agent.Temperature);
        Assert.Equal(4096, agent.MaxTokens);
        Assert.Equal("You are helpful.", agent.SystemPrompt);
        Assert.Equal("agents/full", agent.SystemPromptRef);
        Assert.Equal(2, agent.HandoffTargets.Count);
        Assert.Equal("human", agent.EscalationTarget);
    }

    [Fact]
    public void LoadFromDirectory_IgnoresNonAgentJsonFiles()
    {
        WriteAgentFile("researcher.agent.json", new { id = "researcher", provider = "openai", model = "gpt-4o" });
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# Agents");
        File.WriteAllText(Path.Combine(_tempDir, "workflow.json"), "{}");

        var agents = FileAgentLoader.LoadFromDirectory(_tempDir);

        Assert.Single(agents);
    }

    [Fact]
    public void LoadFromDirectory_EmptyDirectory_ReturnsEmpty()
    {
        var agents = FileAgentLoader.LoadFromDirectory(_tempDir);
        Assert.Empty(agents);
    }

    [Fact]
    public void LoadFromDirectory_ThrowsOnMissingDirectory()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => FileAgentLoader.LoadFromDirectory("/nonexistent/path"));
    }

    [Fact]
    public void LoadFromFile_ThrowsOnMissingFile()
    {
        Assert.Throws<FileNotFoundException>(
            () => FileAgentLoader.LoadFromFile("/nonexistent/file.agent.json"));
    }

    [Fact]
    public void LoadFromFile_ThrowsOnInvalidJson()
    {
        var file = Path.Combine(_tempDir, "bad.agent.json");
        File.WriteAllText(file, "not json at all");

        Assert.ThrowsAny<JsonException>(
            () => FileAgentLoader.LoadFromFile(file));
    }

    [Fact]
    public void LoadFromFile_SupportsCommentsAndTrailingCommas()
    {
        var file = Path.Combine(_tempDir, "comments.agent.json");
        File.WriteAllText(file, """
        {
            // This is a comment
            "id": "test",
            "provider": "openai",
            "model": "gpt-4o",
        }
        """);

        var agent = FileAgentLoader.LoadFromFile(file);
        Assert.Equal("test", agent.Id);
    }

    [Fact]
    public void LoadFromDirectory_IsCaseInsensitiveOnProperties()
    {
        WriteAgentFile("upper.agent.json", new
        {
            Id = "upper-agent",
            Provider = "openai",
            Model = "gpt-4o"
        });

        var agents = FileAgentLoader.LoadFromDirectory(_tempDir);
        var agent = Assert.Single(agents);
        Assert.Equal("upper-agent", agent.Id);
    }

    private void WriteAgentFile(string fileName, object content)
    {
        var json = JsonSerializer.Serialize(content, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(Path.Combine(_tempDir, fileName), json);
    }
}