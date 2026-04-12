using Spectra.Contracts.Interrupts;

namespace Spectra.Contracts.Steps;

public class StepResult
{
    public required StepStatus Status { get; init; }
    public Dictionary<string, object?> Outputs { get; init; } = [];
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// When <see cref="Status"/> is <see cref="StepStatus.Handoff"/>, contains
    /// the handoff details including target agent, intent, and transferred context.
    /// </summary>
    public Contracts.Workflow.AgentHandoff? Handoff { get; init; }

    public static StepResult Success(Dictionary<string, object?>? outputs = null) => new()
    {
        Status = StepStatus.Succeeded,
        Outputs = outputs ?? []
    };

    public static StepResult Fail(string message, Exception? ex = null, Dictionary<string, object?>? outputs = null) => new()
    {
        Status = StepStatus.Failed,
        ErrorMessage = message,
        Exception = ex,
        Outputs = outputs ?? []
    };


    public static StepResult NeedsContinuation(string reason, Dictionary<string, object?>? outputs = null) => new()
    {
        Status = StepStatus.NeedsContinuation,
        ErrorMessage = reason,
        Outputs = outputs ?? []
    };

    public static StepResult Interrupted(
        string reason,
        Interrupts.InterruptRequest? request = null,
        Dictionary<string, object?>? outputs = null) => new()
        {
            Status = StepStatus.Interrupted,
            ErrorMessage = reason,
            Outputs = outputs ?? []
        };

    public static StepResult HandoffTo(
        Contracts.Workflow.AgentHandoff handoff,
        Dictionary<string, object?>? outputs = null) => new()
        {
            Status = StepStatus.Handoff,
            Handoff = handoff,
            Outputs = outputs ?? []
        };

    public static StepResult AwaitingInput(
        Dictionary<string, object?>? outputs = null) => new()
        {
            Status = StepStatus.AwaitingInput,
            Outputs = outputs ?? []
        };
}
