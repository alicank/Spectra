using Spectra.Contracts.Prompts;

namespace Spectra.Kernel.Prompts;

/// <summary>
/// Loads <see cref="PromptTemplate"/> instances from <c>.md</c> files in a directory.
/// Supports optional file watching for automatic reload on change.
/// </summary>
public sealed class FilePromptRegistry : IPromptRegistry, IDisposable
{
    private readonly string _directory;
    private readonly string _searchPattern;
    private readonly Dictionary<string, PromptTemplate> _prompts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;

    public FilePromptRegistry(string directory, string searchPattern = "*.md", bool watch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = Path.GetFullPath(directory);
        _searchPattern = searchPattern;

        if (!Directory.Exists(_directory))
            throw new DirectoryNotFoundException($"Prompt directory not found: {_directory}");

        LoadAll();

        if (watch)
            StartWatching();
    }

    public PromptTemplate? GetPrompt(string promptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptId);
        lock (_lock)
        {
            return _prompts.GetValueOrDefault(promptId);
        }
    }

    public IReadOnlyList<PromptTemplate> GetAll()
    {
        lock (_lock)
        {
            return _prompts.Values.ToList().AsReadOnly();
        }
    }

    public void Register(PromptTemplate prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        lock (_lock)
        {
            _prompts[prompt.Id] = prompt;
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _prompts.Clear();
            LoadAll();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    // ── Private ────────────────────────────────────────────────────

    private void LoadAll()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, _searchPattern, SearchOption.AllDirectories))
            LoadFile(file);
    }

    private void LoadFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var raw = File.ReadAllText(filePath);
        var (meta, body) = FrontMatterParser.Parse(raw);

        var id = DeriveId(filePath);

        var template = new PromptTemplate
        {
            Id = GetMetaString(meta, "id") ?? id,
            Content = body,
            Name = GetMetaString(meta, "name"),
            Description = GetMetaString(meta, "description"),
            Version = GetMetaString(meta, "version"),
            Variables = GetMetaStringList(meta, "variables"),
            Metadata = BuildMetadata(meta),
            FilePath = filePath,
            LoadedAt = DateTimeOffset.UtcNow
        };

        _prompts[template.Id] = template;
    }

    private void RemoveByPath(string filePath)
    {
        var id = DeriveId(filePath);
        _prompts.Remove(id);

        // Also remove if registered under a custom id that came from front-matter.
        var toRemove = _prompts
            .Where(kv => string.Equals(kv.Value.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
            _prompts.Remove(key);
    }

    private string DeriveId(string filePath)
    {
        var relative = Path.GetRelativePath(_directory, filePath);
        // Remove extension and normalize separators to '/'
        var id = Path.ChangeExtension(relative, null)
                     .Replace(Path.DirectorySeparatorChar, '/');
        return id;
    }

    // ── File watching ──────────────────────────────────────────────

    private void StartWatching()
    {
        _watcher = new FileSystemWatcher(_directory, _searchPattern)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            LoadFile(e.FullPath);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            RemoveByPath(e.FullPath);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            RemoveByPath(e.OldFullPath);
            LoadFile(e.FullPath);
        }
    }

    // ── Meta helpers ───────────────────────────────────────────────

    private static string? GetMetaString(Dictionary<string, object>? meta, string key)
    {
        if (meta is null) return null;
        return meta.TryGetValue(key, out var val) ? val.ToString() : null;
    }

    private static List<string> GetMetaStringList(Dictionary<string, object>? meta, string key)
    {
        if (meta is null) return [];
        if (!meta.TryGetValue(key, out var val)) return [];
        return val is List<string> list ? list : [];
    }

    private static Dictionary<string, object?> BuildMetadata(Dictionary<string, object>? meta)
    {
        if (meta is null) return [];

        // Known keys handled by dedicated properties — exclude from generic metadata.
        HashSet<string> reserved = ["id", "name", "description", "version", "variables"];
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in meta)
        {
            if (!reserved.Contains(key.ToLowerInvariant()))
                result[key] = value;
        }
        return result;
    }
}