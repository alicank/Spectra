namespace Spectra.Contracts.Tools;

public class ToolParameter
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Description { get; set; }
    public bool Required { get; set; }

    /// <summary>
    /// Raw JSON Schema for this parameter, preserved from MCP tool discovery.
    /// When set, providers use this instead of building schema from Type/Description.
    /// </summary>
    public string? RawSchema { get; set; }
}