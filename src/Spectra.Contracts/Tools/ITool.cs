using Spectra.Contracts.State;

namespace Spectra.Contracts.Tools;

public interface ITool
{
    string Name { get; }
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        Dictionary<string, object?> arguments,
        WorkflowState state,
        CancellationToken ct = default);
}