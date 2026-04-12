using System.Text.Json;
using System.Text.Json.Serialization;
using Spectra.Contracts.Workflow;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Loads <see cref="AgentDefinition"/> instances from .agent.json files on disk.
/// </summary>
public static class FileAgentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Scans <paramref name="directory"/> for all *.agent.json files and
    /// deserializes each into an <see cref="AgentDefinition"/>.
    /// </summary>
    /// <param name="directory">The directory to scan.</param>
    /// <param name="searchPattern">The file glob to match. Defaults to <c>*.agent.json</c>.</param>
    /// <returns>A list of loaded agent definitions.</returns>
    /// <exception cref="DirectoryNotFoundException">If <paramref name="directory"/> does not exist.</exception>
    /// <exception cref="InvalidOperationException">If a file cannot be deserialized.</exception>
    public static List<AgentDefinition> LoadFromDirectory(string directory, string searchPattern = "*.agent.json")
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Agent directory not found: {directory}");

        var agents = new List<AgentDefinition>();

        foreach (var file in Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            var agent = LoadFromFile(file);
            agents.Add(agent);
        }

        return agents;
    }

    /// <summary>
    /// Loads a single <see cref="AgentDefinition"/> from a JSON file.
    /// </summary>
    public static AgentDefinition LoadFromFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Agent file not found: {filePath}", filePath);

        var json = File.ReadAllText(filePath);

        var agent = JsonSerializer.Deserialize<AgentDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize agent from {filePath}");

        return agent;
    }
}