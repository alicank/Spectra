using System.Text.Json;

namespace Spectra.Contracts.Memory;

/// <summary>
/// File-backed implementation of <see cref="IMemoryStore"/> for local development.
/// Each namespace is a subdirectory; each entry is a JSON file named by its key.
/// </summary>
public sealed class FileMemoryStore : IMemoryStore
{
    private readonly string _directory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FileMemoryStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Memory directory is required.", nameof(directory));

        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public MemoryStoreCapabilities Capabilities => new()
    {
        CanSearch = true,
        CanExpire = true,
        CanFilterByTags = true,
        CanFilterByMetadata = true
    };

    public async Task<MemoryEntry?> GetAsync(
        string @namespace, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetEntryPath(@namespace, key);
        if (!File.Exists(path)) return null;

        var entry = await ReadEntryAsync(path, cancellationToken);
        if (entry is null) return null;

        if (IsExpired(entry))
        {
            File.Delete(path);
            return null;
        }

        return entry;
    }

    public async Task SetAsync(
        string @namespace, string key, MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nsDir = GetNamespaceDirectory(@namespace);
        Directory.CreateDirectory(nsDir);

        var stamped = entry with
        {
            Key = key,
            Namespace = @namespace,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(stamped, JsonOptions);
        await File.WriteAllTextAsync(GetEntryPath(@namespace, key), json, cancellationToken);
    }

    public Task DeleteAsync(
        string @namespace, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetEntryPath(@namespace, key);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(
        string @namespace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nsDir = GetNamespaceDirectory(@namespace);
        if (!Directory.Exists(nsDir))
            return Array.Empty<MemoryEntry>();

        var entries = new List<MemoryEntry>();
        foreach (var file in Directory.GetFiles(nsDir, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await ReadEntryAsync(file, cancellationToken);
            if (entry is not null && !IsExpired(entry))
                entries.Add(entry);
        }

        return entries.OrderByDescending(e => e.UpdatedAt).ToList();
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(query.Namespace, cancellationToken);
        var candidates = all.AsEnumerable();

        if (query.IncludeExpired)
        {
            // Re-read including expired
            var nsDir = GetNamespaceDirectory(query.Namespace);
            if (Directory.Exists(nsDir))
            {
                var entries = new List<MemoryEntry>();
                foreach (var file in Directory.GetFiles(nsDir, "*.json"))
                {
                    var entry = await ReadEntryAsync(file, cancellationToken);
                    if (entry is not null) entries.Add(entry);
                }
                candidates = entries;
            }
        }

        // Filter by tags
        if (query.Tags is { Count: > 0 })
            candidates = candidates.Where(e =>
                query.Tags.All(t => e.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));

        // Filter by metadata
        if (query.MetadataFilters is { Count: > 0 })
            candidates = candidates.Where(e =>
                query.MetadataFilters.All(f =>
                    e.Metadata.TryGetValue(f.Key, out var v)
                    && string.Equals(v, f.Value, StringComparison.OrdinalIgnoreCase)));

        // Text search
        var results = candidates.Select(e =>
        {
            var score = 1.0;
            if (!string.IsNullOrEmpty(query.Text))
            {
                var keyMatch = e.Key.Contains(query.Text, StringComparison.OrdinalIgnoreCase);
                var contentMatch = e.Content.Contains(query.Text, StringComparison.OrdinalIgnoreCase);
                if (!keyMatch && !contentMatch) return null;
                score = keyMatch ? 1.0 : 0.8;
            }
            return new MemorySearchResult { Entry = e, Score = score };
        })
        .Where(r => r is not null)
        .OrderByDescending(r => r!.Score)
        .ThenByDescending(r => r!.Entry.UpdatedAt)
        .Take(query.MaxResults)
        .Cast<MemorySearchResult>()
        .ToList();

        return results;
    }

    public Task PurgeAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nsDir = GetNamespaceDirectory(@namespace);
        if (Directory.Exists(nsDir))
            Directory.Delete(nsDir, recursive: true);

        return Task.CompletedTask;
    }

    // ── Helpers ──

    private string GetNamespaceDirectory(string @namespace) =>
        Path.Combine(_directory, SanitizePath(@namespace));

    private string GetEntryPath(string @namespace, string key) =>
        Path.Combine(_directory, SanitizePath(@namespace), $"{SanitizePath(key)}.json");

    private static string SanitizePath(string segment) =>
        segment.Replace('/', '_').Replace('\\', '_');

    private static async Task<MemoryEntry?> ReadEntryAsync(
        string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<MemoryEntry>(json, JsonOptions);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static bool IsExpired(MemoryEntry entry) =>
        entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTimeOffset.UtcNow;
}