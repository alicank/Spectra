namespace Spectra.Contracts.Interrupts;

/// <summary>
/// Fluent builder for constructing <see cref="InterruptRequest"/> instances.
/// Used by the <see cref="Steps.StepContext.InterruptAsync(string, Action{InterruptRequestBuilder})"/> overload.
/// </summary>
public class InterruptRequestBuilder
{
    private readonly string _runId;
    private readonly string _workflowId;
    private readonly string _nodeId;
    private readonly string _reason;

    private string? _title;
    private string? _description;
    private object? _payload;
    private readonly Dictionary<string, object?> _metadata = [];

    internal InterruptRequestBuilder(string runId, string workflowId, string nodeId, string reason)
    {
        _runId = runId;
        _workflowId = workflowId;
        _nodeId = nodeId;
        _reason = reason;
    }

    public InterruptRequestBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public InterruptRequestBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public InterruptRequestBuilder WithPayload(object? payload)
    {
        _payload = payload;
        return this;
    }

    public InterruptRequestBuilder WithMetadata(string key, object? value)
    {
        _metadata[key] = value;
        return this;
    }

    internal InterruptRequest Build() => new()
    {
        RunId = _runId,
        WorkflowId = _workflowId,
        NodeId = _nodeId,
        Reason = _reason,
        Title = _title,
        Description = _description,
        Payload = _payload,
        Metadata = new Dictionary<string, object?>(_metadata)
    };
}