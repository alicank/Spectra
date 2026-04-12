namespace Spectra.Contracts.Workflow;

/// <summary>
/// Loads <see cref="WorkflowDefinition"/> instances from JSON files on disk.
/// Scans a directory for <c>*.workflow.json</c> files.
/// </summary>
public sealed class JsonFileWorkflowStore : IWorkflowStore
{
    private readonly string _directory;
    private readonly string _searchPattern;

    public JsonFileWorkflowStore(string directory, string searchPattern = "*.workflow.json")
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _searchPattern = searchPattern;

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Workflow directory not found: {directory}");
    }

    public WorkflowDefinition? Get(string name)
    {
        var path = Path.Combine(_directory, $"{name}.workflow.json");
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return WorkflowSerializer.Deserialize(json);
    }

    public IReadOnlyList<WorkflowDefinition> List()
    {
        var files = Directory.GetFiles(_directory, _searchPattern);
        var definitions = new List<WorkflowDefinition>(files.Length);

        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var def = WorkflowSerializer.TryDeserialize(json);
            if (def is not null)
                definitions.Add(def);
        }

        return definitions;
    }
}