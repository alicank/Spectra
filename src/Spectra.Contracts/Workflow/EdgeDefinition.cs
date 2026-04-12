namespace Spectra.Contracts.Workflow;

public class EdgeDefinition
{
    public required string From { get; set; }
    public required string To { get; set; }
    public string? Condition { get; set; }  // null = always, or expression like "Context.HasMore == true"
    public bool IsLoopback { get; set; } = false;
}