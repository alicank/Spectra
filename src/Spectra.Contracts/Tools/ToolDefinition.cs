namespace Spectra.Contracts.Tools;

public class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public List<ToolParameter> Parameters { get; set; } = [];
}