namespace Spectra.Contracts.Memory;

/// <summary>
/// Helpers for building well-known namespace strings.
/// Namespaces are dot-separated paths that scope memory entries.
/// <code>
/// MemoryNamespace.Global                          → "global"
/// MemoryNamespace.ForUser("alice")                → "user.alice"
/// MemoryNamespace.ForWorkflow("my-flow")          → "workflow.my-flow"
/// MemoryNamespace.Compose("tenant", "acme", "user", "alice") → "tenant.acme.user.alice"
/// </code>
/// </summary>
public static class MemoryNamespace
{
    public const string Global = "global";

    public static string ForUser(string userId) => $"user.{Sanitize(userId)}";

    public static string ForWorkflow(string workflowId) => $"workflow.{Sanitize(workflowId)}";

    public static string ForRun(string runId) => $"run.{Sanitize(runId)}";

    /// <summary>
    /// Composes an arbitrary namespace from alternating label/value segments.
    /// <c>Compose("org", "acme", "team", "eng")</c> → <c>"org.acme.team.eng"</c>
    /// </summary>
    public static string Compose(params string[] segments)
    {
        if (segments.Length == 0)
            throw new ArgumentException("At least one segment is required.", nameof(segments));

        return string.Join('.', segments.Select(Sanitize));
    }

    /// <summary>
    /// Validates that a namespace string follows the dotted convention.
    /// </summary>
    public static bool IsValid(string @namespace) =>
        !string.IsNullOrWhiteSpace(@namespace)
        && @namespace.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
        && !@namespace.StartsWith('.')
        && !@namespace.EndsWith('.')
        && !@namespace.Contains("..");

    private static string Sanitize(string segment) =>
        string.IsNullOrWhiteSpace(segment)
            ? throw new ArgumentException("Namespace segment cannot be empty.")
            : segment.Replace(' ', '-').ToLowerInvariant();
}